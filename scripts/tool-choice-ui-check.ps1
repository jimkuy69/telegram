param(
    [string]$Exe = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\src\TelegramControlStudio.App\bin\Debug\net8.0-windows\win-x64\TelegramControlStudio.exe',
    [string]$SourceDatabase = (Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'),
    [string]$OutDir = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts',
    [switch]$KeepSandbox
)
$ErrorActionPreference = 'Stop'

# The check runs as a renamed copy so the operator's own installed app is never touched or killed.
$SandboxExeName = 'TcsSandbox.exe'
if (Get-Process ($SandboxExeName -replace '.exe$', '') -ErrorAction SilentlyContinue) {
    throw 'a sandbox check is already running'
}
if (-not (Test-Path $SourceDatabase)) { throw "source database not found: $SourceDatabase" }
$ExeDirectory = Split-Path $Exe -Parent
Copy-Item $Exe (Join-Path $ExeDirectory $SandboxExeName) -Force
$Exe = Join-Path $ExeDirectory $SandboxExeName
$Database = Join-Path $env:TEMP ('tcs-tool-choice-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.db')
foreach ($suffix in @('', '-wal', '-shm')) {
    $sidecar = $SourceDatabase + $suffix
    if (Test-Path $sidecar) { Copy-Item $sidecar ($Database + $suffix) -Force }
}

# Media used by the preview checks lives outside the database so the run never reads the user files.
$MediaDir = Join-Path $env:TEMP 'tcs-c-sandbox'
New-Item -ItemType Directory -Force -Path $MediaDir | Out-Null
$PngPath = Join-Path $MediaDir 'preview.png'
$WebpPath = Join-Path $MediaDir 'preview.webp'
if (-not (Test-Path $PngPath)) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 360, 220
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 105, 65, 232))
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $graphics.DrawString('TCS preview check', [System.Drawing.SystemFonts]::DefaultFont, $brush, 20, 90)
    $graphics.Dispose()
    $bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}
if (-not (Test-Path $WebpPath)) { [System.IO.File]::WriteAllBytes($WebpPath, [byte[]](0x52,0x49,0x46,0x46,0,0,0,0,0x57,0x45,0x42,0x50)) }

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
    [DllImport("user32.dll")] public static extern IntPtr GetActiveWindow();
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
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static void PlaceTopmost(IntPtr handle, int x, int y, int width, int height) {
        SetWindowPos(handle, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW);
    }
    public const uint LEFT_DOWN = 0x0002;
    public const uint LEFT_UP = 0x0004;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(LEFT_DOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(LEFT_UP, 0, 0, 0, UIntPtr.Zero);
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

Add-Type -TypeDefinition @'
public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApiShim {
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out WinRect r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, int flags);
}
'@

$log = Join-Path $OutDir 'tool-choice-ui.log'
Set-Content -Path $log -Value '' -Encoding UTF8
function Log([string]$message) { Add-Content -Path $log -Value $message -Encoding UTF8 }

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
    $found = @()
    foreach ($button in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($button.Current.Name -eq $buttonName) { return $button }
        $found += $button.Current.Name
    }
    Log "BUTTONS_WANTED=$buttonName HAVE=$($found -join ' | ')"
    return $null
}

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-Value($element) {
    try { return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return $null }
}

function Test-Visible($element) {
    if ($null -eq $element) { return $false }
    $rect = $element.Current.BoundingRectangle
    return ($rect.Width -gt 0 -and $rect.Height -gt 0)
}

function Get-AppWindows([int]$processId, $main) {
    $windows = @()
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    foreach ($child in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($child.Current.ProcessId -eq $processId) { $windows += $child }
    }
    # A WPF dialog shown with Owner set sits under its owner in the UIA tree, not on the desktop root.
    if ($null -ne $main) {
        foreach ($inner in $main.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition)) {
            $windows += $inner
        }
    }
    return $windows
}

