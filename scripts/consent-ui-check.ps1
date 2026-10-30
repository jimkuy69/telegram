param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$Database = (Join-Path $env:TEMP 'tcs-consent-ui.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts',
    [switch]$KeepSandbox,
    [switch]$NoKillSwitch
)
$ErrorActionPreference = 'Stop'

# This run only ever opens a throwaway copy of the live database, and the first thing it does inside
# that copy is engage the kill switch, so no opt-in request can leave the machine.
if (-not $Database.StartsWith((Join-Path $env:TEMP 'tcs-'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "refusing to run against $Database; point -Database at a sandbox copy under TEMP named tcs-*"
}
if (-not (Test-Path $Database)) { throw "sandbox database not found: $Database (seed it first)" }
if (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue) {
    throw 'another TelegramControlStudio instance is running; close it before the sandbox run'
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
public static class WinApi3 {
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
        return builder.ToString().Replace("&", "");
    }
    public static string StaticTexts(IntPtr dialog) {
        var texts = new StringBuilder();
        for (var value = FindWindowEx(dialog, IntPtr.Zero, "Static", null); value != IntPtr.Zero; value = FindWindowEx(dialog, value, "Static", null)) {
            var text = Text(value);
            if (text.Length == 0) continue;
            if (texts.Length > 0) texts.Append(" | ");
            texts.Append(text);
        }
        return texts.ToString();
    }
    public static string ButtonCaptions(IntPtr dialog) {
        var captions = new StringBuilder();
        for (var button = FindWindowEx(dialog, IntPtr.Zero, "Button", null); button != IntPtr.Zero; button = FindWindowEx(dialog, button, "Button", null)) {
            if (captions.Length > 0) captions.Append('|');
            captions.Append(Text(button));
        }
        return captions.ToString();
    }
    public static IntPtr ClickButtonByCaption(IntPtr dialog, string[] captions) {
        for (var button = FindWindowEx(dialog, IntPtr.Zero, "Button", null); button != IntPtr.Zero; button = FindWindowEx(dialog, button, "Button", null)) {
            foreach (var wanted in captions) {
                if (Text(button) == wanted) {
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
}
'@
Add-Type -TypeDefinition $signature

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'consent-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

$auto = [System.Windows.Automation.AutomationElement]
$treeChildren = [System.Windows.Automation.TreeScope]::Children
$treeDescendants = [System.Windows.Automation.TreeScope]::Descendants
$appWindows = @('Telegram Control Studio', 'ขอความยินยอม (Opt-in)', 'Consent Inbox', 'Settings', 'Queue & History')

function Find-Window([string]$name) {
    # Owned dialogs show up either as a root child or nested under the main window, so both are searched.
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $candidates = @($auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition))
        foreach ($child in $candidates) {
            if ($child.Current.Name -eq $name) { return $child }
        }
        $main = $auto::RootElement.FindFirst($treeChildren,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Telegram Control Studio')))
        if ($null -ne $main -and $main.Current.Name -ne $name) {
            foreach ($inner in $main.FindAll($treeChildren, $windowCondition)) {
                if ($inner.Current.Name -eq $name) { return $inner }
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Find-AutomationId($parent, [string]$id) {
    return $parent.FindFirst($treeDescendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}

function Get-AllText($parent) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $names = @()
    foreach ($element in $parent.FindAll($treeDescendants, $condition)) {
        if ($element.Current.Name) { $names += $element.Current.Name }
    }
    return $names
}

function Get-Button([string]$windowName, [string]$buttonName) {
    $window = Find-Window $windowName
    if ($null -eq $window) { throw "window '$windowName' not found" }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $found = @()
    foreach ($button in $window.FindAll($treeDescendants, $condition)) {
        if ($button.Current.Name -eq $buttonName) { return $button }
        $found += $button.Current.Name
    }
    Log "BUTTONS_IN='$windowName' WANTED='$buttonName' HAVE=$($found -join ' | ')"
    throw "button '$buttonName' not found in '$windowName'"
}

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Log-Window([string]$label, [string]$windowName) {
    $window = Find-Window $windowName
    if ($null -eq $window) { Log "STEP=$label WINDOW='$windowName' MISSING"; return }
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    Log "STEP=$label WINDOW='$windowName' TEXTS=$(((Get-AllText $window) -join ' / '))"
    Log "STEP=$label WIN32_TEXTS=$([WinApi3]::StaticTexts($handle))"
}

function Save-WindowShot([string]$windowName, [string]$fileName) {
    $window = Find-Window $windowName
    if ($null -eq $window) { Log "SHOT_SKIPPED='$windowName'"; return }
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][WinApi3]::ShowWindow($handle, 9)
    Start-Sleep -Milliseconds 600
    $rect = New-Object WinRect
    [void][WinApi3]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][WinApi3]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $path = Join-Path $OutDir $fileName
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

function Get-DialogWindows([int]$processId) {
    # A WPF MessageBox shows up either as a root child or nested inside the main window's tree, so
    # both places have to be searched before a modal can be dismissed.
    $candidates = @()
    foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -ne $processId) { continue }
        $candidates += $child
    }
    $main = Find-Window 'Telegram Control Studio'
    if ($null -ne $main) {
        $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
        foreach ($inner in $main.FindAll($treeChildren, $windowCondition)) { $candidates += $inner }
    }
    $dialogs = @()
    foreach ($candidate in $candidates) {
        if ($appWindows -contains $candidate.Current.Name) { continue }
        $dialogs += $candidate
    }
    return $dialogs
}

function Close-MessageBoxes([int]$processId) {
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $boxes = @(Get-DialogWindows $processId)
        if ($boxes.Count -eq 0) { return }
        $box = $boxes[0]
        $handle = [IntPtr]$box.Current.NativeWindowHandle
        Log "BOX='$($box.Current.Name)' TEXTS=$([WinApi3]::StaticTexts($handle)) BUTTONS=$([WinApi3]::ButtonCaptions($handle)) UIA=$(((Get-AllText $box) -join ' / '))"
        $clicked = [WinApi3]::ClickButtonByCaption($handle, @('OK', 'ตกลง', 'Yes', 'ใช่', 'ปิด'))
        if ($clicked -eq [IntPtr]::Zero) { $clicked = [WinApi3]::ClickDefaultButton($handle) }
        if ($clicked -eq [IntPtr]::Zero) { Log "BOX='$($box.Current.Name)' NO_BUTTON"; return }
        Log "BOX_CLOSE='$([WinApi3]::Text($clicked))'"
        Start-Sleep -Seconds 2
    }
}

$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
$process = $null
try {
    $process = Start-Process $Exe -PassThru
    $main = Find-Window 'Telegram Control Studio'
    if ($null -eq $main) { throw 'main window not found' }
    Start-Sleep -Seconds 3
    Log "DATABASE=$Database"
    Log-Window 'launch' 'Telegram Control Studio'
    $badge = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
    if ($NoKillSwitch) {
        if ($badge -notmatch 'TEST MODE') { throw 'sandbox is not isolated: test mode is off and the kill switch was skipped' }
        if ($badge -match 'เชื่อมต่อ [1-9]') { throw 'sandbox is not isolated: a Telegram session is connected' }
        Log 'STEP=kill-switch SKIPPED (test mode on, no connected session, job never confirmed)'
    } else {
        if ($badge -notmatch 'KILL SWITCH') {
            $emergency = Find-AutomationId (Find-Window 'Telegram Control Studio') 'EmergencyStopButton'
            if ($null -eq $emergency) { throw 'EmergencyStopButton not found' }
            Invoke-Element $emergency
            Start-Sleep -Seconds 2
            Close-MessageBoxes $process.Id
            Start-Sleep -Seconds 2
            $badge = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
            if ($badge -notmatch 'KILL SWITCH') { throw 'sandbox is not isolated: the kill switch badge never appeared' }
        }
        Log 'STEP=kill-switch STATUS=engaged'
    }

    # The first account in the list is an empty placeholder, so the seeded account has to be picked.
    $launchTexts = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
    if ($launchTexts -notmatch 'jimmy') {
        $combo = Find-AutomationId (Find-Window 'Telegram Control Studio') 'AccountSwitchBox'
        if ($null -eq $combo) { throw 'AccountSwitchBox not found' }
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $listCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
        $items = @()
        $waited = 0
        while ($items.Count -eq 0 -and $waited -lt 8) {
            Start-Sleep -Seconds 1
            $waited++
            foreach ($item in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Subtree, $listCondition)) {
                if ($item.Current.ProcessId -eq $process.Id) { $items += $item }
            }
        }
        $target = $null
        foreach ($item in $items) { if ($item.Current.Name -like '*jimmy*') { $target = $item } }
        if ($null -eq $target) { throw "jimmy row not found (items=$($items.Count))" }
        $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Seconds 4
        Log "STEP=account-switch ITEM='$($target.Current.Name)'"
    } else {
        Log 'STEP=account-switch SKIPPED (jimmy already active)'
    }
    Close-MessageBoxes $process.Id
    Save-WindowShot 'Telegram Control Studio' 'ui-consent-main.png'

    Invoke-Element (Get-Button 'Telegram Control Studio' 'เลือกทั้งหมด')
    Start-Sleep -Seconds 2
    Invoke-Element (Get-Button 'Telegram Control Studio' 'ขอยินยอม (Opt-in)')
    Start-Sleep -Seconds 3
    $names = @()
    foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $process.Id) { $names += "'$($child.Current.Name)'" }
    }
    Log "APP_WINDOWS=$($names -join ', ')"
    $plan = Find-Window 'ขอความยินยอม (Opt-in)'
    if ($null -eq $plan) { throw 'consent request window never opened' }
    Start-Sleep -Seconds 3
    Log-Window 'request-plan' 'ขอความยินยอม (Opt-in)'
    Save-WindowShot 'ขอความยินยอม (Opt-in)' 'ui-consent-plan.png'

    # With the kill switch engaged nothing is eligible, and the queue button has to stay disabled.
    # The confirmation-box refusal only runs when at least one recipient can be queued.
    $queueButton = Get-Button 'ขอความยินยอม (Opt-in)' 'สร้างคิวคำขอ'
    if (-not $queueButton.Current.IsEnabled) {
        Log 'STEP=queue-button STATUS=disabled (fail-closed while the kill switch is engaged)'
    } else {
        Invoke-Element $queueButton
        Start-Sleep -Seconds 2
        Close-MessageBoxes $process.Id
        Log-Window 'after-unconfirmed-click' 'ขอความยินยอม (Opt-in)'

        $confirm = Find-AutomationId (Find-Window 'ขอความยินยอม (Opt-in)') 'ConfirmBox'
        if ($null -eq $confirm) { throw 'ConfirmBox not found' }
        $confirm.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Start-Sleep -Milliseconds 500
        Invoke-Element (Get-Button 'ขอความยินยอม (Opt-in)' 'สร้างคิวคำขอ')
        Start-Sleep -Seconds 3
        Close-MessageBoxes $process.Id
        Log-Window 'after-queue' 'ขอความยินยอม (Opt-in)'
        Save-WindowShot 'ขอความยินยอม (Opt-in)' 'ui-consent-queued.png'
    }

    Invoke-Element (Get-Button 'ขอความยินยอม (Opt-in)' 'ปิด')
    Start-Sleep -Seconds 2

    # The contact that already has a request in the trail must show that state in the list.
    $search = Find-AutomationId (Find-Window 'Telegram Control Studio') 'ContactSearchBox'
    if ($null -eq $search) { throw 'ContactSearchBox not found' }
    $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('1829328669')
    Start-Sleep -Milliseconds 500
    Invoke-Element (Get-Button 'Telegram Control Studio' 'ค้นหา')
    Start-Sleep -Seconds 3
    $afterSearch = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
    Log "STEP=contact-request-state HAS_ASKED=$($afterSearch -match 'ถามไป') SNIPPET=$($afterSearch.Substring(0, [Math]::Min(400, $afterSearch.Length)))"
    Save-WindowShot 'Telegram Control Studio' 'ui-consent-request-state.png'

    Invoke-Element (Get-Button 'Telegram Control Studio' 'กล่องคำตอบ Consent')
    $inbox = Find-Window 'Consent Inbox'
    if ($null -eq $inbox) { throw 'consent inbox never opened' }
    Start-Sleep -Seconds 3
    Log-Window 'inbox' 'Consent Inbox'
    Save-WindowShot 'Consent Inbox' 'ui-consent-inbox.png'
    Invoke-Element (Get-Button 'Consent Inbox' 'ปิด')
    Start-Sleep -Seconds 2

    Invoke-Element (Get-Button 'Telegram Control Studio' 'ตั้งค่า')
    $settings = Find-Window 'Settings'
    if ($null -eq $settings) { throw 'settings window never opened' }
    Start-Sleep -Seconds 3
    Log-Window 'settings-before' 'Settings'
    Save-WindowShot 'Settings' 'ui-consent-settings-before.png'

    foreach ($pair in @(@('ConsentCooldownBox', '45'), @('ConsentDailyLimitBox', '7'))) {
        $box = Find-AutomationId (Find-Window 'Settings') $pair[0]
        if ($null -eq $box) { throw "settings field $($pair[0]) not found" }
        $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($pair[1])
        Start-Sleep -Milliseconds 400
    }
    Invoke-Element (Get-Button 'Settings' 'บันทึกขีดจำกัด Opt-in')
    Start-Sleep -Seconds 2
    Close-MessageBoxes $process.Id
    Log-Window 'settings-after' 'Settings'
    Save-WindowShot 'Settings' 'ui-consent-settings-after.png'
    (Find-Window 'Settings').GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Seconds 2
    Log-Window 'main-after' 'Telegram Control Studio'
}
finally {
    if ($null -ne $process) {
        Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    if ($KeepSandbox) {
        Log "SANDBOX_KEPT=$Database"
    } else {
        foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item ($Database + $suffix) -Force -ErrorAction SilentlyContinue }
        Log 'SANDBOX_REMOVED=true'
    }
}
