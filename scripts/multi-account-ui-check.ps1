param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$SourceDatabase = (Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts',
    [switch]$KeepSandbox
)
$ErrorActionPreference = 'Stop'

# The run only ever touches a throwaway copy of the live database, and the first thing it does
# inside that copy is engage the kill switch, so the auto runner can never transmit for real.
if (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue) {
    throw 'another TelegramControlStudio instance is running; close it before the sandbox run'
}
if (-not (Test-Path $SourceDatabase)) { throw "source database not found: $SourceDatabase" }
$Database = Join-Path $env:TEMP ('tcs-multi-account-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.db')
foreach ($suffix in @('', '-wal', '-shm')) {
    $sidecar = $SourceDatabase + $suffix
    if (Test-Path $sidecar) { Copy-Item $sidecar ($Database + $suffix) -Force }
}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$signature = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApi2 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out WinRect r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr dialog, int controlId);
    public static string Text(IntPtr handle) {
        var builder = new StringBuilder(256);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }
    public static IntPtr ClickButtonByCaption(IntPtr dialog, string[] captions) {
        for (var button = FindWindowEx(dialog, IntPtr.Zero, "Button", null); button != IntPtr.Zero; button = FindWindowEx(dialog, button, "Button", null)) {
            var caption = Text(button);
            foreach (var captionWanted in captions) {
                if (caption == captionWanted) {
                    SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
                    return button;
                }
            }
        }
        return IntPtr.Zero;
    }
    public static IntPtr ClickDefaultButton(IntPtr dialog) {
        var value = SendMessage(dialog, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64(); // DM_GETDEFID
        if (((value >> 16) & 0xFFFF) != 0x4B42) return IntPtr.Zero;                  // 'KB' magic
        var item = GetDlgItem(dialog, (int)(value & 0xFFFF));
        if (item == IntPtr.Zero) return IntPtr.Zero;
        SendMessage(item, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
        return item;
    }
    public static string FirstButtonText(IntPtr dialog) {
        var button = FindWindowEx(dialog, IntPtr.Zero, "Button", null);
        return button == IntPtr.Zero ? "" : Text(button);
    }
    public static string ButtonCaptions(IntPtr dialog) {
        var captions = new StringBuilder();
        for (var button = FindWindowEx(dialog, IntPtr.Zero, "Button", null); button != IntPtr.Zero; button = FindWindowEx(dialog, button, "Button", null)) {
            if (captions.Length > 0) captions.Append('|');
            captions.Append(Text(button));
        }
        return captions.ToString();
    }
    public static string StaticTexts(IntPtr dialog) {
        var texts = new StringBuilder();
        for (var staticText = FindWindowEx(dialog, IntPtr.Zero, "Static", null); staticText != IntPtr.Zero; staticText = FindWindowEx(dialog, staticText, "Static", null)) {
            var value = Text(staticText);
            if (value.Length == 0) continue;
            if (texts.Length > 0) texts.Append(" | ");
            texts.Append(value);
        }
        return texts.ToString();
    }
    public static IntPtr ClickNthButton(IntPtr dialog, int index) {
        var found = 0;
        for (var button = FindWindowEx(dialog, IntPtr.Zero, "Button", null); button != IntPtr.Zero; button = FindWindowEx(dialog, button, "Button", null)) {
            if (found++ == index) {
                SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
                return button;
            }
        }
        return IntPtr.Zero;
    }
}
'@
Add-Type -TypeDefinition $signature

$log = Join-Path $OutDir 'multi-account-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) {
    Add-Content -Path $log -Value $message -Encoding UTF8
}

$auto = [System.Windows.Automation.AutomationElement]

function Get-AutomationWindow([datetime]$deadline) {
    while ((Get-Date) -lt $deadline) {
        $window = $auto::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Telegram Control Studio')))
        if ($null -ne $window) { return $window }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Find-AutomationId($parent, [string]$id) {
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}

function Get-AllText($parent) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $names = @()
    foreach ($element in $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($element.Current.Name) { $names += $element.Current.Name }
    }
    return $names
}

function Get-AllButton($parent) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    return $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-AppListItems($process, $condition) {
    $items = @()
    foreach ($item in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Subtree, $condition)) {
        if ($item.Current.ProcessId -eq $process.Id) { $items += $item }
    }
    return $items
}

function Invoke-Button($button) {
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Save-WindowShot($process, [string]$path) {
    $process.Refresh()
    $handle = $process.MainWindowHandle
    [void][WinApi2]::ShowWindow($handle, 9)
    Start-Sleep -Milliseconds 800
    $rect = New-Object WinRect
    [void][WinApi2]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][WinApi2]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

function Log-Header([string]$label) {
    $window = Get-AutomationWindow ((Get-Date).AddSeconds(5))
    if ($null -eq $window) { Log "STEP=$label WINDOW=missing"; return }
    Log "STEP=$label TEXTS=$((Get-AllText $window) -join ' / ')"
}

function Get-ModalWindows($process) {
    $candidates = @()
    foreach ($child in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $process.Id -and $child.Current.Name -ne 'Telegram Control Studio') { $candidates += $child }
    }
    $main = Get-AutomationWindow ((Get-Date).AddSeconds(3))
    if ($null -ne $main) {
        $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
        foreach ($inner in $main.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition)) { $candidates += $inner }
    }
    Log ("MODAL_WINDOWS=" + (@($candidates | ForEach-Object { "'$($_.Current.Name)'" }) -join ', '))
    return $candidates
}

function Invoke-Acceptable($element) {
    try {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        return $true
    } catch { }
    try {
        $legacy = $element.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
        $legacy.DoDefaultAction()
        return $true
    } catch { return $false }
}

