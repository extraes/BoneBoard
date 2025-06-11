using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("frog")]
internal class FrogRole : ModuleBase
{
    private DiscordRole? frogRole;
    private DiscordMessage? frogMsg;
    private DiscordMessage? frogLeaderboardMsg;
    private DiscordMember? frogKing;
    private volatile bool assigningNewKing;

    private Task? leaderboardTask;

    public FrogRole(BoneBot bot) : base(bot)
    { }

    protected override async Task FetchGuildResources()
    {
        await FetchFrogMessage();
        await FetchFrogLeaderboardMsg();

        foreach (DiscordGuild guild in bot.client.Guilds.Values)
        {
            if (guild.Roles.TryGetValue(Config.values.frogRole, out frogRole))
            {
                try
                {
                    if (PersistentData.values.lastFrogKing != default)
                        frogKing = await guild.GetMemberAsync(PersistentData.values.lastFrogKing);
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception while trying to retrieve frog king from guild that somehow has a role with the frog role's ID!" + ex);
                }
                break;
            }
        }
    }

    protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        leaderboardTask = UpdateLeaderboard();
    }

    async Task FetchFrogMessage()
    {
        if (!Config.values.frogMessageLink.Contains("/channels/"))
            return;

        ulong? frogMsgChannel = null;
        ulong? frogMsgId = null;

        string[] idStrings = Config.values.frogMessageLink.Split("/channels/");
        ulong[] ids = idStrings[1].Split('/').Skip(1).Select(ulong.Parse).ToArray();
        if (ids.Length >= 2)
        {
            frogMsgChannel = ids[0];
            frogMsgId = ids[1];
        }

        if (!frogMsgId.HasValue || !frogMsgChannel.HasValue)
            return;

        foreach (DiscordGuild guild in bot.client.Guilds.Values)
        {
            if (guild.Channels.TryGetValue(frogMsgChannel.Value, out DiscordChannel? dCh))
            {
                frogMsg = await TryFetchMessage(dCh, frogMsgId.Value);
                return;
            }

            foreach (DiscordThreadChannel? thread in guild.Channels.SelectMany(chp => chp.Value.Type == DiscordChannelType.Text ? chp.Value.Threads : Enumerable.Empty<DiscordThreadChannel>()))
            {
                if (thread.Id == frogMsgChannel.Value)
                {
                    frogMsg = await TryFetchMessage(thread, frogMsgId.Value);
                    return;
                }
            }
        }

        Logger.Error("Failed to fetch message link @ " + Config.values.frogMessageLink);
    }

    public async Task FetchFrogLeaderboardMsg()
    {
        if (!Config.values.frogLeaderboardLink.Contains("/channels/"))
            return;

        ulong? frogMsgChannel = null;
        ulong? frogMsgId = null;

        string[] idStrings = Config.values.frogLeaderboardLink.Split("/channels/");
        ulong[] ids = idStrings[1].Split('/').Skip(1).Select(ulong.Parse).ToArray();
        if (ids.Length >= 2)
        {
            frogMsgChannel = ids[0];
            frogMsgId = ids[1];
        }

        if (!frogMsgId.HasValue || !frogMsgChannel.HasValue)
            return;

        foreach (DiscordGuild guild in bot.client.Guilds.Values)
        {
            if (guild.Channels.TryGetValue(frogMsgChannel.Value, out DiscordChannel? dCh))
            {
                frogLeaderboardMsg = await TryFetchMessage(dCh, frogMsgId.Value);
                return;
            }

            foreach (DiscordThreadChannel? thread in guild.Channels.SelectMany(chp => chp.Value.Type == DiscordChannelType.Text ? chp.Value.Threads : Enumerable.Empty<DiscordThreadChannel>()))
            {
                if (thread.Id == frogMsgChannel.Value)
                {
                    frogLeaderboardMsg = await TryFetchMessage(thread, frogMsgId.Value);
                    return;
                }
            }
        }

        Logger.Error("Failed to fetch message link @ " + Config.values.frogMessageLink);
    }

    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (Config.values.frogRoleActivation != FrogRoleActivation.REPLY)
            return;
        if (frogMsg is null && !string.IsNullOrEmpty(Config.values.frogMessageLink))
        {
            Logger.Warn("Frog message not found!");
            return;
        }

        if (frogMsg is null || args.Message.ReferencedMessage is null || args.Message.ReferencedMessage != frogMsg || args.Message.Author is null)
            return;

        await AssignNewFrogKing(client, args.Guild, args.Message.Author);
    }

    protected override async Task ReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args)
    {
        if (Config.values.frogRoleActivation != FrogRoleActivation.REACTION)
            return;

        if (frogMsg is null && !string.IsNullOrEmpty(Config.values.frogMessageLink))
        {
            Logger.Warn("Frog message not found!");
            return;
        }

        if (frogMsg is null || args.Message != frogMsg)
            return;

        if (args.Emoji.Name != "🐸")
        {
            Logger.Put($"Removing non-frog reaction (of {args.Emoji.Name}) by {args.User.GlobalName} ({args.User.Username}) from frog message.");
            await args.Message.DeleteReactionAsync(args.Emoji, args.User, "Not a frog!!!!");
            return;
        }

        if (assigningNewKing)
        {
            Logger.Put($"To prevent race condition, removing frog reaction by {args.User.GlobalName} ({args.User.Username}) from frog message.");
            await args.Message.DeleteReactionAsync(args.Emoji, args.User, "Top 10 solutions to race conditions - Number 1: Deleting the problem and pretending it doesn't exist");
            return;
        }

        assigningNewKing = true;

        await foreach (DiscordUser user in args.Message.GetReactionsAsync(args.Emoji))
        {
            if (user == args.User || user == bot.client.CurrentUser)
                continue;


            Logger.Put($"Removing frog reaction by {user.GlobalName} ({user.Username}) from frog message.", LogType.Debug);
            await args.Message.DeleteReactionAsync(args.Emoji, user, "The king is dead, long live the king!");
        }

        await AssignNewFrogKing(client, args.Guild, args.User);

        assigningNewKing = false;
    }

    private async Task AssignNewFrogKing(DiscordClient client, DiscordGuild guild, DiscordUser newKing)
    {
        if (Config.values.frogRoleLimitations.HasFlag(FrogRoleLimitation.DAY_OF_WEEK) && DateTime.Now.DayOfWeek != Config.values.frogRoleAvailableOn)
        {
            Logger.Put($"Ignoring frog role switch attempt - wrong day ({DateTime.Now.DayOfWeek} doesn't match configured {Config.values.frogRoleAvailableOn})", LogType.Debug);

            string str;
            if (frogKing is not null)
                str = string.Format(Config.values.frogMessageClosedBase, frogKing.DisplayName, Config.values.frogRoleAvailableOn);
            else
                str = string.Format(Config.values.frogMessageClosedBase, "somebody/nobody (lol)", Config.values.frogRoleAvailableOn);

            if (frogMsg is not null && frogMsg.Channel is not null && frogMsg.Content != str)
            {
                await frogMsg.ModifyAsync(str);
                frogMsg = await TryFetchMessage(frogMsg.Channel, frogMsg.Id, true);
            }

            return;
        }

        if (frogKing is not null && frogKing.Id == newKing.Id)
        {
            Logger.Put("The current frog king " + frogKing + " re-reacted to the frog message... lol?", LogType.Debug);
            return;
        }

        if (frogRole is null && Config.values.frogRole != default)
        {
            Logger.Warn("Frog role not found @ ID " + Config.values.frogRole + " !!!");
            return;
        }

        if (frogRole is null)
            return;

        try
        {
            Logger.Put($"Assigning new frog king to {newKing}", LogType.Debug);


            if (newKing is not DiscordMember newFrogKing)
                newFrogKing = await guild.GetMemberAsync(newKing.Id);

            if (!frogKing?.Roles.Any(r => r.Id == Config.values.frogRole) ?? false)
            {
                Logger.Put("Updating frogking in cache - D#+ didn't give (our clientside copy of) them the role!", LogType.Debug);
                try
                {
                    frogKing = await guild.GetMemberAsync(frogKing!.Id, true);
                }
                catch (NotFoundException)
                {
                    Logger.Put("Well that's because they're not here anymore... what a bitch. Clearing frogking.");
                    frogKing = null;
                }
            }

            //guild.RequestMembersAsync

            if (frogKing is null || !frogKing.Roles.Any(r => r.Id == Config.values.frogRole))
            {
                Logger.Warn($"Fetching all members of {guild.Name} because {(frogKing is null ? "there's no saved frog king" : $"the frog king '{frogKing.DisplayName}' didn't have the frog role")}");

                await foreach (DiscordMember memberson in guild.GetAllMembersAsync())
                {
                    if (!memberson.Roles.Any(r => r.Id == Config.values.frogRole))
                        continue;

                    Logger.Put($" - {memberson.DisplayName} has the frog role, removing it.", LogType.Debug);
                    await memberson.RevokeRoleAsync(frogRole, "Resetting frog roles to assign new 'frog king'");
                }
            }
            else await frogKing.RevokeRoleAsync(frogRole, "Revoking role to assign new 'frog king'");

            Logger.Put("New frog king: " + newFrogKing);
            await newFrogKing.GrantRoleAsync(frogRole, "New king!");

            // frog king may not yet be assigned
            if (frogKing is not null)
            {
                if (PersistentData.values.frogRoleTimes.TryGetValue(frogKing.Id, out TimeSpan ts))
                    ts += DateTime.Now - PersistentData.values.lastSwitchTime;
                else ts = DateTime.Now - PersistentData.values.lastSwitchTime;
                PersistentData.values.frogRoleTimes[frogKing.Id] = ts;
            }

            PersistentData.values.lastSwitchTime = DateTime.Now;

            frogKing = newFrogKing;
            PersistentData.values.lastFrogKing = frogKing.Id;
            PersistentData.WritePersistentData();

            // id need to implement a queue system, which adds a lotta comlpexity lel
            // (DateTime.Now - (frogMsg.EditedTimestamp ?? frogMsg.Timestamp)).TotalSeconds > Config.values.editTimeoutSec
            if (Config.values.frogMessageBase.Contains("{0}") && frogMsg is not null)
                await frogMsg.ModifyAsync(string.Format(Config.values.frogMessageBase, newFrogKing.DisplayName));
            // hopefully D#+ got me
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while changing frog user! " + ex);
        }
    }

    [DebuggerStepThrough]
    async Task UpdateLeaderboard()
    {
        List<ulong> leftUsers = new(2);
        while (true)
        {
            try
            {
                TimeSpan wait = TimeSpan.FromMinutes(Config.values.leaderboardUpdatePeriodMin);
                await Task.Delay(wait);

                if (frogMsg is null || frogLeaderboardMsg is null || frogRole is null)
                    continue;

                int baseLen = Config.values.frogLeaderboardBase.Length;
                StringBuilder sb = new();
                sb.AppendLine();


                foreach (KeyValuePair<ulong, TimeSpan> kvp in PersistentData.values.frogRoleTimes.OrderByDescending(kvp => kvp.Value))
                {
                    DiscordMember? memberson = null;
                    if (leftUsers.Contains(kvp.Key))
                        continue;
                    try
                    {
                        memberson = await frogMsg.Channel!.Guild.GetMemberAsync(kvp.Key, false);
                    }
                    catch (NotFoundException)
                    {
                        leftUsers.Add(kvp.Key);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Exception while trying to fetch user (ID {kvp.Key}) for leaderboard: " + ex);
                        continue;
                    }

                    TimeSpan span = kvp.Value;
                    if (frogKing is not null && memberson == frogKing)
                        span += DateTime.Now - PersistentData.values.lastSwitchTime;

                    int days = (int)span.TotalDays % 7;
                    int weeks = (int)span.TotalDays / 7;
                    string disName = memberson.DisplayName.Contains("mod.io") ? "fordkiller's apprentice" : memberson.DisplayName;
                    string newLine = $"**{disName}** - {(weeks == 0 ? "" : weeks + "wk, ")}{(days == 0 ? "" : days + "d, ")}{span.Hours}h {span.Minutes}m{span.Seconds}s";

                    if (sb.Length + baseLen + newLine.Length + 1 < 2000)
                        sb.AppendLine(newLine);
                    else
                    {
                        Logger.Put($"Cutting leaderboard short, {newLine.Length}-long line for {memberson.DisplayName} ({memberson.Username}) would have sent the message over 2000 chars to {sb.Length + baseLen + newLine.Length + 1} chars", LogType.Debug);
                        break;
                    }
                }

                //sb.AppendLine();

                string leaderboardTxt = string.Format(Config.values.frogLeaderboardBase, sb.ToString());

                Logger.Put($"Updating leaderboard message to a {leaderboardTxt.Length}-long string", LogType.Debug);
                if (leftUsers.Count > 0)
                    Logger.Put($"Omitted {leftUsers.Count} user(s) from the leaderboard because they left/weren't found: {string.Join(", ", leftUsers)}");

                await frogLeaderboardMsg.ModifyAsync(leaderboardTxt);
                PersistentData.WritePersistentData();
            }
            catch (Exception ex)
            {
                Logger.Error("Exception while updating leaderboard! " + ex);
            }
        }
    }

    [Command("createFrogMsg")]
    [Description("Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [RequireGuild]
    [RequirePermissions([DiscordPermission.AddReactions, DiscordPermission.ManageMessages], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task CreateFrogMessage(
        SlashCommandContext ctx,
        [Parameter("msgContent")]
        [Description("The text content of the message. Use {0} to for curr king.")]
        string content
        )
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "(there'll be a frog haver here)"));

        if (Config.values.frogRoleActivation == FrogRoleActivation.REACTION)
            await msg.CreateReactionAsync(DiscordEmoji.FromUnicode("🐸"));

        Config.values.frogMessageLink = msg.JumpLink.OriginalString;
        Config.values.frogMessageBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].frogRole.FetchFrogMessage();

        await ctx.RespondAsync("Created KOTF message!", true);
    }

    [Command("setFrogUnavailableText")]
    [Description("Set the text that will be displayed when someone tries to get the frog role when it's not available.")]
    [RequireGuild]
    [RequirePermissions([DiscordPermission.AddReactions, DiscordPermission.ManageMessages], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task SetFrogUnavailableText(
        SlashCommandContext ctx,
        [Parameter("msgContent")]
        [Description("The text content of the message. Use {0} to for curr king, and {1} available day of week.")]
        string content
        )
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        Config.values.frogMessageClosedBase = content;
        Config.WriteConfig();

        await ctx.RespondAsync("Set frog unavailable text!", true);
    }

    [Command("createFrogLeaderboardMsg")]
    [Description("Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [RequireGuild]
    [RequirePermissions([DiscordPermission.AddReactions, DiscordPermission.ManageMessages], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task CreateFrogLeaderboardMsg(
        SlashCommandContext ctx,
        [Parameter("msgContent")]
        [Description("Use {0} to indicate where the leaderboard goes (Will be surrounded by multilines)")]
        string content
       )
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "\n(There'll be a leaderboard here...)\n"));

        Config.values.frogLeaderboardLink = msg.JumpLink.OriginalString;
        Config.values.frogLeaderboardBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].frogRole.FetchFrogLeaderboardMsg();

        await ctx.RespondAsync("Created KOTF leaderboard message! You may need to delete the old one!", true);
    }


    [Command("removeUserFromLeaderboard")]
    [Description("Remove user from frog role leaderboard. De-role/block/ban them before use.")]
    [RequireGuild]
    [RequirePermissions([DiscordPermission.AddReactions, DiscordPermission.ManageMessages], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task RemoveUserFromLeaderboard(
        SlashCommandContext ctx,
        [Parameter("user")]
        [Description("Whose ID to remove from the leaderboard. Won't trigger leaderboard refresh.")]
        DiscordUser? user = null,
        [Parameter("userId")]
        [Description("The ID to remove from the leaderboard. Won't trigger leaderboard refresh.")]
        long? userId = null
       )
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        ulong? id = (ulong?)userId ?? user?.Id;
        if (!id.HasValue)
        {
            await ctx.RespondAsync("care to elaborate on whos gettin the ax?", true);
            return;
        }

        bool wasInData = PersistentData.values.frogRoleTimes.Remove(id.Value);
        PersistentData.WritePersistentData();

        await ctx.RespondAsync(wasInData ? "Removed that user from the frog leaderboard." : "That user wasn't in the leaderboard (yet?).", true);
    }
}