function Wait-MainWindow($process, [int]$seconds) {
    # A process can own several top-level windows, so the main window is matched by its handle and
    # not by whichever desktop child happens to be listed first.
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        $handle = $process.MainWindowHandle
        foreach ($child in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($child.Current.ProcessId -ne $process.Id) { continue }
            if ([IntPtr]$child.Current.NativeWindowHandle -eq $handle) { return $child }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
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

function Get-SelectedText($combo) {
    # WPF reports the chosen item as the combo's own name; the selection pattern only names items that
    # the popup has realised.
    $names = @($combo.Current.Name)
    try {
        $selection = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
        $names += ($selection.GetCurrentSelection() | ForEach-Object { $_.Current.Name })
    } catch { }
    return (($names | Where-Object { $_ }) -join ' / ')
}

function Save-ScreenShot([string]$path) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $graphics.Dispose()
    $bitmap.Save($path)
    $bitmap.Dispose()
    Log "SHOT=$path"
}

function Get-ComboBoxItems($combo, [string]$openShotPath) {
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 800
    if ($openShotPath) { Save-ScreenShot $openShotPath }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $names = @()
    foreach ($item in $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($item.Current.Name) { $names += $item.Current.Name }
    }
    if (-not $names.Count) {
        foreach ($childWindow in (Get-AppWindows $combo.Current.ProcessId $null)) {
            foreach ($item in $childWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
                if ($item.Current.Name) { $names += $item.Current.Name }
            }
        }
    }
    $expand.Collapse()
    Start-Sleep -Milliseconds 300
    return , $names
}

function Close-AppWindow($process, $window) {
    $close = Get-Button $window 'ปิด'
    if ($null -eq $close) { throw "window '$($window.Current.Name)' has no close button" }
    Invoke-Element $close
    Start-Sleep -Seconds 2
}

function Click-Navigate($process, $main, [string]$buttonName, [string]$windowTitle) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Focus-AppWindow $process $main
        Invoke-Element (Get-Button $main $buttonName)
        $window = Wait-AppWindow $process $main $windowTitle 10
        if ($null -ne $window) {
            Log "STEP=open-window ATTEMPT=$attempt TITLE=$windowTitle"
            return $window
        }
        Log "STEP=open-window ATTEMPT=$attempt TITLE=$windowTitle STATUS=not-opened"
    }
    throw "$windowTitle did not open after clicking $buttonName"
}

function Focus-Dialog($process, $window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][WinMouse]::PlaceTopmost($handle, 30, 30, 1240, 880)
    [WinMouse]::ForceForeground($handle) | Out-Null
    Start-Sleep -Milliseconds 600
}

function Select-ComboBoxItem($combo, [string]$wanted) {
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 800
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    foreach ($item in $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($item.Current.Name -eq $wanted) {
            $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 600
            return $true
        }
    }
    $expand.Collapse()
    return $false
}

function Save-WindowShot($window, [string]$path) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { Log "SHOT_SKIPPED=$path"; return }
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

function Focus-AppWindow($process, $window) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    # The operator keeps the installed app maximised on this screen, so the sandbox window takes its
    # own rect and goes topmost; otherwise a coordinate click falls through to their window.
    [void][WinMouse]::ShowWindow($handle, 9)
    [void][WinMouse]::PlaceTopmost($handle, 30, 30, 1240, 880)
    Start-Sleep -Milliseconds 400
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        [WinMouse]::ForceForeground($handle) | Out-Null
        Start-Sleep -Milliseconds 300
        if ([WinMouse]::GetForegroundWindow() -eq $handle) { return }
    }
    Start-Sleep -Milliseconds 500
}

