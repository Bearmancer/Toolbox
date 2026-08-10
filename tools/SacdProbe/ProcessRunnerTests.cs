using Services.Audio;
namespace SacdProbe;
internal static class ProcessRunnerTests
{
    public static async Task Run()
    {
        var runner = new ProcessRunner();
        var lines = new List<string>();
        // Test 1: Streaming
        await runner.RunAsync("cmd.exe", ["/c", "echo testline"], CancellationToken.None, onOutputLine: line => lines.Add(line));
        if (lines.Count == 0 || !lines[0].Contains("testline")) throw new Exception("FAIL: Streaming did not work");
        
        // Test 2: Timeout
        var start = DateTime.UtcNow;
        var result = await runner.RunAsync("powershell.exe", ["-c", "Start-Sleep -Seconds 10"], CancellationToken.None, inactivityTimeout: TimeSpan.FromSeconds(2));
        if (!result.IsError || !result.FirstError.Description.Contains("Timed out")) throw new Exception("FAIL: Timeout did not trigger");
        if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5)) throw new Exception("FAIL: Timeout took too long");
        
        Console.WriteLine("PASS: ProcessRunner tests");
    }
}
