<#
.SYNOPSIS
Saves disk space between rips. Every rip exports the same shared game assets again, byte for byte but under IDs of its
own, so a file identical to one in another rip is replaced with a hard link to it: one copy on disk, seen at both
paths. Each rip keeps all its files and their .meta files, so nothing that reads a rip notices. Deleting a rip is still
safe, because a linked file's data stays until its last path is gone. Changes nothing outside the rip folder.

.PARAMETER RipFolder
The folder holding your rips.

.PARAMETER Scene
The rip to link to the others. Without it, every rip is linked to the ones before it.

.PARAMETER ReportOnly
Only says how much would be saved, changing nothing.
#>
param(
    [Parameter(Mandatory = $true)][string]$RipFolder,
    [string]$Scene,
    [switch]$ReportOnly
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public static class RipFileSharing
{
    // Files this small live inside the file table anyway, so a link saves nothing.
    private const long MIN_BYTES = 4096;
    private const int BUFFER_BYTES = 1 << 20;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int memcmp(byte[] a, byte[] b, UIntPtr count);

    public static long FilesLinked;
    public static long BytesSaved;

    // Matches are looked for at the same path in the other rips, because that's how the same asset comes out.
    public static void Share(string rip, IList<string> others, bool reportOnly)
    {
        foreach (string file in Directory.EnumerateFiles(rip, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;
            string relative = file.Substring(rip.Length).TrimStart('\\');
            if (relative.StartsWith(@"ExportedProject\Assets\Scenes\", StringComparison.OrdinalIgnoreCase))
                continue;
            var info = new FileInfo(file);
            if (info.Length < MIN_BYTES)
                continue;
            foreach (string other in others)
            {
                string match = Path.Combine(other, relative);
                var matchInfo = new FileInfo(match);
                if (!matchInfo.Exists || matchInfo.Length != info.Length || !SameBytes(file, match))
                    continue;
                if (reportOnly)
                {
                    FilesLinked++;
                    BytesSaved += info.Length;
                }
                else
                    Link(file, match, info.Length);
                break;
            }
        }
    }

    private static void Link(string file, string match, long length)
    {
        string spare = file + ".ocklink";
        if (File.Exists(spare))
            File.Delete(spare);
        if (!CreateHardLink(spare, match, IntPtr.Zero))
            throw new IOException("Couldn't link " + file + ", error " + Marshal.GetLastWin32Error() + ". The rip folder's drive may not support hard links.");
        File.Delete(file);
        File.Move(spare, file);
        FilesLinked++;
        BytesSaved += length;
    }

    private static bool SameBytes(string a, string b)
    {
        using (var first = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_BYTES))
        using (var second = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_BYTES))
        {
            var bufferA = new byte[BUFFER_BYTES];
            var bufferB = new byte[BUFFER_BYTES];
            while (true)
            {
                int readA = first.Read(bufferA, 0, BUFFER_BYTES);
                int readB = second.Read(bufferB, 0, BUFFER_BYTES);
                if (readA != readB)
                    return false;
                if (readA == 0)
                    return true;
                if (memcmp(bufferA, bufferB, (UIntPtr)readA) != 0)
                    return false;
            }
        }
    }
}
'@

$rips = @(Get-ChildItem $RipFolder -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'ExportedProject\Assets\Scenes') } | Sort-Object LastWriteTime | Select-Object -ExpandProperty FullName)
$timer = [Diagnostics.Stopwatch]::StartNew()
if ($Scene) {
    $rip = Join-Path $RipFolder $Scene
    [RipFileSharing]::Share($rip, [string[]]@($rips | Where-Object { $_ -ne $rip }), [bool]$ReportOnly)
}
else {
    for ($i = 1; $i -lt $rips.Count; $i++) {
        [RipFileSharing]::Share($rips[$i], [string[]]@($rips[0..($i - 1)]), [bool]$ReportOnly)
    }
}
$verb = if ($ReportOnly) { 'Would save' } else { 'Saved' }
Write-Host ("{0} {1:0.0} GB by sharing {2} files with other rips, in {3:0} seconds." -f $verb, ([RipFileSharing]::BytesSaved / 1GB), [RipFileSharing]::FilesLinked, $timer.Elapsed.TotalSeconds)
