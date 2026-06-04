using System.Diagnostics;
using System.Text.RegularExpressions;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BoneBoard.Modules.Blockers;

[Command("original")]
public partial class BeOriginal(BoneBot bot) : ModuleBase(bot)
{
    private static readonly Regex Whitespace = WhitespaceRegex();
    private static readonly DiscordEmoji[] NumberEmojis =
    [
        DiscordEmoji.FromUnicode("0️⃣"),
        DiscordEmoji.FromUnicode("1️⃣"),
        DiscordEmoji.FromUnicode("2️⃣"),
        DiscordEmoji.FromUnicode("3️⃣"),
        DiscordEmoji.FromUnicode("4️⃣"),
        DiscordEmoji.FromUnicode("5️⃣"),
        DiscordEmoji.FromUnicode("6️⃣"),
        DiscordEmoji.FromUnicode("7️⃣"),
        DiscordEmoji.FromUnicode("8️⃣"),
        DiscordEmoji.FromUnicode("9️⃣"),
        DiscordEmoji.FromUnicode("🔟"),
    ];

    private readonly Stopwatch sw = new Stopwatch();
    private static TimeSpan elapsedTime = TimeSpan.Zero;
    private static int processedMessages = 0;
    private static int levDistCounts = 0;
    
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return MessageCheck(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return MessageCheck(msgUpdatedArgs.Message);
        }

        return false;
    }
    
    private bool MessageCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;
        
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant
        
        if (!Config.values.channelsWhereMessagesMustBeOriginal.Contains(msg.ChannelId))
            return false;

        if (!PersistentData.values.uniqueChannelsMessages.TryGetValue(msg.ChannelId, out var msgDict))
        {
            msgDict = [];
            PersistentData.values.uniqueChannelsMessages[msg.ChannelId] = msgDict;
        }

        string cleanContent = Quoter.Link.Replace(msg.Content.ToLower(), "<link>");
        // intentional choice to not let links slip through the cracks
        // if (cleanContent == "<link>")
        //     return false;
        
        cleanContent = Quoter.UserMention.Replace(cleanContent, "<mention>");
        cleanContent = Whitespace.Replace(cleanContent, " ");

        int requiredLevDist = Config.values.originalityMinLevDist +
                              (int)(Config.values.originalityLevDistScale * cleanContent.Length);
        int minSeenLevDist = Config.values.isOriginalityInDryRun ? 1024 : (requiredLevDist + 1);
        string minLevDistStr = "<None>";
        int levDistCalcs = 0;
        
        sw.Restart();
        
        foreach (var kvp in msgDict)
        {
            if (msg.Id == kvp.Key)
                continue;
            
            // The lev dist isn't going to be lowered by a string with a difference in length greater
            // than the currently found smallest lev dist
            if (Math.Abs(kvp.Value.Length - cleanContent.Length) > minSeenLevDist)
                continue;
            
            int newLevDist = cleanContent.LevenshteinDistance(kvp.Value);
            levDistCalcs++;
            
            if (newLevDist < minSeenLevDist)
            {
                minSeenLevDist = newLevDist;
                minLevDistStr = kvp.Value;
            }
        }
        
        sw.Stop();
        levDistCounts += levDistCalcs;
        elapsedTime += sw.Elapsed;
        processedMessages++;
        Logger.Put($"Found lev dist {minSeenLevDist} between string [[{cleanContent}]] (message) " +
                   $"and [[{minLevDistStr}]] (historical, of {msgDict.Count} past strings) " +
                   $"in {sw.ElapsedMilliseconds}ms ({levDistCalcs} ld calculations)", LogType.Debug);

        
        msgDict[msg.Id] = cleanContent;
        PersistentData.WritePersistentData();

        if (Config.values.isOriginalityInDryRun)
        {
            // num exists in NumberEmojis array & a comparison was made
            if (minSeenLevDist < NumberEmojis.Length && levDistCounts != 0)
            {
                _ = TryReact(msg, NumberEmojis[minSeenLevDist]);
            }
        }
        
        // There are more differences than the allowed amount
        if (minSeenLevDist < requiredLevDist)
        {
            if (Config.values.isOriginalityInDryRun)
            {
                // down react instead of deleting
                _ = TryReact(msg, DiscordEmoji.FromUnicode("👎"));
            }
            else
            {
                TryDeleteDontCare(msg, $"Lev dist of {minSeenLevDist} is lesser than the allowed {requiredLevDist} " +
                                       $"({Config.values.originalityMinLevDist} min + ({Config.values.originalityLevDistScale} scale * {cleanContent.Length} content len) ).");
            }
            
            return true;
        }
        
        
        return false;
    }

    [GeneratedRegex(@"[\s_-]+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [Command("toggleDryRun")]
    [RequirePermissions([], [DiscordPermission.ManageMessages])]
    public static async Task SetDryRun(SlashCommandContext ctx, bool newValue)
    {
        bool oldValue = Config.values.isOriginalityInDryRun;
        Config.values.isOriginalityInDryRun = newValue;
        await ctx.RespondAsync($"Got it, dry runs are now {(newValue ? "active" : "inactive")} " +
                               $"(formerly {(oldValue ? "active" : "inactive")})", true);
        Config.WriteConfig();
    }
    
    [Command("setLevDist")]
    [RequirePermissions([], [DiscordPermission.ManageMessages])]
    [RequireApplicationOwner]
    public static async Task SetLevDist(SlashCommandContext ctx, int newValue = 4, double scale = 0.2)
    {
        int oldMin = Config.values.originalityMinLevDist;
        double oldScale = Config.values.originalityLevDistScale;
        Config.values.originalityMinLevDist = newValue;
        Config.values.originalityLevDistScale = scale;
        await ctx.RespondAsync($"Got it, messages are now considered reused if a lev dist of {newValue}+(length * {scale}) is found.\n" +
                               $"(read: appx that # of characters must get changed the new msg and an older one)\n" +
                               $"(value was formerly {oldMin}, w scale of {oldScale})", true);
        Config.WriteConfig();
    }
    
    [Command("getTimingInfo")]
    [RequirePermissions([], [DiscordPermission.ManageMessages])]
    public static async Task SetDryRun(SlashCommandContext ctx)
    {
        await ctx.RespondAsync($"Processed {processedMessages} messages over a combined {elapsedTime.TotalSeconds:0.00} seconds.\n" +
                               $"This involved {levDistCounts} ({levDistCounts / 1000.0:0.0}K) levenshtein distance calculations, " +
                               $"representing an average of {processedMessages/(double)levDistCounts} calculations per message.", true);
    }
}