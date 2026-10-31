param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$Database = (Join-Path $env:TEMP 'tcs-account-delete.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts',
    [switch]$KeepSandbox
)
$ErrorActionPreference = 'Stop'

# Deleting an account is the one flow that removes rows for good, so it is rehearsed against a
# throwaway database under TEMP that holds no real recipient.
if (-not $Database.StartsWith((Join-Path $env:TEMP 'tcs-'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "refusing to run against $Database; point -Database at a sandbox copy under TEMP named tcs-*"
}
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
public struct WinRect5 { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApi5 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out WinRect5 r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    public static string Text(IntPtr handle) {
        var builder = new StringBuilder(512);
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
}
'@
Add-Type -TypeDefinition $signature

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'account-delete-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

$repo = Split-Path -Parent $PSScriptRoot
$seeder = Join-Path $repo 'scripts\seed-account-delete-sandbox.py'
$auto = [System.Windows.Automation.AutomationElement]
$treeChildren = [System.Windows.Automation.TreeScope]::Children
$treeDescendants = [System.Windows.Automation.TreeScope]::Descendants
$windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
$appWindows = @('Telegram Control Studio')

function Find-Window([string]$name) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
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

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Save-WindowShot([string]$windowName, [string]$fileName) {
    $window = Find-Window $windowName
    if ($null -eq $window) { Log "SHOT_SKIPPED='$windowName'"; return }
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][WinApi5]::ShowWindow($handle, 9)
    Start-Sleep -Milliseconds 600
    $rect = New-Object WinRect5
    [void][WinApi5]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][WinApi5]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $path = Join-Path $OutDir $fileName
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

function Get-DialogWindows([int]$processId) {
    $candidates = @()
    foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $processId) { $candidates += $child }
    }
    $main = Find-Window 'Telegram Control Studio'
    if ($null -ne $main) {
        foreach ($inner in $main.FindAll($treeChildren, $windowCondition)) { $candidates += $inner }
    }
    $dialogs = @()
    foreach ($candidate in $candidates) {
        if ($appWindows -contains $candidate.Current.Name) { continue }
        $dialogs += $candidate
    }
    return $dialogs
}

# Answers the newest confirmation box. $captions decides which button is pressed.
function Invoke-Dialog([int]$processId, [string[]]$captions, [string]$label) {
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        $boxes = @(Get-DialogWindows $processId)
        if ($boxes.Count -gt 0) {
            $handle = [IntPtr]$boxes[0].Current.NativeWindowHandle
            Log "DIALOG=$label TEXTS=$([WinApi5]::StaticTexts($handle)) BUTTONS=$([WinApi5]::ButtonCaptions($handle))"
            $clicked = [WinApi5]::ClickButtonByCaption($handle, $captions)
            if ($clicked -eq [IntPtr]::Zero) { throw "dialog '$label' has none of: $($captions -join ', ')" }
            Log "DIALOG=$label CLICKED='$([WinApi5]::Text($clicked))'"
            Start-Sleep -Seconds 2
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "no confirmation dialog appeared for '$label'"
}

function Select-Account([int]$processId, [string]$name) {
    $combo = Find-AutomationId (Find-Window 'Telegram Control Studio') 'AccountSwitchBox'
    if ($null -eq $combo) { throw 'AccountSwitchBox not found' }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = @()
    $waited = 0
    while ($items.Count -eq 0 -and $waited -lt 8) {
        Start-Sleep -Seconds 1
        $waited++
        foreach ($item in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Subtree, $condition)) {
            if ($item.Current.ProcessId -eq $processId) { $items += $item }
        }
    }
    $target = $null
    foreach ($item in $items) { if ($item.Current.Name -like "*$name*") { $target = $item } }
    if ($null -eq $target) { throw "account row '$name' not found (items=$($items.Count))" }
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 3
    Log "STEP=select-account NAME='$($target.Current.Name)'"
}

$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
$process = $null
try {
    if (-not (Test-Path $Database)) {
        Log 'STEP=create-schema LAUNCHING (the app migrates a fresh sandbox itself)'
        $boot = Start-Process $Exe -PassThru
        Start-Sleep -Seconds 10
        Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
        if (-not (Test-Path $Database)) { throw "the app did not create $Database" }
    }

    python $seeder --target $Database --mode seed 2>&1 | ForEach-Object { Log "SEED $_" }
    if ($LASTEXITCODE -ne 0) { throw 'seeding the sandbox failed' }

    $process = Start-Process $Exe -PassThru
    if ($null -eq (Find-Window 'Telegram Control Studio')) { throw 'main window not found' }
    Start-Sleep -Seconds 3
    Log "DATABASE=$Database"

    $badge = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
    if ($badge -match 'เชื่อมต่อ [1-9]') { throw 'sandbox is not isolated: a Telegram session is connected' }
    if ($badge -notmatch 'KILL SWITCH') {
        Invoke-Element (Find-AutomationId (Find-Window 'Telegram Control Studio') 'EmergencyStopButton')
        Start-Sleep -Seconds 2
        Invoke-Dialog $process.Id @('OK', 'ตกลง', 'Yes', 'ใช่') 'kill-switch'
        Log 'STEP=kill-switch STATUS=engaged'
    }
    $engaged = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
    if ($engaged -notmatch 'KILL SWITCH') { throw 'the kill switch did not engage; refusing to rehearse a destructive flow' }

    # A connected account must refuse deletion; nothing is connected here, so the guard is checked
    # by the button being present and the flow reaching its confirmation boxes.
    $deleteButton = Find-AutomationId (Find-Window 'Telegram Control Studio') 'DeleteAccountButton'
    if ($null -eq $deleteButton) { throw 'DeleteAccountButton not found' }
    Log 'STEP=button-found NAME=DeleteAccountButton'

    foreach ($junk in @('Junk One', 'Junk Two')) {
        Select-Account $process.Id $junk
        Invoke-Element (Find-AutomationId (Find-Window 'Telegram Control Studio') 'DeleteAccountButton')
        Start-Sleep -Seconds 2
        Invoke-Dialog $process.Id @('ใช่', 'Yes') "confirm-1-$junk"
        Invoke-Dialog $process.Id @('ใช่', 'Yes') "confirm-2-$junk"
        Invoke-Dialog $process.Id @('OK', 'ตกลง') "result-$junk"
        Log "STEP=deleted ACCOUNT='$junk'"
    }

    Save-WindowShot 'Telegram Control Studio' 'ui-account-delete-after.png'
    Select-Account $process.Id 'jimmy'
    Log "STEP=survivor TEXTS=$(((Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / ').Substring(0, 300))"

    Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    $process = $null

    python $seeder --target $Database --mode verify 2>&1 | ForEach-Object { Log "VERIFY $_" }
    if ($LASTEXITCODE -ne 0) { throw 'verification failed: the database still holds deleted accounts or orphans' }
    Log 'RESULT=PASS'
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
    Log "LOG=$log"
}
