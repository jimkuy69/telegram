param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts\publish\win-x64\TelegramControlStudio.exe',
    [string]$SourceDatabase = (Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'),
    [string]$Database = (Join-Path $env:TEMP 'tcs-group-broadcast.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts'
)
$ErrorActionPreference = 'Stop'

# The group broadcast window can post into the operator's real groups, so this run drives a
# throwaway copy of the database with the kill switch engaged, and the account is never connected
# to Telegram, which is the guard that refuses the send before anything leaves the machine.
if (-not $Database.StartsWith((Join-Path $env:TEMP 'tcs-'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "refusing to run against $Database; point -Database at a sandbox copy under TEMP named tcs-*"
}
if ([string]::Equals((Resolve-Path $SourceDatabase).Path, [System.IO.Path]::GetFullPath($Database), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'sandbox and source databases are the same file'
}
if (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue) {
    throw 'another TelegramControlStudio instance is running; close it before the sandbox run'
}
if (-not (Test-Path -LiteralPath $SourceDatabase)) { throw "live database not found: $SourceDatabase" }
if (-not (Test-Path -LiteralPath $Exe)) { throw "executable not found: $Exe" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'group-broadcast-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# A WPF MessageBox is a Win32 dialog: its button captions follow the OS language, so the buttons are
# enumerated and clicked by handle instead of being matched through UI Automation.
$signature = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WinApiG {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    public static string Text(IntPtr handle) {
        var builder = new StringBuilder(1024);
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

$auto = [System.Windows.Automation.AutomationElement]
$treeChildren = [System.Windows.Automation.TreeScope]::Children
$treeDescendants = [System.Windows.Automation.TreeScope]::Descendants
$windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)

function Find-Window([string]$name) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($child.Current.Name -eq $name) { return $child }
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

function Get-Button($window, [string]$buttonName) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($button in $window.FindAll($treeDescendants, $condition)) {
        if ($button.Current.Name -eq $buttonName) { return $button }
    }
    return $null
}

function Invoke-Element($element) {
    if ($null -eq $element) { throw 'the requested control is not present in the window' }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-Text($element, [string]$value) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Get-WindowsUnder($element, [int]$depth) {
    $result = @($element)
    if ($depth -le 0) { return $result }
    foreach ($child in $element.FindAll($treeChildren, $windowCondition)) {
        $result += Get-WindowsUnder $child ($depth - 1)
    }
    return $result
}

function Get-AppWindows([int]$processId, $main) {
    # WPF nests an owned window - and the message box that window raises - under its owner rather
    # than at the desktop root, so the tree is walked a few levels down from every app window.
    $windows = @()
    $seen = @{}
    $roots = @()
    foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $processId) { $roots += $child }
    }
    if ($null -ne $main) { $roots += $main }
    foreach ($root in $roots) {
        foreach ($window in (Get-WindowsUnder $root 3)) {
            $handle = $window.Current.NativeWindowHandle
            if ($handle -ne 0 -and $seen.ContainsKey($handle)) { continue }
            if ($handle -ne 0) { $seen[$handle] = $true }
            $windows += $window
        }
    }
    return $windows
}

function Get-DialogWindows([int]$processId, $main) {
    $dialogs = @()
    foreach ($window in (Get-AppWindows $processId $main)) {
        if ($window.Current.Name -eq 'Telegram Control Studio') { continue }
        if ($window.Current.Name -eq 'ส่งเข้ากลุ่ม (Group Broadcast)') { continue }
        if ($window.Current.NativeWindowHandle -eq 0) { continue }
        $dialogs += $window
    }
    return $dialogs
}

function Invoke-Dialog([int]$processId, $main, [string[]]$captions, [string]$label) {
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        $boxes = @(Get-DialogWindows $processId $main)
        if ($boxes.Count -gt 0) {
            $handle = [IntPtr]$boxes[0].Current.NativeWindowHandle
            Log "DIALOG=$label CAPTION='$($boxes[0].Current.Name)' TEXTS=$([WinApiG]::StaticTexts($handle)) BUTTONS=$([WinApiG]::ButtonCaptions($handle))"
            $clicked = [WinApiG]::ClickButtonByCaption($handle, $captions)
            if ($clicked -eq [IntPtr]::Zero) { throw "dialog '$label' has none of: $($captions -join ', ')" }
            Log "DIALOG=$label CLICKED='$([WinApiG]::Text($clicked))'"
            Start-Sleep -Seconds 2
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "no dialog appeared for '$label'"
}

function Close-Boxes([int]$processId, $main) {
    foreach ($box in @(Get-DialogWindows $processId $main)) {
        $handle = [IntPtr]$box.Current.NativeWindowHandle
        Log "CLOSE_BOX='$($box.Current.Name)' BUTTONS=$([WinApiG]::ButtonCaptions($handle))"
        [void][WinApiG]::ClickButtonByCaption($handle, @('OK', 'ตกลง', 'Yes', 'ใช่', 'No', 'ไม่ใช่', 'Cancel', 'ยกเลิก'))
        Start-Sleep -Milliseconds 600
    }
}

function Describe-Windows([int]$processId, $main) {
    $lines = @()
    foreach ($window in (Get-AppWindows $processId $main)) {
        $lines += ("[name='{0}' type={1} handle={2} class={3}]" -f `
            $window.Current.Name, $window.Current.ControlType.ProgrammaticName, $window.Current.NativeWindowHandle, $window.Current.ClassName)
    }
    return ($lines -join ' ')
}

function Assert-Dialog([int]$processId, $main, [string]$caption, [string]$body, [string]$label, [string]$Shot) {
    for ($attempt = 0; $attempt -lt 16; $attempt++) {
        $boxes = @(Get-DialogWindows $processId $main)
        if ($boxes.Count -eq 0) {
            if ($attempt -eq 0 -or $attempt -eq 15) { Log "NO_DIALOG=$label WINDOWS=$(Describe-Windows $processId $main)" }
            Start-Sleep -Milliseconds 500
            continue
        }
        $box = $boxes[0]
        $handle = [IntPtr]$box.Current.NativeWindowHandle
        $texts = [WinApiG]::StaticTexts($handle)
        Log "REFUSE=$label CAPTION='$($box.Current.Name)' TEXTS=$texts BUTTONS=$([WinApiG]::ButtonCaptions($handle))"
        if ($box.Current.Name -ne $caption) { throw "dialog '$label' has caption '$($box.Current.Name)', expected '$caption'" }
        if ($texts -notmatch [regex]::Escape($body)) { throw "dialog '$label' does not state '$body': $texts" }
        if ($Shot) { Save-RectShot $box $Shot }
        $clicked = [WinApiG]::ClickButtonByCaption($handle, @('OK', 'ตกลง'))
        if ($clicked -eq [IntPtr]::Zero) { throw "dialog '$label' has no OK button" }
        Start-Sleep -Seconds 1
        return
    }
    if (-not (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue)) {
        throw ('the app is no longer running, so ' + $label + ' opened no box: ' + (Describe-Windows $processId $main))
    }
    throw ('no dialog appeared for ' + $label + ': ' + (Describe-Windows $processId $main))
}

function Get-GroupBroadcastJobs([string]$path) {
    # The sandbox database is written by the app, so counting its jobs afterwards proves that a
    # refused send never queued a post behind the operator's back.
    $probe = @'
import sqlite3, sys
connection = sqlite3.connect(sys.argv[1])
print(connection.execute("select count(*) from jobs where target_type = 'GroupBroadcast'").fetchone()[0])
'@
    $probeFile = Join-Path $env:TEMP 'tcs-group-broadcast-probe.py'
    Set-Content -Path $probeFile -Value $probe -Encoding UTF8
    $output = (& python $probeFile $path 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "counting the sandbox jobs failed: $output" }
    return [int]("$output").Trim()
}

function Save-RectShot($window, [string]$fileName) {
    $rect = $window.Current.BoundingRectangle
    $width = [int]$rect.Width
    $height = [int]$rect.Height
    if ($width -le 0 -or $height -le 0) { Log "SHOT_SKIPPED=$fileName"; return }
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$rect.Left, [int]$rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()
    $path = Join-Path $OutDir $fileName
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

Copy-Item -LiteralPath $SourceDatabase -Destination $Database -Force
foreach ($suffix in @('-wal', '-shm')) {
    if (Test-Path -LiteralPath ($SourceDatabase + $suffix)) { Copy-Item -LiteralPath ($SourceDatabase + $suffix) -Destination ($Database + $suffix) -Force }
}
Log "SANDBOX=$Database SOURCE=$SourceDatabase"
$jobsBefore = Get-GroupBroadcastJobs $Database
Log "BASELINE_GROUPBROADCAST_JOBS=$jobsBefore"

$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
$process = $null
try {
    $process = Start-Process $Exe -PassThru
    $main = Find-Window 'Telegram Control Studio'
    if ($null -eq $main) { throw 'main window not found' }
    Start-Sleep -Seconds 4

    $badge = (Get-AllText $main) -join ' / '
    Log "BADGE_LAUNCH='$badge'"
    if ($badge -notmatch 'KILL SWITCH') {
        Invoke-Element (Find-AutomationId $main 'EmergencyStopButton')
        Start-Sleep -Seconds 2
        Invoke-Dialog $process.Id $main @('Yes', 'ใช่') 'kill-switch-confirm'
        Invoke-Dialog $process.Id $main @('OK', 'ตกลง') 'kill-switch-engaged'
        for ($attempt = 0; $attempt -lt 12; $attempt++) {
            Start-Sleep -Seconds 1
            $badge = (Get-AllText (Find-Window 'Telegram Control Studio')) -join ' / '
            if ($badge -match 'KILL SWITCH') { break }
        }
        Log "BADGE_AFTER='$($badge.Substring(0, [Math]::Min(200, $badge.Length)))'"
        if ($badge -notmatch 'KILL SWITCH') { throw 'the kill switch did not engage; refusing to drive the sandbox' }
    }
    if ($badge -match 'เชื่อมต่อ [1-9]') { throw 'sandbox is not isolated: a Telegram session is connected' }
    Log 'STEP=kill-switch STATUS=engaged'
    $main = Find-Window 'Telegram Control Studio'

    Invoke-Element (Get-Button $main 'ส่งเข้ากลุ่ม (Group Broadcast)')
    $window = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        foreach ($candidate in (Get-AppWindows $process.Id $main)) {
            if ($candidate.Current.Name -eq 'ส่งเข้ากลุ่ม (Group Broadcast)') { $window = $candidate; break }
        }
        if ($null -ne $window) { break }
    }
    if ($null -eq $window) { throw 'the group broadcast window never opened' }
    Log 'STEP=open-window STATUS=opened'

    $safety = (Find-AutomationId $window 'SafetyText').Current.Name
    Log "SAFETY_BANNER='$safety'"
    foreach ($expected in @('หยุดฉุกเฉิน', 'ไม่ต้องขอ Consent')) {
        if ($safety -notmatch [regex]::Escape($expected)) { throw "the safety banner does not state '$expected': $safety" }
    }

    $before = (Find-AutomationId $window 'SelectionText').Current.Name
    Log "SELECTION_BEFORE='$before'"
    Invoke-Element (Get-Button $window 'เลือกทั้งหมด')
    Start-Sleep -Seconds 2
    $all = (Find-AutomationId $window 'SelectionText').Current.Name
    Log "SELECTION_ALL='$all'"
    $match = [regex]::Match($all, 'เลือก\s+(\d+)\s+•\s+จะส่งได้\s+(\d+)\s+•\s+ไม่มีสิทธิ์โพสต์\s+(\d+)')
    if (-not $match.Success) { throw "the selection counter is not in the expected shape: $all" }
    $ticked = [int]$match.Groups[1].Value
    $postable = [int]$match.Groups[2].Value
    $excluded = [int]$match.Groups[3].Value
    if ($ticked -le 0) { throw 'no group row is ticked, so this sandbox has nothing to check the maths against' }
    if (($postable + $excluded) -ne $ticked) { throw "the counter does not add up: ticked=$ticked postable=$postable excluded=$excluded" }
    Log "CHECK=selection-maths TICKED=$ticked POSTABLE=$postable EXCLUDED=$excluded"

    $rows = @()
    $rowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    foreach ($row in $window.FindAll($treeDescendants, $rowCondition)) { $rows += $row }
    Log "GRID_ROWS_REALISED=$($rows.Count)"
    $rights = (Get-AllText $window) | Where-Object { $_ -eq 'โพสต์ได้' -or $_ -eq 'ไม่มีสิทธิ์โพสต์' }
    Log "POST_RIGHTS_CELLS=$(($rights | Select-Object -First 6) -join ' | ')"
    Save-RectShot $window 'ui-group-broadcast-window.png'

    Invoke-Element (Get-Button $window 'ยกเลิกที่เลือก')
    Start-Sleep -Seconds 1
    $none = (Find-AutomationId $window 'SelectionText').Current.Name
    Log "SELECTION_CLEARED='$none'"
    if ($none -notmatch 'ยังไม่ได้เลือก') { throw "clearing the selection did not reset the counter: $none" }

    Set-Text (Find-AutomationId $window 'MessageBoxInput') 'ทดสอบ sandbox — ห้ามส่งจริง'
    Start-Sleep -Milliseconds 500

    # The window asks for a live TDLib client before it looks at the selection, the safety state or
    # the confirmation box, so an unconnected sandbox account cannot queue a post at all.
    Invoke-Element (Get-Button $window 'เลือกทั้งหมด')
    Start-Sleep -Seconds 1
    $send = Find-AutomationId $window 'SendButton'
    Log "SEND_BUTTON NAME='$($send.Current.Name)' ENABLED=$($send.Current.IsEnabled)"
    Invoke-Element $send
    Assert-Dialog $process.Id $main 'ยังไม่พร้อมส่ง' 'ยังไม่ได้เชื่อมต่อ' 'refuse-disconnected' 'ui-group-broadcast-send-refused.png'
    Close-Boxes $process.Id $main

    $status = (Find-AutomationId $window 'StatusText').Current.Name
    Log "STATUS_AFTER_REFUSAL='$status'"
    if ($status -match 'กำลังโพสต์') { throw 'the window reports a run although nothing could be sent' }
    Log 'CHECK=no-send STATUS=ok'

    $queued = Get-GroupBroadcastJobs $Database
    Log "DB_GROUPBROADCAST_JOBS=$queued BASELINE=$jobsBefore"
    if ($queued -ne $jobsBefore) { throw "the sandbox database gained $($queued - $jobsBefore) group broadcast jobs although every send was refused" }

    Invoke-Element (Get-Button $window 'ปิด')
    Start-Sleep -Seconds 2
    Log 'RESULT=PASS'
}
finally {
    if ($null -ne $process) {
        Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item ($Database + $suffix) -Force -ErrorAction SilentlyContinue }
    Log 'SANDBOX_REMOVED=true'
}
