param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$SourceDatabase = (Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'),
    [string]$Database = (Join-Path $env:TEMP 'tcs-menu-help.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts'
)
$ErrorActionPreference = 'Stop'

# The menu help is a read-only popover, but the run still opens a throwaway copy of the live
# database, engages the kill switch first, and never confirms a job, so nothing can be sent.
if (-not $Database.StartsWith((Join-Path $env:TEMP 'tcs-'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "refusing to run against $Database; point -Database at a sandbox copy under TEMP named tcs-*"
}
if (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue) {
    throw 'another TelegramControlStudio instance is running; close it before the sandbox run'
}
if (-not (Test-Path -LiteralPath $SourceDatabase)) { throw "live database not found: $SourceDatabase" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'menu-help-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$auto = [System.Windows.Automation.AutomationElement]
$treeChildren = [System.Windows.Automation.TreeScope]::Children
$treeDescendants = [System.Windows.Automation.TreeScope]::Descendants
$treeSubtree = [System.Windows.Automation.TreeScope]::Subtree

function Find-Window([string]$name) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
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
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-AsteriskButtons($main) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = @()
    foreach ($button in $main.FindAll($treeDescendants, $condition)) {
        if ($button.Current.Name -ne '*') { continue }
        $buttons += $button
    }
    return $buttons
}

function Get-MenuHelpButton($main, [string]$menu) {
    # Scrolled-out rows report a zero rectangle, so the asterisk is matched to its menu button by
    # row instead of by list position.
    $nav = Get-Button $main $menu
    if ($null -eq $nav) { throw "sidebar button '$menu' not found" }
    $top = $nav.Current.BoundingRectangle.Top
    foreach ($button in (Get-AsteriskButtons $main)) {
        if ([Math]::Abs($button.Current.BoundingRectangle.Top - $top) -le 8) { return $button }
    }
    throw "asterisk help button for '$menu' not found"
}

function Get-PopupText([int]$processId) {
    # The popover is its own top-level window, so its text is found by scanning every element the
    # process owns instead of walking down from the main window.
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($element in $auto::RootElement.FindAll($treeSubtree, $condition)) {
        if ($element.Current.ProcessId -ne $processId) { continue }
        $text = $element.Current.Name
        if ($text -like 'วิธีใช้:*') { return $element }
    }
    return $null
}

function Get-TitleText([int]$processId, [string]$title) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($element in $auto::RootElement.FindAll($treeSubtree, $condition)) {
        if ($element.Current.ProcessId -eq $processId -and $element.Current.Name -eq $title) { return $title }
    }
    return '(missing)'
}

function Save-PopupShot($main, $body, [string]$fileName) {
    # The popover is its own window, so the shot is the union of the main window and the popover.
    $a = $main.Current.BoundingRectangle
    $b = $body.Current.BoundingRectangle
    $left = [int][Math]::Min($a.Left, $b.Left) - 12
    $top = [int][Math]::Min($a.Top, $b.Top) - 12
    $right = [int][Math]::Max($a.Right, $b.Right) + 12
    $bottom = [int][Math]::Max($a.Bottom, $b.Bottom) + 12
    $width = $right - $left
    $height = $bottom - $top
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()
    $path = Join-Path $OutDir $fileName
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

function Get-AppWindows([int]$processId) {
    # A modal MessageBox is normally a root child, but WPF also nests owned windows under the
    # main window in the UIA tree, so both places are searched.
    $windows = @()
    foreach ($child in $auto::RootElement.FindAll($treeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $processId) { $windows += $child }
    }
    $main = Find-Window 'Telegram Control Studio'
    if ($null -ne $main) {
        $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
        foreach ($inner in $main.FindAll($treeChildren, $windowCondition)) { $windows += $inner }
    }
    return $windows
}

function Close-Boxes([int]$processId) {
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $extra = @()
        foreach ($window in (Get-AppWindows $processId)) {
            $name = $window.Current.Name
            if ($name -eq 'Telegram Control Studio' -or $name -eq '') { continue }
            $extra += $window
        }
        if ($extra.Count -eq 0) { Start-Sleep -Milliseconds 400; continue }
        $box = $extra[0]
        $handle = [IntPtr]$box.Current.NativeWindowHandle
        Log "BOX='$($box.Current.Name)' TEXTS=$([WinApi]::StaticTexts($handle)) BUTTONS=$([WinApi]::ButtonCaptions($handle))"
        $clicked = [WinApi]::ClickButtonByCaption($handle, @('Yes', 'ใช่', 'OK', 'ตกลง', 'ปิด'))
        if ($clicked -eq [IntPtr]::Zero) { $clicked = [WinApi]::ClickDefaultButton($handle) }
        Log "BOX_CLICKED='$([WinApi]::Text($clicked))'"
        Start-Sleep -Seconds 2
    }
}

$signature = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WinApi {
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
                    SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                    return button;
                }
            }
        }
        return IntPtr.Zero;
    }
    public static IntPtr ClickDefaultButton(IntPtr dialog) {
        var value = SendMessage(dialog, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64();
        if (((value >> 16) & 0xFFFF) != 0x4B42) return IntPtr.Zero;
        var item = GetDlgItem(dialog, (int)(value & 0xFFFF));
        if (item == IntPtr.Zero) return IntPtr.Zero;
        SendMessage(item, 0x00F5, IntPtr.Zero, IntPtr.Zero);
        return item;
    }
}
'@
Add-Type -TypeDefinition $signature

