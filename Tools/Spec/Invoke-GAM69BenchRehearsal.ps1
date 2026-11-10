<#
.SYNOPSIS
  GAM-69 Gate E rehearsal: drives the Windows player through real OS input
  (SendInput keyboard scan codes and mouse clicks), records the screen with
  ffmpeg, and saves screenshots. It does not touch the game process; it only
  presses keys a player would press.

.DESCRIPTION
  Scenario "good": gym -> Bench station -> lift off (Space) -> wait for START
  -> hold S to the chest -> wait for PRESS -> hold W to lockout -> wait for
  RACK -> Space to rerack -> result -> R retry.
  Scenario "abort": lift off -> lower -> Esc (spotters) -> result -> G gym.
  Signals are watched in the player log (the bench writes "GAM69_UI" lines),
  so key timing follows the referee exactly as a player watching the HUD.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [ValidateSet('good', 'abort', 'early', 'all')][string]$Scenario = 'all',
    [int]$Width = 1600,
    [int]$Height = 900
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class GamInput {
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION u; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    const uint KEYEVENTF_SCANCODE = 0x0008, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_EXTENDED = 0x0001;
    public static void Key(ushort scan, bool up, bool extended) {
        var i = new INPUT[1]; i[0].type = 1;
        i[0].u.ki.wScan = scan;
        i[0].u.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDED : 0);
        SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Click() {
        var i = new INPUT[2]; i[0].type = 0; i[0].u.mi.dwFlags = 0x0002; i[1].type = 0; i[1].u.mi.dwFlags = 0x0004;
        SendInput(2, i, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

# Physical pixels: without this, on a scaled display the client rect is
# virtualized and screenshots capture only part of the game window.
[GamInput]::SetProcessDPIAware() | Out-Null

$Scan = @{ Space = 0x39; Enter = 0x1C; S = 0x1F; W = 0x11; Esc = 0x01; R = 0x13; G = 0x22; C = 0x2E }

function Key-Down([string]$k) { [GamInput]::Key([uint16]$Scan[$k], $false, $false) }
function Key-Up([string]$k) { [GamInput]::Key([uint16]$Scan[$k], $true, $false) }
function Key-Tap([string]$k, [int]$ms = 90) { Key-Down $k; Start-Sleep -Milliseconds $ms; Key-Up $k }

$logPath = Join-Path $OutDir 'player.log'
$events = Join-Path $OutDir 'rehearsal-events.txt'
function Note([string]$m) { $line = "{0:HH:mm:ss.fff} {1}" -f (Get-Date), $m; Add-Content $events $line; Write-Host $line }

function Wait-Log([string]$pattern, [int]$timeoutS = 30, [int]$after = 0) {
    $deadline = (Get-Date).AddSeconds($timeoutS)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $logPath) {
            $lines = @(Get-Content $logPath -ErrorAction SilentlyContinue)
            for ($i = $after; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $pattern) { return $i + 1 } }
        }
        Start-Sleep -Milliseconds 50
    }
    throw "Timed out waiting for log pattern '$pattern'"
}
function Log-Count() { if (Test-Path $logPath) { @(Get-Content $logPath -ErrorAction SilentlyContinue).Count } else { 0 } }

Add-Type -AssemblyName System.Drawing
function Shot([string]$name) {
    $p = [GamInput+POINT]@{ X = 0; Y = 0 }; [GamInput]::ClientToScreen($script:hwnd, [ref]$p) | Out-Null
    $r = New-Object GamInput+RECT; [GamInput]::GetClientRect($script:hwnd, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($p.X, $p.Y, 0, 0, $bmp.Size)
    $file = Join-Path $OutDir ($name + '.png'); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); Note "screenshot $file"
}
# Click at a position given in 1920x1080 reference-canvas coordinates (canvas
# scaler match 0.5 of width/height; for 16:9 windows this is a plain scale).
function Click-Ref([double]$x, [double]$y) {
    $p = [GamInput+POINT]@{ X = 0; Y = 0 }; [GamInput]::ClientToScreen($script:hwnd, [ref]$p) | Out-Null
    $r = New-Object GamInput+RECT; [GamInput]::GetClientRect($script:hwnd, [ref]$r) | Out-Null
    $sx = ($r.Right - $r.Left) / 1920.0; $sy = ($r.Bottom - $r.Top) / 1080.0
    [GamInput]::SetCursorPos([int]($p.X + $x * $sx), [int]($p.Y + $y * $sy)) | Out-Null
    Start-Sleep -Milliseconds 150; [GamInput]::Click(); Note "click ref($x,$y)"
}

Note "launch $Exe"
$proc = Start-Process -FilePath $Exe -ArgumentList @('-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-logFile', $logPath) -PassThru
$script:hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 200 -and $script:hwnd -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 100; $proc.Refresh(); $script:hwnd = $proc.MainWindowHandle }
if ($script:hwnd -eq [IntPtr]::Zero) { throw 'no player window' }
[GamInput]::SetForegroundWindow($script:hwnd) | Out-Null
Start-Sleep -Seconds 6

