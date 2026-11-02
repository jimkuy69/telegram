param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts\publish\win-x64\TelegramControlStudio.exe',
    [string]$SourceDatabase = (Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts'
)
$ErrorActionPreference = 'Stop'

# Runs a renamed copy inside the publish folder so the operator's installed app is never opened or killed.
$ExeDirectory = Split-Path $Exe -Parent
$SandboxExe = Join-Path $ExeDirectory 'TcsSandbox.exe'
Copy-Item $Exe $SandboxExe -Force

if (-not (Test-Path $SourceDatabase)) { throw "source database not found: $SourceDatabase" }
$Database = Join-Path $env:TEMP ('tcs-import-preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.db')
foreach ($suffix in @('', '-wal', '-shm')) {
    $sidecar = $SourceDatabase + $suffix
    if (Test-Path $sidecar) { Copy-Item $sidecar ($Database + $suffix) -Force }
}
$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
if ($env:TELEGRAM_CONTROL_STUDIO_DB -ne $Database -or -not (Test-Path $Database)) { throw 'sandbox database override is not in place' }
if ([string]::Equals((Resolve-Path $Database).Path, (Resolve-Path $SourceDatabase).Path, [StringComparison]::OrdinalIgnoreCase)) { throw 'sandbox and source databases are the same file' }

# One row of each kind the importer must tell apart. The numbers are a fixture, not the operator's list.
$CsvPath = Join-Path $env:TEMP 'tcs-import-preview-fixture.csv'
$lines = @(
    'telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out',
    ',สมชาย,ใจดี,,+66812340001,true,false',
    ',สมหญิง,ดี,,+66812340002,true,false',
    ',ทดสอบ,หนึ่ง,,+66812340003,true,false',
    ',ผิด,รูปแบบ,,0812340004,true,false',
    ',ลืม,ความยินยอม,,+66812340005,,false',
    '987654321,มี,ไอดี,,+66812340006,true,false'
)
[System.IO.File]::WriteAllLines($CsvPath, $lines, (New-Object System.Text.UTF8Encoding($true)))

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApiImport {
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out WinRect r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int c);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly System.IntPtr HWND_TOPMOST = new System.IntPtr(-1);
    public static void PlaceTopmost(System.IntPtr h, int x, int y, int w, int ht) { SetWindowPos(h, HWND_TOPMOST, x, y, w, ht, 0x0040); }
}
'@

$auto = [System.Windows.Automation.AutomationElement]
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
function Get-Button($window, [string]$buttonName) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($button in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($button.Current.Name -eq $buttonName) { return $button }
    }
    return $null
}
function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Get-Value($element) {
    # A read-only TextBox carries its text in ValuePattern, not in the automation name.
    try { return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return '' }
}
function Get-AppWindows([int]$processId, $main) {
    $windows = @()
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    foreach ($child in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $processId) { $windows += $child }
    }
    if ($null -ne $main) {
        foreach ($inner in $main.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition)) {
            if ($inner.Current.ProcessId -eq $processId) { $windows += $inner }
        }
    }
    return $windows
}
function Wait-AppWindow($process, $main, [string]$title, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($window in (Get-AppWindows $process.Id $main)) {
            if ($window.Current.Name -eq $title) { return $window }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}
function Wait-MainWindow($process, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        foreach ($child in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($child.Current.ProcessId -ne $process.Id) { continue }
            if ([IntPtr]$child.Current.NativeWindowHandle -eq $process.MainWindowHandle) { return $child }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}
function Focus-Window($window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { return }
    [void][WinApiImport]::ShowWindow($handle, 9)
    [void][WinApiImport]::PlaceTopmost($handle, 30, 30, 1280, 900)
    [void][WinApiImport]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 500
}
function Save-WindowShot($window, [string]$path) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    $rect = New-Object WinRect
    [void][WinApiImport]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][WinApiImport]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path)
    $bitmap.Dispose()
    Write-Host "SHOT=$path"
}
function Get-ProcessWindowTree([int]$processId, $parent, [int]$depth) {
    # The shell dialog is owned by the window that opened it, which is the import window, not main.
    $found = @()
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    foreach ($child in $parent.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition)) {
        if ($child.Current.ProcessId -ne $processId) { continue }
        $found += $child
        if ($depth -gt 0) { $found += Get-ProcessWindowTree $processId $child ($depth - 1) }
    }
    return $found
}
function Get-FileDialog($process, $main) {
    foreach ($candidate in (Get-AppWindows $process.Id $main)) {
        if ($candidate.Current.ClassName -eq '#32770') { return $candidate }
        foreach ($inner in (Get-ProcessWindowTree $process.Id $candidate 3)) {
            if ($inner.Current.ClassName -eq '#32770') { return $inner }
        }
    }
    return $null
}
function Send-FilePath($process, $main, [string]$path) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $dialog = Get-FileDialog $process $main
        if ($null -ne $dialog) {
            Focus-Window $dialog
            Start-Sleep -Milliseconds 700
            [System.Windows.Forms.SendKeys]::SendWait($path)
            Start-Sleep -Milliseconds 500
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            $gone = (Get-Date).AddSeconds(10)
            while ((Get-Date) -lt $gone) {
                if ($null -eq (Get-FileDialog $process $main)) { return $true }
                Start-Sleep -Milliseconds 300
            }
            return $false
        }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

