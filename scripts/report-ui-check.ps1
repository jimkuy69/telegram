param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$Database = (Join-Path $env:TEMP 'tcs-report-ui.db'),
    [string]$CsvPath = (Join-Path $env:TEMP 'tcs-report-export.csv'),
    [string]$AccountName = 'Sandbox Report',
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts'
)
$ErrorActionPreference = 'Stop'

# The report window only reads, but it still runs against a throwaway copy of the live database so
# nothing written here can reach real recipients.
if (-not $Database.StartsWith((Join-Path $env:TEMP 'tcs-'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "refusing to run against $Database; point -Database at a sandbox copy under TEMP named tcs-*"
}
if (-not (Test-Path $Database)) { throw "sandbox database not found: $Database (run scripts/seed-report-sandbox.py first)" }
if (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue) {
    throw 'another TelegramControlStudio instance is running; close it before the sandbox run'
}
if (Test-Path $CsvPath) { Remove-Item $CsvPath -Force }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$signature = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApi4 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out WinRect r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
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
}
'@
Add-Type -TypeDefinition $signature

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'report-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

$auto = [System.Windows.Automation.AutomationElement]
$treeChildren = [System.Windows.Automation.TreeScope]::Children
$treeDescendants = [System.Windows.Automation.TreeScope]::Descendants
$windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
$appWindows = @('Telegram Control Studio', 'Result Report')

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
    if ($null -eq $window) { Log "STEP=$label WINDOW='$windowName' MISSING"; return $null }
    Log "STEP=$label WINDOW='$windowName' TEXTS=$(((Get-AllText $window) -join ' / '))"
    return $window
}

function Save-WindowShot([string]$windowName, [string]$fileName) {
    $window = Find-Window $windowName
    if ($null -eq $window) { Log "SHOT_SKIPPED='$windowName'"; return }
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][WinApi4]::ShowWindow($handle, 9)
    Start-Sleep -Milliseconds 600
    $rect = New-Object WinRect
    [void][WinApi4]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][WinApi4]::PrintWindow($handle, $hdc, 2)
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
        if ($child.Current.ProcessId -ne $processId) { continue }
        $candidates += $child
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

function Close-MessageBoxes([int]$processId) {
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $boxes = @(Get-DialogWindows $processId)
        if ($boxes.Count -eq 0) { return }
        $box = $boxes[0]
        $handle = [IntPtr]$box.Current.NativeWindowHandle
        Log "BOX='$($box.Current.Name)' TEXTS=$([WinApi4]::StaticTexts($handle)) BUTTONS=$([WinApi4]::ButtonCaptions($handle))"
        $clicked = [WinApi4]::ClickButtonByCaption($handle, @('OK', 'ตกลง', 'Yes', 'ใช่', 'บันทึก', 'Save'))
        if ($clicked -eq [IntPtr]::Zero) { Log "BOX='$($box.Current.Name)' NO_BUTTON"; return }
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
    if ($badge -notmatch 'TEST MODE') { throw 'sandbox is not isolated: test mode badge is missing' }
    if ($badge -match 'เชื่อมต่อ [1-9]') { throw 'sandbox is not isolated: a Telegram session is connected' }
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

    if ($badge -notmatch [regex]::Escape($AccountName)) {
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
        foreach ($item in $items) { if ($item.Current.Name -like "*$AccountName*") { $target = $item } }
        if ($null -eq $target) { throw "account row '$AccountName' not found (items=$($items.Count))" }
        $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Seconds 4
        Log "STEP=account-switch ITEM='$($target.Current.Name)'"
    } else {
        Log 'STEP=account-switch SKIPPED (sandbox account already active)'
    }
    Close-MessageBoxes $process.Id

    Invoke-Element (Get-Button 'Telegram Control Studio' 'รายงานผล')
    $report = Find-Window 'Result Report'
    if ($null -eq $report) { throw 'report window never opened' }
    Start-Sleep -Seconds 3
    Log-Window 'report-default-week' 'Result Report'
    Save-WindowShot 'Result Report' 'ui-report-week.png'

    Invoke-Element (Get-Button 'Result Report' '30 วันล่าสุด')
    Start-Sleep -Seconds 3
    $wide = (Get-AllText (Find-Window 'Result Report')) -join ' / '
    Log "STEP=report-30d TEXTS=$wide"
    Log "STEP=report-30d HAS_SENT_CARD=$($wide -match 'ส่งสำเร็จ') HAS_FAILURE_TABLE=$($wide -match 'เหตุผลที่ต้องแก้ไข') HAS_CONSENT=$($wide -match 'ความยินยอม')"
    Save-WindowShot 'Result Report' 'ui-report-month.png'

    Invoke-Element (Get-Button 'Result Report' 'ส่งออก CSV')
    Start-Sleep -Seconds 3
    $dialogs = @(Get-DialogWindows $process.Id)
    Log "STEP=export DIALOGS=$($dialogs.Count) NAMES=$(($dialogs | ForEach-Object { "'$($_.Current.Name)'" }) -join ', ')"
    if ($dialogs.Count -eq 0) { throw 'save dialog never opened' }
    [System.Windows.Forms.SendKeys]::SendWait($CsvPath)
    Start-Sleep -Milliseconds 800
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 3
    Close-MessageBoxes $process.Id

    if (-not (Test-Path $CsvPath)) {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        throw "csv was never written to $CsvPath"
    }
    $bytes = [System.IO.File]::ReadAllBytes($CsvPath)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $lines = @($text -split "`r`n")
    Log ("STEP=csv BYTES={0} BOM={1} LINES={2} HAS_SUMMARY={3} HAS_DAYS={4} HAS_REASONS={5} HAS_CONSENT={6} HAS_FLOOD={7}" -f `
        $bytes.Length, $bom, $lines.Count,
        ($text -match 'รายงานผลตามช่วงวันที่'),
        ($text -match 'ผลรายวัน'),
        ($text -match 'เหตุผลที่ต้องแก้ไข'),
        ($text -match 'ความยินยอม'),
        ($text -match 'FLOOD_WAIT_3600'))
    Log "STEP=csv FIRST_ROWS=$(($lines | Select-Object -First 6) -join ' ~ ')"
    if (-not $bom) { throw 'csv has no UTF-8 BOM; Excel would not render Thai text' }

    $reportWindow = Find-Window 'Result Report'
    if ($null -ne $reportWindow) {
        Invoke-Element (Get-Button 'Result Report' 'ปิด')
        Start-Sleep -Seconds 2
    }
    Log-Window 'main-after' 'Telegram Control Studio'
    Save-WindowShot 'Telegram Control Studio' 'ui-report-main.png'
    Log 'RESULT=PASS'
}
finally {
    if ($null -ne $process) {
        Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    Log "CSV_KEPT=$CsvPath"
}
