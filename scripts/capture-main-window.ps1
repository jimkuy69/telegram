param(
    [string]$Exe = 'C:\Users\ADMIN\AppData\Local\ProgramsTelegramControlStudio\TelegramControlStudio.exe',
    [string]$Out = 'C:\Users\ADMIN\Documents\Qoder\2026-10-29\4018d0d5\telegram\artifacts\ui-autorun-badge.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$signature = @'
using System;
using System.Runtime.InteropServices;
public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class WinApi {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out WinRect r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
}
'@
Add-Type -TypeDefinition $signature

$source = Join-Path $env:LOCALAPPDATA 'TelegramControlStudio\telegram-control-studio.db'
$copy = Join-Path $env:TEMP ("tcs-ui-" + [guid]::NewGuid().ToString('N') + '.db')
Copy-Item $source $copy -Force
$env:TELEGRAM_CONTROL_STUDIO_DB = $copy

try {
    $process = Start-Process $Exe -PassThru
    Start-Sleep -Seconds 12
    if ($process.HasExited) { throw "app exited with code $($process.ExitCode)" }
    $process.Refresh()
    $handle = $process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        $deadline = (Get-Date).AddSeconds(15)
        while ($handle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            $process.Refresh()
            $handle = $process.MainWindowHandle
        }
    }
    [void][WinApi]::ShowWindow($handle, 9)
    Start-Sleep -Seconds 2
    $rect = New-Object WinRect
    [void][WinApi]::GetWindowRect($handle, [ref]$rect)
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    # PW_RENDERFULLCONTENT: WPF composes through a redirection surface, so a plain
    # PrintWindow (flag 0) produces a black frame.
    [void][WinApi]::PrintWindow($process.MainWindowHandle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($Out)
    $bitmap.Dispose()
    Write-Host "TITLE=$($process.MainWindowTitle)"
    Write-Host "SAVED=$Out ${width}x${height}"
}
finally {
    Get-Process TelegramControlStudio -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item $copy -Force -ErrorAction SilentlyContinue
}
