param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$SourceDatabase = (Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts',
    [switch]$KeepSandbox
)
$ErrorActionPreference = 'Stop'

if (Get-Process TelegramControlStudio -ErrorAction SilentlyContinue) {
    throw 'another TelegramControlStudio instance is running; close it before the sandbox run'
}
if (-not (Test-Path $SourceDatabase)) { throw "source database not found: $SourceDatabase" }
$Database = Join-Path $env:TEMP ('tcs-grid-edit-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.db')
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
public static class WinMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    public static void ForceForeground(IntPtr handle) {
        var foreground = GetForegroundWindow();
        uint foregroundThread;
        GetWindowThreadProcessId(foreground, out foregroundThread);
        var current = GetCurrentThreadId();
        AttachThreadInput(current, foregroundThread, true);
        SetForegroundWindow(handle);
        AttachThreadInput(current, foregroundThread, false);
    }
    public const uint LEFT_DOWN = 0x0002;
    public const uint LEFT_UP = 0x0004;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(LEFT_DOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(LEFT_UP, 0, 0, 0, UIntPtr.Zero);
    }
    public static void FocusAndClick(IntPtr handle, int x, int y) {
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
        System.Threading.Thread.Sleep(500);
        Click(x, y);
    }
}
public static class WinDialog {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
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
                    SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                    return button;
                }
            }
        }
        return IntPtr.Zero;
    }
}
'@
Add-Type -TypeDefinition $signature

$log = Join-Path $OutDir 'grid-edit-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

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

function Invoke-Button($button) {
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Get-ModalWindows($process) {
    $candidates = @()
    foreach ($child in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $process.Id -and $child.Current.Name -ne 'Telegram Control Studio') { $candidates += $child }
    }
    return $candidates
}

function Close-ModalWindows($process) {
    $accept = @('ปิด', 'OK', 'ตกลง', 'Yes', 'ใช่', 'ยืนยัน')
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        $modal = @(Get-ModalWindows $process) | Select-Object -First 1
        if ($null -eq $modal) { return }
        $handle = [IntPtr]$modal.Current.NativeWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            $clicked = [WinDialog]::ClickButtonByCaption($handle, $accept)
            if ($clicked -ne [IntPtr]::Zero) { Start-Sleep -Seconds 2; continue }
        }
        $chosen = $null
        foreach ($descendant in $modal.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($accept -contains $descendant.Current.Name) { $chosen = $descendant; break }
        }
        if ($null -eq $chosen) { return }
        try {
            $pattern = $chosen.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $pattern.Invoke()
        } catch { return }
        Start-Sleep -Seconds 2
    }
}

function Save-WindowShot($process, [string]$path) {
    $process.Refresh()
    $handle = $process.MainWindowHandle
    Add-Type -AssemblyName System.Drawing
    $rect = New-Object WinRect
    [void][WinApiShim]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [void][WinApiShim]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

Add-Type -TypeDefinition @'
public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApiShim {
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out WinRect r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, int flags);
}
'@

function Focus-AppWindow($process) {
    $process.Refresh()
    [WinMouse]::ShowWindow($process.MainWindowHandle, 9)
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        [WinMouse]::ForceForeground($process.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 400
        if ([WinMouse]::GetForegroundWindow() -eq $process.MainWindowHandle) { return }
    }
    Start-Sleep -Seconds 1
}

