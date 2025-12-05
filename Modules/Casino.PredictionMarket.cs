using System.ComponentModel;
using System.Text;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

namespace BoneBoard.Modules;

internal class PredictionEvent
{
    public const ulong SEED_ID = 0;
    // public const int SEED_AMOUNT = 10_000;
    public const int PER_USER_BONUS = 1_000;
    
    public string title = "";
    public string criteriaDesc = "";
    public DateTime lockAt;
    public DateTime createdAt;
    public Dictionary<ulong, int> pointsFor = [];
    public Dictionary<ulong, int> pointsAgainst = [];

    public void InitPoints(int totalSeedAmount)
    {
        pointsFor = new()
        {
            { SEED_ID, totalSeedAmount / 2 }
        };
        pointsAgainst = new()
        {
            { SEED_ID, totalSeedAmount / 2 }
        };
    }

    public bool? CheckPosition(ulong userId)
    {
        if (pointsFor.ContainsKey(userId))
            return true;
        if (pointsAgainst.ContainsKey(userId))
            return false;

        return null;
    }

    public bool IsLocked() => DateTime.Now > lockAt;

    public void RefundEveryone()
    {
        foreach (var kvp in pointsFor)
        {
            if (kvp.Key == SEED_ID)
                continue;

            PersistentData.values.casinoPoints[kvp.Key] += kvp.Value;
        }
        
        foreach (var kvp in pointsAgainst)
        {
            if (kvp.Key == SEED_ID)
                continue;

            PersistentData.values.casinoPoints[kvp.Key] += kvp.Value;
        }
        // dont bother saving data, this will only be used in scope where it'll need to be saved afterward anyway
    }
}

internal partial class Casino
{
    public enum Resolution
    {
        Refund,
        Happened,
        DidntHappen
    }
    
    private DiscordMessage? predictionBoard;

    private async Task<DiscordMessage?> GetPredictionMessage()
    {
        if (string.IsNullOrWhiteSpace(PersistentData.values.predictionBoardLink))
        {
            predictionBoard = null;
            return predictionBoard;
        }
        
        if (predictionBoard is not null &&
            PersistentData.values.predictionBoardLink.EndsWith(predictionBoard.Id.ToString()))
            return predictionBoard;
            
        var msg = await bot.GetMessageFromLink(PersistentData.values.predictionBoardLink);
        predictionBoard = msg;
        return msg;
    }

    private async Task UpdatePredictionMessage()
    {
        var msg = await GetPredictionMessage();
        if (msg is null)
            return;
        
        var totalSb = new StringBuilder("# Predictions\n");

        const string COMMAND_PROMPT = "-# *Use `/casino predict` to place a bet!*";
        const string AND_MORE = "\n*and more...*";
        
        var sb = new StringBuilder();
        
        foreach (var pEvent in PersistentData.values.predictionEvents.OrderBy(e => e.createdAt))
        {
            sb.AppendLine("__.                                                                                    .__");
            sb.AppendLine($"## {pEvent.title}");
            sb.AppendLine($"*\"{Formatter.Strip(pEvent.criteriaDesc)}\"*");
            sb.AppendLine(pEvent.IsLocked()
                ? "Locked, now we wait..."
                : $"Open, place your bets now! (Locks {Formatter.Timestamp(pEvent.lockAt)})");
            sb.AppendLine("Current market:");
            sb.AppendLine($"- {pEvent.pointsFor.Values.Sum()} points saying that **it *will* come true**");
            sb.AppendLine($"- {pEvent.pointsAgainst.Values.Sum()} saying predicting **it will *not* come true**");

            if (totalSb.Length + sb.Length > 2000)
            {
                if (sb.Length + AND_MORE.Length <= 2000)
                    totalSb.AppendLine(AND_MORE);
                break;
            }

            totalSb.AppendLine(sb.ToString());
            sb.Clear();
        }
        
        
        if (PersistentData.values.predictionEvents.Count == 0)
        {
            totalSb.AppendLine("-# Ain't nobody here but us chickens!");
        }
        else if (totalSb.Length < 2000 - COMMAND_PROMPT.Length)
        {
            totalSb.AppendLine(COMMAND_PROMPT);
        }

        try
        {
            await msg.ModifyAsync(totalSb.ToString());
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while updating prediction market message!", ex);
        }
    }
    