Copy-Item -LiteralPath $SourceDatabase -Destination $Database -Force
foreach ($suffix in @('-wal', '-shm')) {
    if (Test-Path -LiteralPath ($SourceDatabase + $suffix)) { Copy-Item -LiteralPath ($SourceDatabase + $suffix) -Destination ($Database + $suffix) -Force }
}
Log "SANDBOX=$Database SOURCE=$SourceDatabase"

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
        $emergency = Find-AutomationId $main 'EmergencyStopButton'
        if ($null -eq $emergency) { throw 'EmergencyStopButton not found' }
        Invoke-Element $emergency
        for ($attempt = 0; $attempt -lt 12; $attempt++) {
            Start-Sleep -Seconds 1
            foreach ($window in (Get-AppWindows $process.Id)) { Log "APP_WINDOW='$($window.Current.Name)'" }
            Close-Boxes $process.Id
            $badge = (Get-AllText $main) -join ' / '
            if ($badge -match 'KILL SWITCH') { break }
        }
        Log "BADGE_AFTER='$badge'"
        if ($badge -notmatch 'KILL SWITCH') { throw 'the kill switch did not engage; refusing to drive the sandbox' }
    }
    if ($badge -match 'เชื่อมต่อ [1-9]') { throw 'sandbox is not isolated: a Telegram session is connected' }
    Log 'STEP=kill-switch STATUS=engaged'

    $asterisks = Get-AsteriskButtons $main
    Log "ASTERISKS=$($asterisks.Count)"
    if ($asterisks.Count -ne 18) { throw "expected 18 asterisk buttons, found $($asterisks.Count)" }

    $target = Get-MenuHelpButton $main 'ขอยินยอม (Opt-in)'
    Invoke-Element $target
    Start-Sleep -Seconds 2
    $body = Get-PopupText $process.Id
    if ($null -eq $body) { throw 'the menu help popover never appeared' }
    $text = $body.Current.Name
    Log "POPUP_TITLE='$(Get-TitleText $process.Id 'ขอยินยอม (Opt-in)')'"
    Log "POPUP_BODY='$text'"
    foreach ($expected in @('ขอบเขต', '20 คน/วัน', '30 วัน', 'ไม่ถามซ้ำ')) {
        if ($text -notmatch [regex]::Escape($expected)) { throw "popover text is missing '$expected': $text" }
    }
    Save-PopupShot $main $body 'ui-menu-help-optin.png'
    Log "CHECK=optin-popover STATUS=ok"

    # A second menu must swap the text instead of showing the previous entry.
    Invoke-Element (Get-MenuHelpButton $main 'ส่งข้อความ')
    Start-Sleep -Seconds 2
    $send = Get-PopupText $process.Id
    if ($null -eq $send) { throw 'the popover closed instead of switching menus' }
    Log "POPUP_BODY_SEND='$($send.Current.Name)'"
    if ($send.Current.Name -notmatch 'allowlist' -or $send.Current.Name -match '20 คน/วัน') {
        throw "the popover did not swap to the send-message help: $($send.Current.Name)"
    }
    Save-PopupShot $main $send 'ui-menu-help-send.png'

    # The group broadcast menu is new in this release, so its own scope text is asserted.
    Invoke-Element (Get-MenuHelpButton $main 'ส่งเข้ากลุ่ม (Group Broadcast)')
    Start-Sleep -Seconds 2
    $broadcast = Get-PopupText $process.Id
    if ($null -eq $broadcast) { throw 'the group broadcast popover never appeared' }
    Log "POPUP_BODY_BROADCAST='$($broadcast.Current.Name)'"
    foreach ($expected in @('ซิงก์', 'ไม่ต้องขอ Consent', 'KILL SWITCH', 'ไม่มีสิทธิ์โพสต์')) {
        if ($broadcast.Current.Name -notmatch [regex]::Escape($expected)) {
            throw "the group broadcast help is missing '$expected': $($broadcast.Current.Name)"
        }
    }
    if ($broadcast.Current.Name -match 'allowlist') {
        throw "the group broadcast help still shows the send-message text: $($broadcast.Current.Name)"
    }
    Save-PopupShot $main $broadcast 'ui-menu-help-group-broadcast.png'
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
