using System.Text;

// ReSharper disable once CheckNamespace
public record class RelaunchParameters
{
    internal const string RELAUNCHED_ARG = "RELAUNCHED";

    public string? buildProject;
    public string? launchExecutable;
    public string? launchWorkingDir;
    public ulong? initiatorId = null;

    public List<string> BuildLaunchParameters()
    {
        // BUILD= CONT= PWD= USERID=
        List<string> ret =
        [
            "BUILD=" + buildProject,
            "CONT=" + launchExecutable,
            "PWD=" + launchWorkingDir,
        ];
        if (initiatorId.HasValue)
            ret.Add("USERID=" + initiatorId.Value);
        
        return ret;
    }

    public static RelaunchParameters? Parse(string[] args)
    {
        string buildProject = args.First(arg => arg.StartsWith("BUILD=")).Replace("BUILD=", ""); // full path
        string continueWith = args.First(arg => arg.StartsWith("CONT=")).Replace("CONT=", ""); // full path
        string continueRootFolder = args.First(arg => arg.StartsWith("PWD=")).Replace("PWD=", "");
        string? initiatorId = args.FirstOrDefault(arg => arg.StartsWith("USERID="))?.Replace("USERID=", ""); // optional

        ulong.TryParse(initiatorId ?? "", out var id);
        Console.WriteLine($"Initiator ID: {initiatorId}");
        
        RelaunchParameters ret = new()
        {
            buildProject = buildProject,
            launchExecutable = continueWith,
            launchWorkingDir = continueRootFolder,
            initiatorId = id == default ? null : id,
        };

        return ret;
    }

    public static ulong? GetInitiatorId(string[] args)
    {
        if (!args.Contains(RELAUNCHED_ARG))
            return null;
        
        string? initiatorId = args.FirstOrDefault(arg => arg.StartsWith("USERID="))?.Replace("USERID=", ""); // optional

        ulong.TryParse(initiatorId ?? "", out var id);
        Console.WriteLine($"Initiator ID: {initiatorId}");

        return id == default ? null : id;
    }
    
#if !RELAUNCHER
    private static bool sentMessage = false;
    public static void SetupProcessStartMessage(string[] args, DSharpPlus.DiscordClientBuilder clientBuilder)
    {
        Logger.Put("Launched with arguments...");
        foreach (string arg in args)
        {
            Logger.Put(" - " + arg);
        }
        Logger.Put($"From working dir: {Environment.CurrentDirectory}");
        if (!args.Contains(RELAUNCHED_ARG))
            return;
        
        
        string? initiatorId = args.FirstOrDefault(arg => arg.StartsWith("USERID="))?.Replace("USERID=", ""); // optional

        ulong.TryParse(initiatorId ?? "", out var id);
        if (id == default)
            return;

        string buildOutput = args.First(arg => arg.StartsWith("dotnet"));
        clientBuilder.ConfigureEventHandlers(x =>
            x.HandleSessionCreated((clint, sArgs) => RelaunchThunk(buildOutput, id, clint, sArgs)));
    }

    private static async Task RelaunchThunk(string buildOutput, ulong initiatorId,
        DSharpPlus.DiscordClient client, DSharpPlus.EventArgs.SessionCreatedEventArgs args)
    {
        if (sentMessage)
            return;
        try
        {
            var initiator = await client.GetUserAsync(initiatorId);
            var channel = await initiator.CreateDmChannelAsync();

            StringBuilder sb = new();
            sb.AppendLine($"## Restarted successfully!");
            sb.AppendLine(buildOutput);
            
            await channel.SendMessageAsync(Logger.EnsureShorterThan(sb.ToString(), 2000));
            
            Logger.Put($"Successfully DMed initiator ({initiatorId}) to tell them dotnet build output!");
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to DM user after relaunching! Initiator ID: {initiatorId}", e);
        }

        sentMessage = true;
    }
#endif
}