    [Command("createEvent"), Description("Let people bet on something.")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task CreateEvent(SlashCommandContext ctx, string title, string criteria, int totalSeedAmount = 10_000)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;
        
        PredictionEvent newEvent = new()
        {
            title = title,
            criteriaDesc = criteria,
            createdAt = DateTime.Now,
            lockAt = DateTime.Now.AddDays(1)
        };
        newEvent.InitPoints(totalSeedAmount);
        
        PersistentData.values.predictionEvents.Add(newEvent);
        
        await UpdatePredictionMessage();
        await ctx.RespondAsync("Done!\n-# It'll be locked after a day unless you use the unlockFor command!", true);
    }
    
    [Command("modifyEvent"), Description("Modifies an event's title or description.")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task ModifyEvent(SlashCommandContext ctx, string originalTitle, string newTitle = "", string newCriteria = "")
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        var target = PersistentData.values.predictionEvents.MinBy(p => p.title.LevenshteinDistance(originalTitle));
        if (target is null)
        {
            await ctx.RespondAsync($"Couldn't find anything with that title for some reason. Strange.", true);
            return;
        }

        const int LEV_DIST_CUTOFF = 10;

        int levDist = target.title.LevenshteinDistance(originalTitle);
        Logger.Put($"LevDist from input '{originalTitle}' to found '{target.title}' - {levDist}");
        bool levTooFar = levDist > LEV_DIST_CUTOFF;
        bool doesntContainInput = !target.title.Contains(newTitle, StringComparison.InvariantCultureIgnoreCase);
        if (levTooFar && doesntContainInput)
        {
            await ctx.RespondAsync($"Found something named '{target.title}', but something tells me that's not what you're looking for.", true);
            return;
        } 

        string res = $"Found '{target.title}'\n";
        
        if (!string.IsNullOrWhiteSpace(newTitle))
        {
            target.title = newTitle;
            res += $"Renamed to '{newTitle}'\n";
        }
        
        if (!string.IsNullOrWhiteSpace(newCriteria))
        {
            target.criteriaDesc = newCriteria;
            res += $"Changed criteria/description to '{newCriteria}'\n";
        }
        
        PersistentData.WritePersistentData();
        await UpdatePredictionMessage();
        
        res += "Done!";
        await ctx.RespondAsync(res, true);
    }
    
    [Command("resolveEvent"), Description("Resolves an event, paying out its bettors")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task ResolveEvent(SlashCommandContext ctx, string title, Resolution resolution)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        var target = PersistentData.values.predictionEvents.MinBy(p => p.title.LevenshteinDistance(title));
        if (target is null)
        {
            await ctx.RespondAsync($"Couldn't find anything with that title for some reason. Strange.", true);
            return;
        }

        int forValue = target.pointsFor.Values.Sum();
        int forWagered = forValue - target.pointsFor[PredictionEvent.SEED_ID];
        int againstValue = target.pointsAgainst.Values.Sum();
        int againstWagered = againstValue - target.pointsAgainst[PredictionEvent.SEED_ID];
        int totalMarketValue = forValue + againstValue + (target.pointsFor.Count + target.pointsAgainst.Count - 2) * PredictionEvent.PER_USER_BONUS;
        Logger.Put($"Total market value: {totalMarketValue}, total wagered: {forWagered + againstWagered}");
        switch (resolution)
        {
            case Resolution.Happened:
                foreach (var kvp in target.pointsFor)
                {
                    if (kvp.Key == PredictionEvent.SEED_ID)
                        continue;
                    double valueProportion = kvp.Value / (double)forWagered;
                    int award = (int)(valueProportion * totalMarketValue);
                    PersistentData.values.casinoPoints[kvp.Key] += award;
                    Logger.Put($"User {kvp.Key} won {award} on a bet of {kvp.Value} ({award - kvp.Value} profit)");
                }
                break;
            case Resolution.DidntHappen:
                foreach (var kvp in target.pointsAgainst)
                {
                    if (kvp.Key == PredictionEvent.SEED_ID)
                        continue;
                    double valueProportion = kvp.Value / (double)againstWagered;
                    int award = (int)(valueProportion * totalMarketValue);
                    PersistentData.values.casinoPoints[kvp.Key] += award;
                    Logger.Put($"User {kvp.Key} won {award} on a bet of {kvp.Value} ({award - kvp.Value} profit)");
                }
                break;
            default:
            case Resolution.Refund:
                target.RefundEveryone();
                break;
        }

        PersistentData.values.predictionEvents.Remove(target);
        PersistentData.WritePersistentData();
        await UpdatePredictionMessage();

        await ctx.RespondAsync($"Resolved the event!", true);
    }


