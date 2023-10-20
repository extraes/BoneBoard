using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
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
    DateTime lastSwitchTime = DateTime.Now;
    volatile bool assigningNewKing;

    // activity agnostic
    DiscordChannel? logChannel;

    public BoneBot(string token)
    {
        DiscordConfiguration cfg = new()
        {
            Token = token,
            Intents = DiscordIntents.GuildMessages | DiscordIntents.GuildMessageReactions | DiscordIntents.Guilds | DiscordIntents.GuildMembers,
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
            Logger.Error("Exception in periodic-runner!");
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
                frogKing = await guild.GetMemberAsync(frogKing!.Id, true);
            }

            // I'm trying to find a more efficient way of finding all users with a role than fetching every member all at once, and it looks like RequestMembersAsync's `query` parameter may be what I need, but I can't seem to find any documentation as to how I would create/format such a string. Are there any resources/examples on how to make such a string?
            // Fetching all members with a role/using RequestMembersAsync query string

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

            frogKing = newFrogKing;

            PersistentData.values.frogRoleTimes.TryGetValue(frogKing.Id, out TimeSpan ts);
            ts += DateTime.Now - lastSwitchTime;
            PersistentData.values.frogRoleTimes[frogKing.Id] = ts;

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
            
            foreach (var kvp in PersistentData.values.frogRoleTimes.OrderByDescending(kvp => kvp.Value))
            {
                DiscordMember memberson = await frogMsg.Channel.Guild.GetMemberAsync(kvp.Key, false);
                TimeSpan span = kvp.Value;
                string newLine = $"**{memberson.DisplayName}** - {(span.TotalDays / 7 == 0 ? "" : (span.TotalDays / 7) + "wk, ")}{(span.TotalDays == 0 ? "" : (span.TotalDays % 7) + "d, ")}{span.Hours}H {span.Minutes}m{span.Seconds}s";

                if (sb.Length + baseLen + newLine.Length + 1 < 2000)
                    sb.AppendLine(newLine);
                else break;
            }

            sb.AppendLine();

            string leaderboardTxt = string.Format(Config.values.frogLeaderboardBase, sb.ToString());
            
            Logger.Put($"Updating leaderboard message to a {leaderboardTxt.Length}-long string", LogType.Debug);

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
