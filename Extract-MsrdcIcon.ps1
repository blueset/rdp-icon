[CmdletBinding()]
param(
    [string]$SourcePath,
    [ValidateRange(1, 65535)]
    [int]$ResourceId = 384,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'extracted-icons')
)

$ErrorActionPreference = 'Stop'
if (-not $SourcePath) {
    $SourcePath = (Get-Process msrdc -ErrorAction Stop | Select-Object -First 1).Path
}
$SourcePath = (Resolve-Path -LiteralPath $SourcePath -ErrorAction Stop).ProviderPath

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class MsrdcIconResources {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resource);

    public static IntPtr Open(string path) {
        // Read resources without executing the module or loading its dependencies.
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x60);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return module;
    }

    public static byte[] Read(IntPtr module, int type, int id) {
        var resource = FindResource(module, new IntPtr(id), new IntPtr(type));
        if (resource == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        uint size = SizeofResource(module, resource);
        if (size == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var loaded = LoadResource(module, resource);
        if (loaded == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var pointer = LockResource(loaded);
        if (pointer == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var bytes = new byte[checked((int)size)];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
}
'@

function ConvertTo-Ico {
    param([object[]]$Frames)
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Frames.Count)
        [uint32]$offset = 6 + 16 * $Frames.Count
        foreach ($frame in $Frames) {
            $writer.Write([byte]$(if ($frame.Width -eq 256) { 0 } else { $frame.Width }))
            $writer.Write([byte]$(if ($frame.Height -eq 256) { 0 } else { $frame.Height }))
            $writer.Write([byte]$frame.ColorCount)
            $writer.Write([byte]0)
            $writer.Write([uint16]$frame.Planes)
            $writer.Write([uint16]$frame.BitDepth)
            $writer.Write([uint32]$frame.Data.Length)
            $writer.Write($offset)
            $offset += $frame.Data.Length
        }
        foreach ($frame in $Frames) {
            $writer.Write([byte[]]$frame.Data)
        }
        $writer.Flush()
        return ,$stream.ToArray()
    } finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$module = [MsrdcIconResources]::Open($SourcePath)
$frames = [System.Collections.Generic.List[object]]::new()
try {
    $group = [MsrdcIconResources]::Read($module, 14, $ResourceId)
    if ($group.Length -lt 6 -or
        [BitConverter]::ToUInt16($group, 0) -ne 0 -or
        [BitConverter]::ToUInt16($group, 2) -ne 1) {
        throw "Resource $ResourceId is not a valid icon group."
    }
    $count = [BitConverter]::ToUInt16($group, 4)
    if ($count -eq 0 -or $group.Length -ne (6 + 14 * $count)) {
        throw "Resource $ResourceId has an invalid icon group directory."
    }
    for ($index = 0; $index -lt $count; $index++) {
        $position = 6 + 14 * $index
        $imageId = [BitConverter]::ToUInt16($group, $position + 12)
        $data = [MsrdcIconResources]::Read($module, 3, $imageId)
        if ($data.Length -ne [BitConverter]::ToUInt32($group, $position + 8)) {
            throw "Image resource $imageId does not match its declared length."
        }
        $isPng = $data.Length -ge 8 -and
            [BitConverter]::ToString($data, 0, 8) -eq '89-50-4E-47-0D-0A-1A-0A'
        $frames.Add([pscustomobject]@{
            Width = $(if ($group[$position] -eq 0) { 256 } else { [int]$group[$position] })
            Height = $(if ($group[$position + 1] -eq 0) { 256 } else { [int]$group[$position + 1] })
            ColorCount = $group[$position + 2]
            Planes = [BitConverter]::ToUInt16($group, $position + 4)
            BitDepth = [BitConverter]::ToUInt16($group, $position + 6)
            ImageResourceId = $imageId
            Encoding = $(if ($isPng) { 'PNG' } else { 'DIB' })
            Data = $data
        })
    }
} finally {
    if (-not [MsrdcIconResources]::FreeLibrary($module)) {
        throw 'Could not release the source resource mapping.'
    }
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)
$icoPath = Join-Path $OutputDirectory 'msrdc-connection.ico'
$icoBytes = ConvertTo-Ico -Frames $frames.ToArray()
[System.IO.File]::WriteAllBytes($icoPath, $icoBytes)

$images = foreach ($frame in $frames) {
    $pngPath = Join-Path $OutputDirectory (
        'msrdc-connection-{0}x{1}-{2}bpp.png' -f $frame.Width, $frame.Height, $frame.BitDepth)
    $singleIco = ConvertTo-Ico -Frames @($frame)
    $stream = [System.IO.MemoryStream]::new($singleIco, $false)
    try {
        $icon = [System.Drawing.Icon]::new($stream)
        try {
            $bitmap = $icon.ToBitmap()
            try {
                if ($bitmap.Width -ne $frame.Width -or $bitmap.Height -ne $frame.Height) {
                    throw "The decoded dimensions do not match image resource $($frame.ImageResourceId)."
                }
                if ($frame.Encoding -eq 'PNG') {
                    [System.IO.File]::WriteAllBytes($pngPath, $frame.Data)
                } else {
                    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
                }
            } finally { $bitmap.Dispose() }
        } finally { $icon.Dispose() }
    } finally { $stream.Dispose() }
    [pscustomobject]@{
        Width = $frame.Width; Height = $frame.Height; BitDepth = $frame.BitDepth
        Encoding = $frame.Encoding; Bytes = $frame.Data.Length
        ImageResourceId = $frame.ImageResourceId; PngPath = $pngPath
    }
}

$manifest = [ordered]@{
    SourcePath = $SourcePath
    FileVersion = (Get-Item -LiteralPath $SourcePath).VersionInfo.FileVersion
    GroupResourceId = $ResourceId
    ImageCount = $frames.Count
    IcoPath = $icoPath
    Images = @($images)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (
    Join-Path $OutputDirectory 'msrdc-connection.json') -Encoding utf8
$images | Sort-Object Width, Height, BitDepth |
    Format-Table Width, Height, BitDepth, Encoding, Bytes, ImageResourceId -AutoSize
Write-Output "Extracted $($frames.Count) images to $icoPath"
