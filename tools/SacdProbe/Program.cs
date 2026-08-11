using Microsoft.Win32;

namespace SacdProbe;

internal static class Program
{
    public static int Main()
    {
        Console.WriteLine("=== SACD PROBE v2 — real Karajan DFF ===");
        Console.WriteLine($"Time       : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        if (OperatingSystem.IsWindows())
            Console.WriteLine($"Identity   : {System.Security.Principal.WindowsIdentity.GetCurrent().Name}");
        Console.WriteLine($"Session ID : {System.Diagnostics.Process.GetCurrentProcess().SessionId}");
        Console.WriteLine($"ACP        : {GetAcp()} {(GetAcp() == 65001 ? "(UTF-8 beta ON — charset error expected on visible arm)" : "(UTF-8 beta OFF — no charset error expected)")}");
        Console.WriteLine();

        return ProbeRunner.RunAll();
    }

    private static int GetAcp()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Nls\CodePage",
            "ACP", "0") is string s && int.TryParse(s, out var v) ? v : 0;
    }
}