    [Command("unlockFor"), Description("Unlocks an event for a given amount of time, allowing betting.")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task UnlockEvent(SlashCommandContext ctx, string title, double openForHours)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        if (openForHours <= 0)
        {
            await ctx.RespondAsync($"It has to be open for *some* period of time, be serious now.", true);
            return;
        }

        var target = PersistentData.values.predictionEvents.MinBy(p => p.title.LevenshteinDistance(title));
        if (target is null)
        {
            await ctx.RespondAsync($"Couldn't find anything with that title for some reason. Strange.", true);
            return;
        }

        target.lockAt = DateTime.Now.AddHours(openForHours);
        
        PersistentData.WritePersistentData();
        await UpdatePredictionMessage();

        await ctx.RespondAsync($"Done! That event's betting will now close {Formatter.Timestamp(target.lockAt)}", true);
    }


    [Command("predict")]
    [RequireGuild]
    public async Task BetOnEvent(SlashCommandContext ctx, string title, int stake, bool willItHappen, bool overridePrevBets = false)
    {
        var target = PersistentData.values.predictionEvents.MinBy(p => p.title.LevenshteinDistance(title));
        if (target is null)
        {
            await ctx.RespondAsync($"Couldn't find anything with that title for some reason. Strange.", true);
            return;
        }

        if (target.IsLocked())
        {
            await ctx.RespondAsync($"You can't bet on that, it's closed.", true);
            return;
        }
        
        if (stake <= 500)
        {
            await ctx.RespondAsync($"you gotta put more than 500 on the line, be serious now.", true);
            return;
        }

        var balance = PersistentData.values.casinoPoints.GetValueOrDefault(ctx.User.Id);
        if (stake > balance)
        {
            await ctx.RespondAsync($"you don't have that scratch buddy, you got {balance} points.", true);
            return;
        }
        
        bool? currPosition = target.CheckPosition(ctx.User.Id);
        int currPosAmt = currPosition.HasValue
            ? (currPosition.Value
                ? target.pointsFor
                : target.pointsAgainst
            )[ctx.User.Id]
            : 0;
        
        bool bettingAgainstSelf = currPosition.HasValue && currPosition.Value != willItHappen;
        bool needToScavengePrevBets = bettingAgainstSelf && overridePrevBets;
        if (needToScavengePrevBets)
        {
            int scavenged = 0;
            target.pointsAgainst.Remove(ctx.User.Id, out int val);
            scavenged += val;
            
            target.pointsFor.Remove(ctx.User.Id, out val);
            scavenged += val;

            balance += scavenged;
        }
        else if (bettingAgainstSelf)
        {
            await ctx.RespondAsync($"You currently have a bet for the opposite lol", true);
            return;
        }

        currPosAmt += stake;
        (willItHappen
                ? target.pointsFor
                : target.pointsAgainst
            )[ctx.User.Id] = currPosAmt;

        balance -= stake;
        PersistentData.values.casinoPoints[ctx.User.Id] = balance;
        
        PersistentData.WritePersistentData();
        await UpdatePredictionMessage();

        await ctx.RespondAsync($"Done! You now have a {currPosAmt} stake on '{target.title}' {(willItHappen ? "happening" : "not happening")}", true);
    }


    [Command("postPredictionBoard"), Description("Creates a new message for the prediction board in this channel")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task UnlockEvent(SlashCommandContext ctx)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        predictionBoard = await ctx.Channel.SendMessageAsync("hi guys my names travis im 33 and i think nintendo is pretty cool");
        PersistentData.values.predictionBoardLink = predictionBoard.JumpLink.ToString();
        PersistentData.WritePersistentData();
        await UpdatePredictionMessage();

        await ctx.RespondAsync("Done!", true);
    }
}