function Close-ModalWindows($process) {
    $accept = @('ปิด', 'OK', 'ตกลง', 'Yes', 'ใช่')
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        $modal = @(Get-ModalWindows $process) | Select-Object -First 1
        if ($null -eq $modal) { return }

        $handle = [IntPtr]$modal.Current.NativeWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            Log "MODAL='$($modal.Current.Name)' TEXTS=$((Get-AllText $modal) -join ' / ') WIN32_TEXT=$([WinApi2]::StaticTexts($handle)) BUTTONS=$([WinApi2]::ButtonCaptions($handle))"
            $clicked = [WinApi2]::ClickButtonByCaption($handle, $accept)
            if ($clicked -ne [IntPtr]::Zero) {
                Log "MODAL='$($modal.Current.Name)' WIN32_CLICK='$([WinApi2]::Text($clicked))'"
                Start-Sleep -Seconds 2
                continue
            }
            $default = [WinApi2]::ClickDefaultButton($handle)
            if ($default -ne [IntPtr]::Zero) {
                Log "MODAL='$($modal.Current.Name)' WIN32_DEFAULT='$([WinApi2]::Text($default))'"
                Start-Sleep -Seconds 2
                continue
            }
            $count = @(([WinApi2]::ButtonCaptions($handle) -split '\|') | Where-Object { $_ }).Count
            if ($count -eq 2) {
                $first = [WinApi2]::ClickNthButton($handle, 0)
                if ($first -ne [IntPtr]::Zero) {
                    Log "MODAL='$($modal.Current.Name)' WIN32_FIRST='$([WinApi2]::Text($first))'"
                    Start-Sleep -Seconds 2
                    continue
                }
            }
        }

        $chosen = $null
        foreach ($descendant in $modal.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($accept -contains $descendant.Current.Name) { $chosen = $descendant; break }
        }
        if ($null -eq $chosen -or -not (Invoke-Acceptable $chosen)) {
            Log "MODAL='$($modal.Current.Name)' NO_ACCEPTABLE_BUTTON"
            return
        }
        Log "MODAL='$($modal.Current.Name)' UIA_CLICK='$($chosen.Current.Name)'"
        Start-Sleep -Seconds 2
    }
}

$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

try {
    $process = Start-Process $Exe -PassThru
    $window = Get-AutomationWindow ((Get-Date).AddSeconds(40))
    if ($null -eq $window) { throw 'main window not found' }
    Start-Sleep -Seconds 3

    Log-Header 'launch'
    Save-WindowShot $process (Join-Path $OutDir 'ui-accounts-before.png')

    $emergency = Find-AutomationId $window 'EmergencyStopButton'
    if ($null -eq $emergency) { throw 'EmergencyStopButton not found' }
    Invoke-Button $emergency
    Start-Sleep -Seconds 2
    Close-ModalWindows $process
    Start-Sleep -Seconds 2
    if (((Get-AllText $window) -join ' / ') -notmatch 'KILL SWITCH') {
        throw 'sandbox is not isolated: the kill switch badge never appeared'
    }
    Log 'STEP=kill-switch STATUS=engaged'

    $combo = Find-AutomationId $window 'AccountSwitchBox'
    if ($null -eq $combo) { throw 'AccountSwitchBox not found' }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $listCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = @(Get-AppListItems $process $listCondition)
    $waited = 0
    while ($items.Count -eq 0 -and $waited -lt 8) {
        Start-Sleep -Seconds 1
        $waited++
        $items = @(Get-AppListItems $process $listCondition)
    }
    Log "DROPDOWN_ITEMS=$($items.Count) WAITED=$waited"
    foreach ($item in $items) { Log "  ITEM=$($item.Current.Name)" }
    Save-WindowShot $process (Join-Path $OutDir 'ui-accounts-dropdown.png')

    $target = $null
    foreach ($item in $items) { if ($item.Current.Name -like '*jimmy*') { $target = $item } }
    if ($null -eq $target) { throw 'jimmy row not found' }
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 3
    Log-Header 'after-switch'
    Save-WindowShot $process (Join-Path $OutDir 'ui-accounts-switched.png')

    $connectButton = Find-AutomationId $window 'ConnectAccountButton'
    if ($null -eq $connectButton) { throw 'ConnectAccountButton not found' }
    Invoke-Button $connectButton
    Start-Sleep -Seconds 15
    Close-ModalWindows $process
    Start-Sleep -Seconds 3
    Log-Header 'after-connect'
    Save-WindowShot $process (Join-Path $OutDir 'ui-accounts-connected.png')

    # The button should now offer to close the session instead of signing in.
    $connectButton = Find-AutomationId $window 'ConnectAccountButton'
    Log "STEP=button-state LABEL=$((Get-AllText $connectButton) -join '')"
    Invoke-Button $connectButton
    Start-Sleep -Seconds 2
    Close-ModalWindows $process
    Start-Sleep -Seconds 3
    Log-Header 'after-disconnect'
    Save-WindowShot $process (Join-Path $OutDir 'ui-accounts-disconnected.png')

    # A released session lock must be re-acquirable in the same window.
    $connectButton = Find-AutomationId $window 'ConnectAccountButton'
    Invoke-Button $connectButton
    Start-Sleep -Seconds 15
    Close-ModalWindows $process
    Start-Sleep -Seconds 2
    Log-Header 'after-reconnect'
    Save-WindowShot $process (Join-Path $OutDir 'ui-accounts-reconnected.png')
}
finally {
    Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    if ($KeepSandbox) {
        Log "SANDBOX_KEPT=$Database"
    } else {
        foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item ($Database + $suffix) -Force -ErrorAction SilentlyContinue }
        Log 'SANDBOX_REMOVED=true'
    }
}
