using System.Diagnostics;

namespace Relauncher;

class Program
{
    static void Main(string[] args)
    {
        Thread.Sleep(1000); // make sure parent process is dead
        
        var parms = RelaunchParameters.Parse(args);
        // string buildProject = args.First(arg => arg.StartsWith("BUILD=")).Replace("BUILD=", ""); // full path
        // string continueWith = args.First(arg => arg.StartsWith("CONT=")).Replace("CONT=", ""); // full path
        // string continueRootFolder = args.First(arg => arg.StartsWith("PWD=")).Replace("PWD=", "");
        // string? initiatorId = args.FirstOrDefault(arg => arg.StartsWith("USERID=")); // optional

        string projFolder = Path.GetDirectoryName(parms.buildProject) ?? Environment.CurrentDirectory;
        string projFile = Path.GetFileName(parms.buildProject);
        string dotnetBuildOutput;

        Process proc = new()
        {
            StartInfo = new()
            {
                FileName = "dotnet",
                Arguments = $"build \"{projFile}\"",
                WorkingDirectory = projFolder,
                RedirectStandardOutput = true,
            }
        };

        string invokedInfo = $"dotnet build \"{projFile}\" [in {projFolder}]";
        
        proc.Start();
        proc.WaitForExit();
        dotnetBuildOutput = proc.StandardOutput.ReadToEnd();

        proc = new()
        {
            StartInfo = new()
            {
                FileName = parms.launchExecutable,
                WorkingDirectory = parms.launchWorkingDir
            }
        };
        proc.StartInfo.ArgumentList.Add(RelaunchParameters.RELAUNCHED_ARG);
        proc.StartInfo.ArgumentList.Add(invokedInfo + " => " + dotnetBuildOutput);
        if (parms.initiatorId.HasValue)
            proc.StartInfo.ArgumentList.Add("USERID=" + parms.initiatorId.Value);

        proc.Start();
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        Console.WriteLine("DONE, NOW LAUNCHING BOT!!!");
        
        Console.WriteLine($"Executing command: {proc.StartInfo.FileName} \"{string.Join("\" \"", proc.StartInfo.ArgumentList)}\"");
        Environment.Exit(0);
    }
}