function Click-Element($process, $element) {
    Focus-AppWindow $process
    $rect = $element.Current.BoundingRectangle
    [WinMouse]::Click([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
}

function Click-ElementById($process, $window, [string]$id) {
    Focus-AppWindow $process
    $element = Find-AutomationId $window $id
    if ($null -eq $element) { throw "$id not found after focus" }
    $rect = $element.Current.BoundingRectangle
    [WinMouse]::Click([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    return $element
}

function Find-GridCell($grid, [int]$cellIndex) {
    $rowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $row = $grid.FindFirst([System.Windows.Automation.TreeScope]::Children, $rowCondition)
    if ($null -eq $row) { return $null }
    $customCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Custom)
    $cells = @($row.FindAll([System.Windows.Automation.TreeScope]::Children, $customCondition))
    if ($cells.Count -le $cellIndex) { return $null }
    return $cells[$cellIndex]
}

$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Engage the kill switch inside the throwaway DB via the app repository before launch,
# so the sandbox is isolated even if synthetic UI clicks never reach the window.
$probe = Join-Path $env:TEMP 'tcs-kill-probe'
$probeOut = & dotnet run --project $probe -- $Database 2>&1
Log "KILL_PROBE=$($probeOut -join ' | ')"
if (($probeOut -join ' ') -notmatch 'KILL_SWITCH_SET=True') { throw 'failed to engage kill switch in the sandbox DB' }

try {
    $process = Start-Process $Exe -PassThru
    $window = Get-AutomationWindow ((Get-Date).AddSeconds(40))
    if ($null -eq $window) { throw 'main window not found' }
    Start-Sleep -Seconds 3

    if (((Get-AllText $window) -join ' / ') -notmatch 'KILL SWITCH') {
        throw 'sandbox is not isolated: the kill switch badge never appeared'
    }
    Log 'STEP=kill-switch STATUS=engaged'

    $grid = Find-AutomationId $window 'ContactGrid'
    if ($null -eq $grid) { throw 'ContactGrid not found' }
    Log "GRID_ROWS=$($grid.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem))).Count)"

    function Get-CellText($cell) {
        return ((Get-AllText $cell) -join '')
    }

    # 1. The reported crash path: a plain mouse click on the Telegram ID cell used to abort the process.
    $cell = Find-GridCell $grid 2
    if ($null -eq $cell) { throw 'Telegram ID cell not found' }
    $expectedId = (Get-CellText $cell) -replace '[^0-9]', ''
    Log "CELL_ID_TEXT=$expectedId"
    Click-Element $process $cell
    Log "FOREGROUND_IS_APP=$([WinMouse]::GetForegroundWindow() -eq $process.MainWindowHandle)"
    Start-Sleep -Seconds 2
    if ($process.HasExited) { throw 'process died after clicking the Telegram ID cell' }
    $modals = @(Get-ModalWindows $process)
    Log "STEP=after-id-click PROCESS_ALIVE=True MODALS=$($modals.Count)"
    if ($modals.Count -gt 0) { throw 'unexpected dialog appeared after clicking the cell' }
    Save-WindowShot $process (Join-Path $OutDir 'ui-grid-after-cell-click.png')

    # 2. The copy buttons are the supported way to take a value out of the grid.
    Set-Clipboard -Value 'sentinel-before-copy'
    $copyIdButton = Find-AutomationId $window 'CopyTelegramIdButton'
    if ($null -eq $copyIdButton) { throw 'CopyTelegramIdButton not found' }
    Invoke-Button $copyIdButton
    Start-Sleep -Seconds 1
    $clipboard = (Get-Clipboard) -join ''
    Log "STEP=copy-telegram-id CLIPBOARD=$clipboard EXPECTED=$expectedId ALIVE=$((-not $process.HasExited))"
    if ($process.HasExited) { throw 'process died after pressing the copy button' }
    if ($clipboard -ne $expectedId) { throw "copy button put '$clipboard' on the clipboard instead of '$expectedId'" }

    $usernameCell = Find-GridCell $grid 3
    $expectedUsername = (Get-CellText $usernameCell) -replace '^\s*|\s*$', ''
    Set-Clipboard -Value 'sentinel-before-copy'
    $copyUserButton = Find-AutomationId $window 'CopyUsernameButton'
    if ($null -eq $copyUserButton) { throw 'CopyUsernameButton not found' }
    Invoke-Button $copyUserButton
    Start-Sleep -Seconds 1
    $clipboard2 = (Get-Clipboard) -join ''
    $expectedAfterUsername = if ($expectedUsername) { $expectedUsername } else { 'sentinel-before-copy' }
    Log "STEP=copy-username CLIPBOARD=$clipboard2 EXPECTED=$expectedAfterUsername ALIVE=$((-not $process.HasExited))"
    if ($clipboard2 -ne $expectedAfterUsername) { throw "username copy put '$clipboard2' on the clipboard, expected '$expectedAfterUsername'" }

    # Leave the app cleanly and prove the crash log stayed untouched by this run.
    Save-WindowShot $process (Join-Path $OutDir 'ui-grid-after-check.png')
    Log 'STEP=done STATUS=ok'
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
