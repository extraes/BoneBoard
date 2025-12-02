using System.Diagnostics;

namespace Skeleton;

public static class Relauncher
{
    
    
    public const string ROOT_FOLDER_PREFIX = "RF=";
    public const string ENTRY_PREFIX = "CW="; // ContinueWith, not ContentWarning, lol
    public const string CSPROJ_PREFIX = "CS=";
    public const string DEBUGGING_MARKER = "DEBUGGING";
    
    public static void Main(string[] args)
    {
        if (args.Contains(DEBUGGING_MARKER)) Debugger.Launch();

        // wait for main proc to finish closing to release locks
        Thread.Sleep(1000); // ideally id await but apparently main cant be async.

        // dont even bother try-catching. if this fails we're basically fucked (and shouldn't have gotten this far in the first place)
        string rootFolder = args.First(arg => arg.StartsWith(ROOT_FOLDER_PREFIX))[ROOT_FOLDER_PREFIX.Length..];
        string entry = args.First(arg => arg.StartsWith(ENTRY_PREFIX))[ENTRY_PREFIX.Length..];
        string csproj = args.First(arg => arg.StartsWith(CSPROJ_PREFIX))[CSPROJ_PREFIX.Length..];

        Process proc = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{csproj}\"",
                WorkingDirectory = rootFolder,
                RedirectStandardOutput = true,
            }
        };

        proc.Start();
        proc.WaitForExit();
        string dotnetBuildOutput = proc.StandardOutput.ReadToEnd();

        proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                    FileName = entry,
                    RedirectStandardOutput = false,
                    WorkingDirectory = Path.GetDirectoryName(entry),
                    WindowStyle = ProcessWindowStyle.Normal,
                    CreateNoWindow = false,
                }
        };
        
        proc.StartInfo.ArgumentList.Add("UPDATED");
        proc.StartInfo.ArgumentList.Add(dotnetBuildOutput);
        proc.StartInfo.ArgumentList.Add(args.First(arg => ulong.TryParse(arg, out _)));
        if (args.Contains(DEBUGGING_MARKER)) proc.StartInfo.ArgumentList.Add(DEBUGGING_MARKER);
        proc.Start();
        Environment.Exit(0);
    }
}