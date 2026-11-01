param([int]$UnityId, [string]$Out, [int]$Minutes = 45)

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Win {
  delegate bool Cb(IntPtr h, IntPtr l);
  [DllImport("user32")] static extern bool EnumWindows(Cb c, IntPtr l);
  [DllImport("user32")] static extern bool EnumChildWindows(IntPtr p, Cb c, IntPtr l);
  [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
  static string T(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
  public static string Busy(uint pid) {
    var found = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      string t = T(h);
      if (t.IndexOf("Hold on", StringComparison.OrdinalIgnoreCase) >= 0 || t == "Construction Kit" || t.IndexOf("Importing", StringComparison.OrdinalIgnoreCase) >= 0) {
        var kids = new List<string>();
        EnumChildWindows(h, (c, x) => { string k = T(c); if (k.Length > 0) kids.Add(k); return true; }, IntPtr.Zero);
        found.Add(t + " [" + string.Join(" | ", kids) + "]");
      }
      return true;
    }, IntPtr.Zero);
    return string.Join(" ;; ", found);
  }
}
'@

$log = "$env:LOCALAPPDATA\Unity\Editor\Editor.log"
$end = (Get-Date).AddMinutes($Minutes)
$lastCpu = $null; $lastRead = $null; $lastCompiler = 0; $lastLen = 0
"time,responding,cpu_s,read_mb,other_mb,main_thread,compiler_cpu_s,log_kb_new,dialog" | Set-Content $Out -Encoding utf8
$stalled = @()

while ((Get-Date) -lt $end) {
    $p = Get-Process -Id $UnityId -ErrorAction SilentlyContinue
    if (-not $p) { "unity closed" | Add-Content $Out; break }

    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$UnityId" -Property ReadTransferCount, OtherTransferCount
    $cpu = $p.TotalProcessorTime.TotalSeconds
    $read = $cim.ReadTransferCount / 1MB
    $other = $cim.OtherTransferCount / 1MB
    $main = $p.Threads | Sort-Object StartTime | Select-Object -First 1
    $state = "$($main.ThreadState)"
    if ($main.ThreadState -eq 'Wait') { $state += ":$($main.WaitReason)" }
    $compiler = (Get-Process UnityShaderCompiler -ErrorAction SilentlyContinue | Measure-Object -Property CPU -Sum).Sum
    $len = (Get-Item $log -ErrorAction SilentlyContinue).Length
    $dialog = [Win]::Busy([uint32]$UnityId)

    if ($lastCpu -ne $null) {
        $row = "{0:HH:mm:ss},{1},{2:0.0},{3:0.0},{4:0.0},{5},{6:0.0},{7:0.0},{8}" -f (Get-Date), $p.Responding, ($cpu - $lastCpu), ($read - $lastRead), ($other - $lastOther), $state, ($compiler - $lastCompiler), (($len - $lastLen) / 1KB), $dialog
        # Quiet moments would swamp the file, so only what looks like work or a stall is kept.
        if (-not $p.Responding -or $dialog -or ($cpu - $lastCpu) -gt 1 -or ($read - $lastRead) -gt 20) { $row | Add-Content $Out }
    }
    $lastCpu = $cpu; $lastRead = $read; $lastOther = $other; $lastCompiler = $compiler; $lastLen = $len
    Start-Sleep -Seconds 2
}