$log = Join-Path $OutDir 'import-preview-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8; Write-Host $message }

try {
    $process = Start-Process $SandboxExe -PassThru
    $main = Wait-MainWindow $process 60
    if ($null -eq $main -or $main.Current.Name -ne 'Telegram Control Studio') { throw 'main window not found' }
    Log "STEP=main STATUS=open PID=$($process.Id)"

    $import = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Focus-Window $main
        $button = Get-Button $main 'นำเข้าผู้ติดต่อ'
        if ($null -eq $button) { throw 'the main window has no "นำเข้าผู้ติดต่อ" button' }
        Invoke-Element $button
        $import = Wait-AppWindow $process $main 'Import Contacts' 10
        if ($null -ne $import) { break }
    }
    if ($null -eq $import) { throw 'Import Contacts did not open' }
    Focus-Window $import
    Log "STEP=open STATUS=ok TITLE=$($import.Current.Name)"

    $summaryBefore = (Find-AutomationId $import 'SummaryText').Current.Name
    Log "STEP=initial-summary TEXT=$summaryBefore"

    $chosen = Get-Button $import 'เลือก CSV / XLSX'
    if ($null -eq $chosen) { throw 'the import window has no file chooser' }
    Invoke-Element $chosen
    $typed = Send-FilePath $process $main $CsvPath
    Log "STEP=choose-file DIALOG_REACHED=$typed"
    if (-not $typed) { throw 'the file dialog never went away' }

    $import = Wait-AppWindow $process $main 'Import Contacts' 10
    Focus-Window $import
    $summary = (Find-AutomationId $import 'SummaryText').Current.Name
    $preview = Get-Value (Find-AutomationId $import 'PreviewText')
    Log "STEP=preview SUMMARY=$summary"
    Log "STEP=preview DETAIL=$preview"

    # Three phone-only rows, one row that already carries an id, and two rows that must be refused.
    if ($summary -notmatch 'ผ่าน 1') { throw "the preview did not count the one row with an id: $summary" }
    if ($summary -notmatch 'รอ resolve จากเบอร์โทร 3') { throw "the preview did not count the three phone-only rows: $summary" }
    if ($summary -notmatch 'ผิดพลาด 2') { throw "the preview did not count the two refused rows: $summary" }
    if ($preview -notmatch 'phone_number ไม่ถูกต้อง') { throw "the bad phone row was not explained: $preview" }
    if ($preview -notmatch 'has_consent') { throw "the missing-consent row was not explained: $preview" }

    Save-WindowShot $import (Join-Path $OutDir 'ui-import-preview-phone-only.png')
    Log 'STEP=done STATUS=ok'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Seconds 1
    foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item ($Database + $suffix) -Force -ErrorAction SilentlyContinue }
    Remove-Item $CsvPath -Force -ErrorAction SilentlyContinue
    Remove-Item $SandboxExe -Force -ErrorAction SilentlyContinue
    Log 'SANDBOX_REMOVED=true'
}
