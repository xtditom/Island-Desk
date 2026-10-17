using System.Diagnostics;
using System.IO;
using System.Reflection;
using IslandDesk.Infrastructure;
using IslandDesk.Settings;

internal static class DeploymentValidation
{
    internal static int Run(string projectRoot)
    {
        var checks = 0;
        void Check(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
            Console.WriteLine("PASS " + description);
            checks++;
        }
        try
        {
            var name = @"Local\IslandDesk.DeploymentTest." + Guid.NewGuid();
            using (var first = SingleInstanceGuard.TryAcquire(name))
            {
                Check(first is not null, "First instance acquires a unique guard");
                using var visible = Mutex.OpenExisting(name);
                Check(visible is not null, "Guard is visible to an external mutex lookup");
                using var duplicate = SingleInstanceGuard.TryAcquire(name);
                Check(duplicate is null, "Duplicate acquisition is rejected, including on the same thread");
                Check(RunChild(name) == 17, "Second process is rejected while the first is alive");
            }
            Check(RunChild(name) == 0, "A new process can start after the first guard is disposed");

            var publish = Path.Combine(projectRoot, "artifacts", "publish", "win-x64");
            var executable = Path.Combine(publish, "IslandDesk.exe");
            var installer = Path.Combine(projectRoot, "dist", "IslandDesk-Setup.exe");
            var runtimeConfig = File.ReadAllText(Path.Combine(publish, "IslandDesk.runtimeconfig.json"));
            Check(runtimeConfig.Contains("includedFrameworks") && !runtimeConfig.Contains("\"frameworks\":"),
                "Published app declares bundled runtime frameworks");
            foreach (var file in new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "PresentationFramework.dll", "System.Private.CoreLib.dll" })
                Check(File.Exists(Path.Combine(publish, file)), "Bundled runtime file: " + file);
            Check(IsX64(executable) && IsX64(installer), "Application and installer PE files are Windows x64");
            Check(FileVersionInfo.GetVersionInfo(executable).ProductName == "IslandDesk", "Published executable has IslandDesk product metadata");
            Check(new FileInfo(installer).Length > 1_000_000, "Compiled installer payload exists and is nonempty");
            Check(StartupRegistration.SelectExecutable(@"C:\development\IslandDesk.exe", executable) == executable,
                "Startup prefers an existing installed executable over a development path");
            Check(StartupRegistration.SelectExecutable(@"C:\development\IslandDesk.exe", @"C:\missing\IslandDesk.exe") == @"C:\development\IslandDesk.exe",
                "Stale installation metadata safely falls back to the current executable");
            Check(StartupRegistration.SelectExecutable(@"C:\development\IslandDesk.exe", "relative\\IslandDesk.exe") == @"C:\development\IslandDesk.exe",
                "Relative deployment paths are rejected");
            Check(StartupRegistration.BuildCommand(executable, "unused.dll") == "\"" + executable + "\"",
                "Installed startup command quotes the executable without a development DLL");

            using var appGuard = SingleInstanceGuard.TryAcquire();
            if (appGuard is null) throw new InvalidOperationException("Exit the running IslandDesk copy before testing duplicate launch.");
            var trace = Path.Combine(projectRoot, "artifacts", "deployment-host-trace.txt");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = publish };
            start.Environment["DOTNET_ROOT"] = Path.Combine(projectRoot, "artifacts", "no-global-runtime");
            start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
            start.Environment["DOTNET_HOST_TRACE"] = "1";
            start.Environment["DOTNET_HOST_TRACEFILE"] = trace;
            using (var duplicateApp = Process.Start(start)!)
            {
                if (!duplicateApp.WaitForExit(10000))
                {
                    duplicateApp.Kill();
                    throw new InvalidOperationException("Published duplicate did not exit within ten seconds.");
                }
                Check(duplicateApp.ExitCode == 0, "Actual published app exits cleanly when its instance guard is held");
            }
            var hostTrace = File.ReadAllText(trace);
            Check(hostTrace.Contains(Path.Combine(publish, "hostfxr.dll"), StringComparison.OrdinalIgnoreCase) &&
                  hostTrace.Contains(Path.Combine(publish, "coreclr.dll"), StringComparison.OrdinalIgnoreCase),
                "Actual launch resolves the bundled hostfxr and CoreCLR despite invalid global runtime paths");
            Console.WriteLine($"{checks} deployment checks passed. Installer was NOT executed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int RunChild(string name)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--deployment-child");
        start.ArgumentList.Add(name);
        using var child = Process.Start(start)!;
        if (!child.WaitForExit(10000)) { child.Kill(); throw new TimeoutException("Deployment test child did not exit."); }
        return child.ExitCode;
    }

    private static bool IsX64(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt16() != 0x5A4D) return false;
        reader.BaseStream.Position = 0x3C;
        var header = reader.ReadInt32();
        reader.BaseStream.Position = header;
        return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
    }
}