$video = Join-Path $OutDir 'bench-rehearsal.mp4'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Get-Command ffmpeg).Source
# Capture the window's screen region: gdigrab by window title returns a
# stale frame for a Direct3D window.
$cp = [GamInput+POINT]@{ X = 0; Y = 0 }; [GamInput]::ClientToScreen($script:hwnd, [ref]$cp) | Out-Null
$cr = New-Object GamInput+RECT; [GamInput]::GetClientRect($script:hwnd, [ref]$cr) | Out-Null
$vw = (($cr.Right - $cr.Left) -band -2); $vh = (($cr.Bottom - $cr.Top) -band -2)
$psi.Arguments = "-y -loglevel error -f gdigrab -framerate 30 -offset_x $($cp.X) -offset_y $($cp.Y) -video_size ${vw}x${vh} -i desktop -c:v libx264 -preset veryfast -pix_fmt yuv420p `"$video`""
Note "video region $($cp.X),$($cp.Y) ${vw}x${vh}"
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.CreateNoWindow = $true
$ff = [System.Diagnostics.Process]::Start($psi)
Note "recording $video (window '$($proc.MainWindowTitle)')"

function Enter-BenchFromMenu() {
    Shot '01_main_menu'
    # Main menu "PLAY - ENTER TRAINING GYM" (card x 76 + button 44..596, y 540-48),
    # then the bench card's select button (deck centre 881 + card -24, button -82).
    Click-Ref 396 492
    Start-Sleep -Seconds 3
    Shot '02_training_gym'
    Click-Ref 360 987
    $script:mark = Wait-Log 'GAM69_UI screen=Setup' 60
    Start-Sleep -Seconds 3
    Shot '03_bench_setup'
}

function Run-Attempt([string]$kind) {
    [GamInput]::SetForegroundWindow($script:hwnd) | Out-Null
    $m = Log-Count
    Note "attempt ${kind}: SPACE lift-off"
    Key-Tap 'Space'
    if ($kind -eq 'early') {
        Wait-Log 'GAM69_UI command=Start' 30 $m | Out-Null
        Note 'START heard: hold S'; Key-Down 'S'
        Wait-Log 'GAM69_UI touch' 20 $m | Out-Null
        Key-Up 'S'; Note 'touch: pressing W before PRESS (fault)'; Key-Down 'W'
        Start-Sleep -Seconds 3; Key-Up 'W'
        Wait-Log 'GAM69_UI screen=Result' 60 $m | Out-Null
        Start-Sleep -Seconds 2; Shot "result_$kind"; return
    }
    Wait-Log 'GAM69_UI command=Start' 30 $m | Out-Null
    Start-Sleep -Milliseconds 300; Shot "10_${kind}_start"
    Note 'START heard: hold S to lower'
    Key-Down 'S'
    if ($kind -eq 'abort') {
        Start-Sleep -Milliseconds 700; Key-Up 'S'; Note 'ESC abort'; Key-Tap 'Esc'
        Start-Sleep -Milliseconds 800; Shot "11_${kind}_spotters"
        Wait-Log 'GAM69_UI screen=Result' 60 $m | Out-Null
        Start-Sleep -Seconds 2; Shot "19_${kind}_result"; return
    }
    Start-Sleep -Milliseconds 600; Shot "11_${kind}_descent"
    Wait-Log 'GAM69_UI touch' 20 $m | Out-Null
    Key-Up 'S'; Note 'touch: release S, hold still'
    Start-Sleep -Milliseconds 150; Shot "12_${kind}_touch"
    Start-Sleep -Milliseconds 120; Shot "13_${kind}_pause"
    Wait-Log 'GAM69_UI command=Press' 20 $m | Out-Null
    Start-Sleep -Milliseconds 150; Shot "14_${kind}_press_signal"
    Note 'PRESS heard: hold W'; Key-Down 'W'
    Start-Sleep -Milliseconds 450; Shot "15_${kind}_pressing"
    Wait-Log 'GAM69_UI lockout' 20 $m | Out-Null
    Key-Up 'W'; Note 'lockout: release W'
    Shot "16_${kind}_lockout"
    Wait-Log 'GAM69_UI command=Rack' 20 $m | Out-Null
    Start-Sleep -Milliseconds 200; Shot "17_${kind}_rack_signal"
    Note 'RACK heard: SPACE to rerack'; Key-Tap 'Space'
    Start-Sleep -Milliseconds 900; Shot "18_${kind}_rerack"
    Wait-Log 'GAM69_UI screen=Result' 60 $m | Out-Null
    Start-Sleep -Seconds 2; Shot "19_${kind}_result"
}

try {
    Enter-BenchFromMenu
    Key-Tap 'C'; Start-Sleep -Milliseconds 800; Shot '04_camera_side'; Key-Tap 'C'; Key-Tap 'C'; Key-Tap 'C'; Start-Sleep -Milliseconds 600
    if ($Scenario -in 'good', 'all') { Run-Attempt 'good'; $m = Log-Count; Key-Tap 'R'; Wait-Log 'GAM69_UI screen=Setup' 60 $m | Out-Null; Start-Sleep -Seconds 3; Shot '20_after_retry_setup' }
    if ($Scenario -in 'early', 'all') { Run-Attempt 'early'; $m = Log-Count; Key-Tap 'R'; Wait-Log 'GAM69_UI screen=Setup' 60 $m | Out-Null; Start-Sleep -Seconds 3 }
    if ($Scenario -in 'abort', 'all') { Run-Attempt 'abort'; Key-Tap 'G'; Start-Sleep -Seconds 4; Shot '30_back_in_gym' }
    Note 'rehearsal complete'
}
finally {
    Start-Sleep -Seconds 1
    if ($ff -and -not $ff.HasExited) { $ff.StandardInput.Write('q'); $ff.StandardInput.Flush(); if (-not $ff.WaitForExit(15000)) { $ff.Kill() } }
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $proc.HasExited) { Stop-Process -Id $proc.Id } }
    Note "log $logPath"
}