function Click-Element($process, $window, $element) {
    Focus-AppWindow $process $window
    $rect = $element.Current.BoundingRectangle
    [WinMouse]::Click([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    Start-Sleep -Milliseconds 800
}


# Without this the app would open the operator's live database, so the run refuses to start unless
# the override points at the throwaway copy.
$env:TELEGRAM_CONTROL_STUDIO_DB = $Database
if ($env:TELEGRAM_CONTROL_STUDIO_DB -ne $Database -or -not (Test-Path $Database)) { throw 'sandbox database override is not in place' }
if ([string]::Equals((Resolve-Path $Database).Path, (Resolve-Path $SourceDatabase).Path, [StringComparison]::OrdinalIgnoreCase)) { throw 'sandbox and source databases are the same file' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# The media picker is a native shell dialog, so the path is typed into it rather than clicked.
function Get-ProcessWindowTree($processId, $parent, [int]$depth) {
    # A shell dialog is owned by the window that opened it, so it sits under that window in the UIA
    # tree rather than on the desktop root.
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
        foreach ($inner in (Get-ProcessWindowTree $process.Id $candidate 2)) {
            if ($inner.Current.ClassName -eq '#32770') { return $inner }
        }
    }
    return $null
}

function Send-MediaPath($process, $main, [string]$path) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $dialog = Get-FileDialog $process $main
        if ($null -ne $dialog) {
            $handle = [IntPtr]$dialog.Current.NativeWindowHandle
            [void][WinMouse]::PlaceTopmost($handle, 60, 60, 1000, 700)
            [WinMouse]::ForceForeground($handle) | Out-Null
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


# Engage the kill switch and add one sandbox template per account inside the throwaway DB through the
# app repositories, so the checks never touch the real data and sending stays impossible.
$probe = Join-Path $env:TEMP 'tcs-tool-choice-probe'
New-Item -ItemType Directory -Force -Path $probe | Out-Null
Set-Content -Path (Join-Path $probe 'probe.csproj') -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$PSScriptRoot\..\src\TelegramControlStudio.Infrastructure\TelegramControlStudio.Infrastructure.csproj" />
  </ItemGroup>
</Project>
"@ -Encoding UTF8
Set-Content -Path (Join-Path $probe 'Program.cs') -Value @"
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

var databasePath = args[0];
var body = File.ReadAllText(args[1]).Trim();
await new SqliteDatabase(databasePath).InitializeAsync();
var settings = await new SendSafetyRepository(databasePath).SetKillSwitchAsync(true);
Console.WriteLine($"KILL_SWITCH_SET={settings.KillSwitchEngaged}");
var templates = new TemplateRepository(databasePath);
foreach (var account in await new AccountRepository(databasePath).ListAsync())
{{
    var id = await templates.CreateNextVersionAsync(new MessageTemplate(
        0, account.Id, "Sandbox Preview", "th", body, null, 1, true, DateTimeOffset.UtcNow));
    Console.WriteLine($"SEEDED_TEMPLATE={account.Id}/{id}");
}}
Console.WriteLine("EXPECTED_BODY_READ=" + (body.Length > 20));
// The sandbox is copied from a live database that may still hold a write-ahead log, so the probe
// folds its own writes into the main file before the app opens it.
using (var checkpoint = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
{{
    checkpoint.Open();
    checkpoint.CreateCommand().CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
    checkpoint.CreateCommand().ExecuteNonQuery();
    Console.WriteLine("CHECKPOINTED=True");
}}
"@ -Encoding UTF8
$SandboxBody = 'Sandbox template body for the dropdown check.'
$BodyFile = Join-Path $env:TEMP 'tcs-tool-choice-body.txt'
Set-Content -Path $BodyFile -Value $SandboxBody -Encoding UTF8 -NoNewline
$probeOut = & dotnet run --project $probe -- $Database $BodyFile 2>&1
Log "KILL_PROBE=$($probeOut -join ' | ')"
if (($probeOut -join ' ') -notmatch 'KILL_SWITCH_SET=True') { throw 'failed to engage kill switch in the sandbox DB' }
if (($probeOut -join ' ') -notmatch 'SEEDED_TEMPLATE') { throw 'failed to seed the sandbox template' }

try {
    $process = Start-Process $Exe -PassThru
    $main = Wait-MainWindow $process 40
    if ($null -eq $main -or $main.Current.Name -ne 'Telegram Control Studio') { throw 'main window not found' }
    # The badge is the proof that nothing can be sent from this window, so wait for it instead of
    # assuming the first render already settled.
    $isolated = $false
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline) {
        if (((Get-AllText $main) -join ' / ') -match 'KILL SWITCH') { $isolated = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $isolated) {
        $seen = @(Get-AllText $main)
        Log "ISOLATION_FAIL NAME=$($main.Current.Name) TEXT_COUNT=$($seen.Count) FIRST=$(($seen | Select-Object -First 15) -join ' | ')"
        throw 'sandbox is not isolated: the kill switch badge never appeared'
    }
    Log 'STEP=kill-switch STATUS=engaged'

    # --- C1: the template preview must show the attached picture, not only the text. ---
    $editor = Click-Navigate $process $main 'เทมเพลตและมีเดีย' 'Template Editor'
    Focus-Dialog $process $editor
    Focus-Dialog $process $editor
    Invoke-Element (Get-Button $editor 'เลือก Media')
    $typed = Send-MediaPath $process $main $PngPath
    Log "STEP=choose-media DIALOG_REACHED=$typed"
    $editor = Wait-AppWindow $process $main 'Template Editor' 10
    Focus-Dialog $process $editor
    $image = Find-AutomationId $editor 'PreviewImage'
    $note = Find-AutomationId $editor 'PreviewMediaText'
    Log "STEP=png-preview VISIBLE=$(Test-Visible $image) NOTE=$($note.Current.Name)"
    if (-not (Test-Visible $image)) { throw 'the selected PNG never became a visible preview' }
    Save-WindowShot $editor (Join-Path $OutDir 'ui-template-preview-png.png')

    # A WebP has no Windows codec, so it must be announced as an attachment instead of a broken image.
    Focus-Dialog $process $editor
    Invoke-Element (Get-Button $editor 'เลือก Media')
    $typed2 = Send-MediaPath $process $main $WebpPath
    Log "STEP=choose-webp DIALOG_REACHED=$typed2"
    $editor = Wait-AppWindow $process $main 'Template Editor' 10
    Focus-Dialog $process $editor
    $image = Find-AutomationId $editor 'PreviewImage'
    $note = Find-AutomationId $editor 'PreviewMediaText'
    $texts = (Get-AllText $editor) -join ' / '
    Log "STEP=webp-preview VISIBLE=$(Test-Visible $image)"
    if (Test-Visible $image) { throw 'a .webp was shown as a picture although Windows cannot decode it' }
    if ($texts -notmatch 'ไฟล์แนบ') { throw "the .webp attachment note is missing: $texts" }
    Save-WindowShot $editor (Join-Path $OutDir 'ui-template-preview-webp.png')
    Close-AppWindow $process $editor

    # --- C2: the wizard must offer saved templates by name. ---
    $wizard = Click-Navigate $process $main 'Campaign Wizard' 'Campaign Wizard'
    Focus-Dialog $process $wizard
    $templateBox = Find-AutomationId $wizard 'TemplateBox'
    if ($null -eq $templateBox) { throw 'TemplateBox combo not found' }
    $items = Get-ComboBoxItems $templateBox (Join-Path $OutDir 'ui-campaign-template-dropdown-open.png')
    Log "STEP=template-dropdown ITEMS=$($items -join '; ')"
    if (@($items | Where-Object { $_ -like 'Sandbox Preview*' }).Count -eq 0) { throw 'the template dropdown does not list the saved template by name' }
    $picked = Select-ComboBoxItem $templateBox 'Sandbox Preview (v1)'
    Log "STEP=template-pick PICKED=$picked"
    if (-not $picked) { throw 'the sandbox template could not be chosen from the dropdown' }
    $selected = Get-SelectedText $templateBox
    $hint = (Get-AllText $wizard) -join ' / '
    Log "STEP=template-selected VALUE=$selected HINT_MATCH=$($hint -match 'มีเทมเพลต')"
    if ($hint -notmatch 'มีเทมเพลต') { throw 'the template count hint is missing' }
    $messageBox = Find-AutomationId $wizard 'MessageTextBox'
    $messageText = (Get-Value $messageBox) -join ''
    Log "STEP=template-body TEXT=$messageText"
    if ($messageText.Trim() -ne $SandboxBody) { throw 'choosing a template did not fill the message box with its body' }
    Save-WindowShot $wizard (Join-Path $OutDir 'ui-campaign-template-dropdown.png')
    Close-AppWindow $process $wizard

    # --- C3: invite links must pick the chat from the synced groups. ---
    $links = Click-Navigate $process $main 'Invite Links' 'Invite Links'
    Focus-Dialog $process $links
    $chatBox = Find-AutomationId $links 'ChatBox'
    if ($null -eq $chatBox) { throw 'ChatBox combo not found' }
    $chats = Get-ComboBoxItems $chatBox (Join-Path $OutDir 'ui-invite-chat-dropdown-open.png')
    Log "STEP=chat-dropdown COUNT=$(@($chats).Count) FIRST=$(( $chats | Select-Object -First 3 ) -join '; ')"
    if (@($chats | Where-Object { $_ -match '-\d+' }).Count -eq 0) { throw 'the chat dropdown does not offer a negative TDLib chat id' }
    # UIA never names the chosen item of a WPF combo, so the preselection is proven behaviourally:
    # pressing create with a chat chosen stops at the missing TDLib connection instead of the
    # "pick a chat first" message. The sandbox has no connection, so nothing reaches Telegram.
    Invoke-Element (Get-Button $links 'สร้าง Join Request Link')
    Start-Sleep -Seconds 2
    $links = Wait-AppWindow $process $main 'Invite Links' 10
    $status = (Get-AllText $links) -join ' / '
    Log "STEP=chat-preselected STATUS_TEXT=$status"
    if ($status -notmatch 'ยังไม่เชื่อมต่อ') { throw "the invite window had no chat preselected: $status" }
    if ($status -match 'กรุณาเลือก') { throw "the invite window still asks for a chat: $status" }
    Save-WindowShot $links (Join-Path $OutDir 'ui-invite-chat-dropdown.png')
    Close-AppWindow $process $links

    Log 'STEP=done STATUS=ok'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Seconds 1
    if ($KeepSandbox) {
        Log "SANDBOX_KEPT=$Database"
    } else {
        foreach ($suffix in @('', '-wal', '-shm')) { Remove-Item ($Database + $suffix) -Force -ErrorAction SilentlyContinue }
        Log 'SANDBOX_REMOVED=true'
    }
}
