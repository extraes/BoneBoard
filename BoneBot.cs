using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.SlashCommands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BoneBoard;

internal partial class BoneBot
{
    public static Dictionary<DiscordClient, BoneBot> Bots { get; } = new();

    DiscordClient client;
    DiscordUser User => client.CurrentUser;

    // boneboard
    DiscordChannel? quoteOutputChannel;

    // frog role
    DiscordRole? frogRole;
    DiscordMessage? frogMsg;
    DiscordMessage? frogLeaderboardMsg;
    DiscordMember? frogKing;
    volatile bool assigningNewKing;

    // activity agnostic
    DiscordChannel? logChannel;

    public BoneBot(string token)
    {
        DiscordConfiguration cfg = new()
        {
            Token = token,
            Intents = DiscordIntents.GuildMessages | DiscordIntents.MessageContents | DiscordIntents.GuildMessageReactions | DiscordIntents.Guilds | DiscordIntents.GuildMembers,
        };
        client = new(cfg);

        var slashExtension = client.UseSlashCommands();
        slashExtension.RegisterCommands<ContextActions>();
        slashExtension.RegisterCommands<SlashCommands>();
        
        client.GuildDownloadCompleted += GetGuildResources;
        client.SessionCreated += Ready;
        client.MessageReactionAdded += ReactionAdded;
        client.MessageCreated += MessageCreated;
        Bots.Add(client, this);
    }

    public async void Init()
    {
        await client.ConnectAsync();

        try
        {
            UpdateLeaderboard();
        }
        catch(Exception ex)
        {
            Logger.Error("Exception in periodic-runner! " + ex);
        }
    }

    Task GetGuildResources(DiscordClient client, GuildDownloadCompletedEventArgs args)
    {
        foreach (var channelKvp in args.Guilds.Values.SelectMany(dg => dg.Channels))
        {

            if (channelKvp.Key == Config.values.outputChannel)
                quoteOutputChannel = channelKvp.Value;
            else if (channelKvp.Key == Config.values.logChannel)
                logChannel = channelKvp.Value;
            else
            {
                if (channelKvp.Value.Type != ChannelType.Text) continue;

                foreach (DiscordThreadChannel thread in channelKvp.Value.Threads)
                {
                    if (thread.Id == Config.values.outputChannel)
                        quoteOutputChannel = thread;
                    else if (thread.Id == Config.values.logChannel)
                        logChannel = thread;
                }
            }
        }

        foreach (DiscordGuild guild in args.Guilds.Values)
        {
            if (guild.Roles.TryGetValue(Config.values.frogRole, out frogRole))
            {
                break;
            }
        }

        _ = FetchFrogMessage();
        _ = FetchFrogLeaderboardMsg();

        return Task.CompletedTask;
    }

    Task Ready(DiscordClient client, SessionReadyEventArgs args)
    {
        try
        {
            Logger.Put($"Logged in on user {User.Username}#{User.Discriminator} (ID {User.Id})");
        }
        catch (Exception e)
        {
            Logger.Put("Discord session created!");
        }
        return Task.CompletedTask;
    }

    #region Message

    private async Task MessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (Config.values.blockedUsers.Contains(args.Message.Author.Id))
            return;

