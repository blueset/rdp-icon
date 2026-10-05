[CmdletBinding()]
param(
    [ValidateRange(10, 600)]
    [int]$HoldSeconds = 120
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public sealed class RdpWindow {
    public long Handle;
    public uint ProcessId;
    public string Title;
}

public static class RdpIconNative {
    private delegate bool EnumCallback(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, UIntPtr kind, IntPtr icon,
        uint flags, uint timeout, out UIntPtr result);
    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint error);

    public static RdpWindow[] Windows() {
        var windows = new List<RdpWindow>();
        EnumCallback callback = (window, data) => {
            var text = new StringBuilder(1024);
            GetWindowText(window, text, text.Capacity);
            if (text.Length != 0) {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                windows.Add(new RdpWindow {
                    Handle = window.ToInt64(), ProcessId = processId,
                    Title = text.ToString()
                });
            }
            return true;
        };
        if (!EnumWindows(callback, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return windows.ToArray();
    }

    public static bool SameWindow(long handle, uint processId, string title) {
        var window = new IntPtr(handle);
        if (!IsWindow(window)) return false;
        uint currentProcess;
        GetWindowThreadProcessId(window, out currentProcess);
        var text = new StringBuilder(1024);
        GetWindowText(window, text, text.Capacity);
        return currentProcess == processId && text.ToString() == title;
    }

    public static long IconMessage(long handle, uint message, uint kind, long icon) {
        UIntPtr result;
        SetLastError(0);
        if (SendMessageTimeout(new IntPtr(handle), message, new UIntPtr(kind),
            new IntPtr(icon), 0x22, 1000, out result) == IntPtr.Zero) {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error == 0 ? 1460 : error,
                "Icon message failed for window " + handle);
        }
        return unchecked((long)result.ToUInt64());
    }
}
'@

$artifactDirectory = Join-Path $PSScriptRoot 'smoke-test-artifacts'
[void][System.IO.Directory]::CreateDirectory($artifactDirectory)
$stopPath = Join-Path $artifactDirectory 'stop'
if (Test-Path -LiteralPath $stopPath) {
    throw "A stop signal already exists: $stopPath. Use a fresh test directory."
}
$rules = @(
    @{ Title = 'hanj-odsp-canada-central-b'; Label = 'CA'; Color = '#9333EA' }
    @{ Title = 'hanj-odsp-westus2'; Label = 'W1'; Color = '#16A34A' }
    @{ Title = 'hanj-odsp-westus2-b'; Label = 'W2'; Color = '#2563EB' }
    @{ Title = 'hanj-odsp-westus2-c'; Label = 'W3'; Color = '#EA580C' }
)

function New-TestIcon {
    param([string]$Label, [string]$Color, [int]$Size, [string]$Path)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $brush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml($Color))
    $font = [System.Drawing.Font]::new(
        'Segoe UI', [single]($Size * 0.43),
        [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::new()
    $rawIcon = [IntPtr]::Zero
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.FillEllipse($brush, 0, 0, ($Size - 1), ($Size - 1))
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString($Label, $font, [System.Drawing.Brushes]::White,
            [System.Drawing.RectangleF]::new(0, 0, $Size, $Size), $format)
        $rawIcon = $bitmap.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($rawIcon).Clone()
        try {
            $stream = [System.IO.File]::Create($Path)
            try { $icon.Save($stream) } finally { $stream.Dispose() }
        } catch {
            $icon.Dispose()
            throw
        }
        return $icon
    } finally {
        if ($rawIcon -ne [IntPtr]::Zero) {
            if (-not [RdpIconNative]::DestroyIcon($rawIcon)) {
                Write-Warning "Could not release generated icon $rawIcon."
            }
        }
        $format.Dispose()
        $font.Dispose()
        $brush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-TaskbarCapture {
    param([string]$Name)
    $taskbar = [RdpIconNative]::FindWindow('Shell_TrayWnd', $null)
    if ($taskbar -eq [IntPtr]::Zero) { throw 'The Windows taskbar was not found.' }
    $rect = [RdpIconNative+Rect]::new()
    if (-not [RdpIconNative]::GetWindowRect($taskbar, [ref]$rect)) {
        throw 'Could not read the taskbar bounds.'
    }
    $bitmap = [System.Drawing.Bitmap]::new(
        ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $artifactDirectory $Name),
            [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$targets = [System.Collections.Generic.List[object]]::new()
$windows = [RdpIconNative]::Windows()
foreach ($rule in $rules) {
    $matches = @($windows | Where-Object {
        $_.Title -ceq $rule.Title -and
        (Get-Process -Id $_.ProcessId -ErrorAction Stop).ProcessName -eq 'msrdc'
    })
    if ($matches.Count -ne 1) {
        throw "Expected one msrdc window titled '$($rule.Title)', found $($matches.Count)."
    }
    $targets.Add([pscustomobject]@{
        Window = $matches[0]; Rule = $rule; Small = $null; Large = $null
        OldSmall = [long]0; OldLarge = [long]0
        SmallChanged = $false; LargeChanged = $false
    })
}

$previousDpi = [RdpIconNative]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$report = [ordered]@{ Started = (Get-Date).ToString('o'); Windows = @(); Restored = @() }
$restoreErrors = [System.Collections.Generic.List[string]]::new()
try {
    Save-TaskbarCapture 'before.png'
    foreach ($target in $targets) {
        $title = $target.Window.Title
        $target.Small = New-TestIcon $target.Rule.Label $target.Rule.Color 16 (
            Join-Path $artifactDirectory "$title-small.ico")
        $target.Large = New-TestIcon $target.Rule.Label $target.Rule.Color 48 (
            Join-Path $artifactDirectory "$title-large.ico")
        if (-not [RdpIconNative]::SameWindow(
            $target.Window.Handle, $target.Window.ProcessId, $title)) {
            throw "The target window changed before applying its icon: $title."
        }
        $target.OldSmall = [RdpIconNative]::IconMessage(
            $target.Window.Handle, 0x80, 0, $target.Small.Handle.ToInt64())
        $target.SmallChanged = $true
        $target.OldLarge = [RdpIconNative]::IconMessage(
            $target.Window.Handle, 0x80, 1, $target.Large.Handle.ToInt64())
        $target.LargeChanged = $true
        $small = [RdpIconNative]::IconMessage($target.Window.Handle, 0x7F, 0, 0)
        $large = [RdpIconNative]::IconMessage($target.Window.Handle, 0x7F, 1, 0)
        $verified = $small -eq $target.Small.Handle.ToInt64() -and
            $large -eq $target.Large.Handle.ToInt64()
        $report.Windows += [ordered]@{
            Title = $title; ProcessId = $target.Window.ProcessId
            Handle = $target.Window.Handle; Label = $target.Rule.Label
            Color = $target.Rule.Color; Verified = $verified
            OriginalSmall = $target.OldSmall; OriginalLarge = $target.OldLarge
            CurrentSmall = $small; CurrentLarge = $large
        }
        if (-not $verified) { throw "Icon readback did not match for $title." }
        Write-Output "APPLIED $title => $($target.Rule.Label) $($target.Rule.Color)"
    }
    Start-Sleep -Seconds 3
    Save-TaskbarCapture 'during.png'
    $report | ConvertTo-Json -Depth 6 | Set-Content (
        Join-Path $artifactDirectory 'result.json') -Encoding utf8
    Write-Output "READY: four icon overrides verified; restoring within $HoldSeconds seconds."
    $deadline = [DateTime]::UtcNow.AddSeconds($HoldSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $stopPath)) {
        Start-Sleep -Seconds 1
    }
    foreach ($target in $targets) {
        $small = [RdpIconNative]::IconMessage($target.Window.Handle, 0x7F, 0, 0)
        $large = [RdpIconNative]::IconMessage($target.Window.Handle, 0x7F, 1, 0)
        if ($small -ne $target.Small.Handle.ToInt64() -or
            $large -ne $target.Large.Handle.ToInt64()) {
            throw "The client reset its icon during the test: $($target.Window.Title)."
        }
    }
    Save-TaskbarCapture 'end-of-test.png'
} finally {
    foreach ($target in $targets) {
        if (-not ($target.SmallChanged -or $target.LargeChanged)) {
            if ($null -ne $target.Small) { $target.Small.Dispose() }
            if ($null -ne $target.Large) { $target.Large.Dispose() }
            continue
        }
        try {
            if (-not [RdpIconNative]::SameWindow(
                $target.Window.Handle, $target.Window.ProcessId, $target.Window.Title)) {
                throw "The original window no longer matches: $($target.Window.Title)."
            }
            if ($target.SmallChanged) {
                [void][RdpIconNative]::IconMessage(
                    $target.Window.Handle, 0x80, 0, $target.OldSmall)
                if ([RdpIconNative]::IconMessage(
                    $target.Window.Handle, 0x7F, 0, 0) -ne $target.OldSmall) {
                    throw "Small icon restoration failed: $($target.Window.Title)."
                }
            }
            if ($target.LargeChanged) {
                [void][RdpIconNative]::IconMessage(
                    $target.Window.Handle, 0x80, 1, $target.OldLarge)
                if ([RdpIconNative]::IconMessage(
                    $target.Window.Handle, 0x7F, 1, 0) -ne $target.OldLarge) {
                    throw "Large icon restoration failed: $($target.Window.Title)."
                }
            }
            $report.Restored += $target.Window.Title
            Write-Output "RESTORED $($target.Window.Title)"
        } catch {
            $restoreErrors.Add($_.Exception.Message)
            Write-Warning $_.Exception.Message
        } finally {
            if ($null -ne $target.Small) { $target.Small.Dispose() }
            if ($null -ne $target.Large) { $target.Large.Dispose() }
        }
    }
    Start-Sleep -Seconds 2
    Save-TaskbarCapture 'after.png'
    $report.Completed = (Get-Date).ToString('o')
    $report.RestorationErrors = @($restoreErrors.ToArray())
    $report | ConvertTo-Json -Depth 6 | Set-Content (
        Join-Path $artifactDirectory 'result.json') -Encoding utf8
    if ($previousDpi -ne [IntPtr]::Zero) {
        [void][RdpIconNative]::SetThreadDpiAwarenessContext($previousDpi)
    }
    if ($restoreErrors.Count -gt 0) {
        throw ($restoreErrors -join '; ')
    }
}
