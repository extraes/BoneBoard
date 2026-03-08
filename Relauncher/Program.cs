using System.Diagnostics;

namespace Relauncher;

class Program
{
    static void Main(string[] args)
    {
        Thread.Sleep(1000); // make sure parent process is dead
        
        var parms = RelaunchParameters.Parse(args)!;
        // string buildProject = args.First(arg => arg.StartsWith("BUILD=")).Replace("BUILD=", ""); // full path
        // string continueWith = args.First(arg => arg.StartsWith("CONT=")).Replace("CONT=", ""); // full path
        // string continueRootFolder = args.First(arg => arg.StartsWith("PWD=")).Replace("PWD=", "");
        // string? initiatorId = args.FirstOrDefault(arg => arg.StartsWith("USERID=")); // optional

        string projFolder = Path.GetDirectoryName(parms.buildProject) ?? Environment.CurrentDirectory;
        string projFile = Path.GetFileName(parms.buildProject)!;

        Process cleanProc = new()
        {
            StartInfo = new()
            {
                FileName = "dotnet",
                Arguments = "clean",
                WorkingDirectory = projFolder,
                RedirectStandardOutput = true,
            }
        };
        string cleanInvocation = $"{cleanProc.StartInfo.FileName} {cleanProc.StartInfo.Arguments} [in {projFolder}]";
        cleanProc.Start();
        cleanProc.WaitForExit();
        string[] cleanLines = cleanProc.StandardOutput.ReadToEnd().Replace(projFolder, "$PWD").Split(Environment.NewLine);
        string[] cleanLinesWithoutDeletions =
            cleanLines.Where(str => !str.Contains("Deleting file")).ToArray();
        string deletedFiles = $"*Not shown: {cleanLines.Length - cleanLinesWithoutDeletions.Length} deletions*";
        string cleanOutput = string.Join(Environment.NewLine, cleanLinesWithoutDeletions) + "\n" + deletedFiles;
        
        
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

        string buildInvocation = $"{proc.StartInfo.FileName} {proc.StartInfo.Arguments} [in {projFolder}]";
        
        proc.Start();
        proc.WaitForExit();
        dotnetBuildOutput = proc.StandardOutput.ReadToEnd().Replace(projFolder, "$PWD");

        proc = new()
        {
            StartInfo = new()
            {
                FileName = parms.launchExecutable,
                WorkingDirectory = parms.launchWorkingDir
            }
        };

        string runCommandsParameter = $"### {cleanInvocation}\n{cleanOutput}\n\n### {buildInvocation}\n{dotnetBuildOutput}";
        proc.StartInfo.ArgumentList.Add(RelaunchParameters.RELAUNCHED_ARG);
        proc.StartInfo.ArgumentList.Add(runCommandsParameter);
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