        if (Config.values.frogRoleActivation == FrogRoleActivation.REPLY)
            await FrogRoleMessageCreated(client, args);
    }

    private async Task FrogRoleMessageCreated(DiscordClient client, MessageCreateEventArgs args)
    {
        if (frogMsg is null && !string.IsNullOrEmpty(Config.values.frogMessageLink))
        {
            Logger.Warn("Frog message not found!");
            return;
        }

        if (frogMsg is null || args.Message.ReferencedMessage != frogMsg)
            return;

        await AssignNewFrogKing(client, args.Guild, args.Message.Author);
    }

    #endregion

    #region Reaction
    async Task ReactionAdded(DiscordClient client, MessageReactionAddEventArgs args)
    {
        //todo: add caching member roles/count
        
        if (Config.values.blockedUsers.Contains(args.User.Id))
            return;

        if (Config.values.frogRoleActivation == FrogRoleActivation.REACTION)
            await FrogRoleReactionAdded(client, args);

        await BoneBoardReactionAdded(client, args);
    }


    private async Task FrogRoleReactionAdded(DiscordClient client, MessageReactionAddEventArgs args)
    {
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

        foreach (DiscordUser user in await args.Message.GetReactionsAsync(args.Emoji, 25))
        {
            if (user == args.User || user == client.CurrentUser)
                continue;


            Logger.Put($"Removing frog reaction by {user.GlobalName} ({user.Username}) from frog message.", LogType.Debug);
            await args.Message.DeleteReactionAsync(args.Emoji, user, "The king is dead, long live the king!");
        }

        await AssignNewFrogKing(client, args.Guild, args.User);

        assigningNewKing = false;
    }


    private async Task BoneBoardReactionAdded(DiscordClient client, MessageReactionAddEventArgs args)
    {

        if (!Config.values.requiredEmojis.Contains(args.Emoji.Id)) return;
        if (args.Channel.IsPrivate || args.User is not DiscordMember member) return;
        if (!MemberReactionCounts(member)) return;

        IReadOnlyList<DiscordUser> usersThatReacted;
        List<DiscordMember> membersThatReacted;
        try
        {
            usersThatReacted = await args.Message.GetReactionsAsync(args.Emoji, 25);
            membersThatReacted = new();
            foreach (DiscordUser user in usersThatReacted)
            {
                DiscordMember memb = await args.Guild.GetMemberAsync(user.Id);
                membersThatReacted.Add(memb);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch reactions on " + args.Message.ToString(), ex);
            return;
        }

        //todo: remove this check and rely on internal cache maybe
        if (usersThatReacted.Any(IsMe)) return; // already reacted

        if (membersThatReacted.Where(MemberReactionCounts).Count() < Config.values.requiredReactionCount)
            return;

        try
        {
            await PerformQuote(args.Message, args.Emoji);
        }
        catch (Exception ex)
        {
            if (logChannel is not null)
                await logChannel.SendMessageAsync($"Failed to quote [a message]({args.Message.JumpLink}), see exception below for details\n" + ex);

            Logger.Error($"Failed to quote a message {args.Message.JumpLink}", ex);
        }
    }
    #endregion

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
            
            if (frogMsg is not null && frogMsg.Content != str)
            {
                await frogMsg.ModifyAsync(str);
                frogMsg = await frogMsg.Channel.GetMessageAsync(frogMsg.Id, true);
            }

            return;
        }

        if (frogKing is not null && frogKing.Id == newKing.Id)
        {
            Logger.Put("The current frog king " + frogKing +" re-reacted to the frog message... lol?", LogType.Debug);
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
                catch(NotFoundException)
                {
                    Logger.Put("Well that's because they're not here anymore... what a bitch. Clearing frogking.");
                    frogKing = null;
                }
            }

            //guild.RequestMembersAsync

            if (frogKing is null || !frogKing.Roles.Any(r => r.Id == Config.values.frogRole))
            {
                Logger.Warn($"Fetching all members of {guild.Name} because {(frogKing is null ? "there's no saved frog king" : $"the frog king '{frogKing.DisplayName}' didn't have the frog role")}");

                foreach (DiscordMember memberson in await guild.GetAllMembersAsync())
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

    async void UpdateLeaderboard()
    {
        TimeSpan wait = TimeSpan.FromMinutes(Config.values.leaderboardUpdatePeriodMin);
        while (true)
        {
            await Task.Delay(wait);

            if (frogMsg is null || frogLeaderboardMsg is null)
                continue;

            int baseLen = Config.values.frogLeaderboardBase.Length;
            StringBuilder sb = new();
            sb.AppendLine();

            List<ulong> leftUsers = new(2);

            foreach (var kvp in PersistentData.values.frogRoleTimes.OrderByDescending(kvp => kvp.Value))
            {
                DiscordMember? memberson = null;
                try
                {
                    memberson = await frogMsg.Channel.Guild.GetMemberAsync(kvp.Key, false);
                }
                catch(NotFoundException notFoundEx)
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
                string newLine = $"**{disName}** - {(weeks == 0 ? "" : (weeks) + "wk, ")}{(days == 0 ? "" : (days) + "d, ")}{span.Hours}h {span.Minutes}m{span.Seconds}s";

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
    }

    bool MemberReactionCounts(DiscordMember member)
    {
        if (member.IsBot)
            return false;

        foreach (DiscordRole role in member.Roles)
        {
            if (Config.values.requiredRoles.Contains(role.Id)) return true;
        }

        return false;
    }

    bool IsMe(DiscordUser user) => user == User;

    public async Task PerformQuote(DiscordMessage msg, DiscordEmoji? triggeredEmoji)
    {
        if (msg.Author is null)
            msg = await msg.Channel.GetMessageAsync(msg.Id);

        if (quoteOutputChannel is null)
        {
            Logger.Error("Unable to perform quote! Output channel is null!");
            return;
        }
        if (logChannel is null)
        {
            Logger.Warn("Unrecommended to continue performing quotes! Log channel is null!");
        }

        try
        {
            if (triggeredEmoji is not null)
            await msg.CreateReactionAsync(triggeredEmoji);
        }
        catch(Exception ex)
        {
            Logger.Warn($"Bot is blocked by {msg.Author}, cannot react so will not continue. Full exception: " + ex);
            if (logChannel is not null)
                await logChannel.SendMessageAsync($"Bot is blocked by {msg.Author} or otherwise cannot react, so will not continue quoting {msg.JumpLink}");
        }


        string displayName = msg.Author is DiscordMember member ? member.DisplayName : msg.Author.GlobalName ?? msg.Author.Username;
        using Image quote = await Quoter.GenerateImageFrom(msg, client);
        using MemoryStream ms = new();

        await quote.SaveAsPngAsync(ms);
        ms.Position = 0;

        DiscordMessageBuilder dmb = new DiscordMessageBuilder()
                                        .AddFile("quote.png", ms)
                                        .WithContent($"[From {displayName}]({msg.JumpLink})");

        try
        {
            await quoteOutputChannel.SendMessageAsync(dmb);
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while trying to send message (or react) for quote process's final step!", ex);
        }
    }

    public async Task FetchFrogMessage()
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

        foreach (DiscordGuild guild in client.Guilds.Values)
        {
            if (guild.Channels.TryGetValue(frogMsgChannel.Value, out DiscordChannel? dCh))
            {
                frogMsg = await dCh.GetMessageAsync(frogMsgId.Value);
                return;
            }

            foreach (var thread in guild.Channels.SelectMany(chp => chp.Value.Type == ChannelType.Text ? chp.Value.Threads : Enumerable.Empty<DiscordThreadChannel>()))
            {
                if (thread.Id == frogMsgChannel.Value)
                {
                    frogMsg = await thread.GetMessageAsync(frogMsgId.Value);
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

        foreach (DiscordGuild guild in client.Guilds.Values)
        {
            if (guild.Channels.TryGetValue(frogMsgChannel.Value, out DiscordChannel? dCh))
            {
                frogLeaderboardMsg = await dCh.GetMessageAsync(frogMsgId.Value);
                return;
            }

            foreach (var thread in guild.Channels.SelectMany(chp => chp.Value.Type == ChannelType.Text ? chp.Value.Threads : Enumerable.Empty<DiscordThreadChannel>()))
            {
                if (thread.Id == frogMsgChannel.Value)
                {
                    frogLeaderboardMsg = await thread.GetMessageAsync(frogMsgId.Value);
                    return;
                }
            }
        }

        Logger.Error("Failed to fetch message link @ " + Config.values.frogMessageLink);
    }
}
