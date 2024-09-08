using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using SixLabors.Fonts.Tables.AdvancedTypographic;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BoneBoard;

internal partial class BoneBot
{
    const long DEFAULT_FILE_SIZE_LIMIT = 25 * 1000 * 1000; // 25MB

    public static Dictionary<DiscordClient, BoneBot> Bots { get; } = new();

    internal Casino casino;
    internal Hangman hangman;
    internal DiscordClientBuilder clientBuilder;
    internal DiscordClient client;
    DiscordUser User => client.CurrentUser;

    internal Dictionary<DiscordGuild, HashSet<DiscordChannel>> allChannels = new();
    //sanitizing
    static readonly Regex mdCleaningRegex = MarkdownCleaningRegex();

    //message buffer
    Timer? dumpMessagesTimer;
    Dictionary<DiscordChannel, Queue<DiscordMessage>> queuedMessages = new();
    Dictionary<string, DiscordMessage> recreatedContentToReferences = new();
    Dictionary<string, string> cachedQueuedAttachmentPaths = new();
    HttpClient attachmentDownloadClient = new();

    readonly Dictionary<DiscordPremiumTier, long> FileSizeLimits = new()
    {
        { DiscordPremiumTier.None, DEFAULT_FILE_SIZE_LIMIT },
        { DiscordPremiumTier.Tier_1, DEFAULT_FILE_SIZE_LIMIT },
        { DiscordPremiumTier.Tier_2, 50 * 1000 * 1000 },
        { DiscordPremiumTier.Tier_3, 100 * 1000 * 1000 },
    };

    // cool confessional
    DiscordChannel? confessionalChannel;
    Dictionary<DiscordMember, DateTime> confessions = new();
    List<DiscordMessage> confesssionsByAi = new();
    OpenAIClient? openAiClient;

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

    private bool calledAllChannelsRecieved;
    private Action<Dictionary<DiscordGuild, HashSet<DiscordChannel>>>? allChannelsRecieved;
    internal event Action<Dictionary<DiscordGuild, HashSet<DiscordChannel>>> AllChannelsRecieved
    {
        add
        {
            if (calledAllChannelsRecieved)
                value(allChannels);
            allChannelsRecieved += value;
        }
        remove { allChannelsRecieved -= value; }
    }

    public static async void TryRestoreConnections()
    {
        foreach (var client in Bots.Keys)
        {
            try
            {
                await client.ReconnectAsync();
                Logger.Put("Reconnected on " + client.CurrentUser);
            }
            catch (Exception ex)
            {
                Logger.Error("Exception while trying to restore connection! " + ex);
            }
        }
    }

    public BoneBot(string token)
    {
        //Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.addconsole)
        clientBuilder = DiscordClientBuilder.CreateDefault(token, DiscordIntents.GuildMessages | DiscordIntents.MessageContents | DiscordIntents.GuildMessageReactions | DiscordIntents.Guilds | DiscordIntents.GuildMembers);
        //clientBuilder.SetLogLevel(LogLevel.Trace);
        //clientBuilder.ConfigureGatewayClient(c => c.GatewayCompressionLevel = GatewayCompressionLevel.None);
        clientBuilder.ConfigureServices(x => x.AddLogging(y => y.AddConsole(clo => clo.LogToStandardErrorThreshold = LogLevel.Trace)));
        clientBuilder.ConfigureServices(x => x.AddSingleton(typeof(BoneBot), this));
        casino = new(this);
        hangman = new(this);

        SlashCommandProcessor scp = new();
        MessageCommandProcessor mcp = new();
        clientBuilder.UseCommands(ce =>
        {
            ce.AddProcessors(scp, mcp);
            ce.AddCommands([typeof(SlashCommands), typeof(MessageContextActions), typeof(Casino), typeof(Hangman)]);
        }, new()
        {
            //ServiceProvider = sc.BuildServiceProvider(),
            RegisterDefaultCommandProcessors = false
        });

        clientBuilder.ConfigureEventHandlers(e =>
        {
            e.HandleGuildDownloadCompleted(GetGuildResources)
                .HandleSessionCreated(Ready)
                .HandleMessageReactionAdded(ReactionAdded)
                .HandleMessageCreated(MessageCreated)
                .HandleMessageUpdated(MessageUpdated);
        });
        client = clientBuilder.Build();

        if (!string.IsNullOrEmpty(Config.values.openAiToken))
        {
            openAiClient = new(new System.ClientModel.ApiKeyCredential(Config.values.openAiToken));
        }

        Bots.Add(client, this);
    }

    public void ConfigureEvents(Action<EventHandlingBuilder> action)
    {
        if (client is not null)
            return;
        //throw new InvalidOperationException("Cannot add events after client is built!");

        clientBuilder.ConfigureEventHandlers(action);
    }

    public async void Init()
    {
        //todo re-add commands

        await client.ConnectAsync();

        try
        {
            UpdateLeaderboard();
            OccasionalAiConfessional();
        }
        catch (Exception ex)
        {
            Logger.Error("Exception in periodic-runner! " + ex);
        }
    }

    private async Task GetGuildResources(DiscordClient client, GuildDownloadCompletedEventArgs args)
    {
        foreach (var channelKvp in args.Guilds.Values.SelectMany(dg => dg.Channels))
        {
            if (!allChannels.TryGetValue(channelKvp.Value.Guild, out var allChannelsSlice))
                allChannelsSlice = allChannels[channelKvp.Value.Guild] = new();

            allChannelsSlice.Add(channelKvp.Value);
            if (channelKvp.Value.Type == DiscordChannelType.Text)
            {
                foreach (DiscordThreadChannel thread in channelKvp.Value.Threads)
                {
                    allChannelsSlice.Add(thread);
                }
            }

            if (channelKvp.Key == Config.values.outputChannel)
                quoteOutputChannel = channelKvp.Value;
            else if (channelKvp.Key == Config.values.confessionalChannel)
                confessionalChannel = channelKvp.Value;
            else if (channelKvp.Key == Config.values.logChannel)
                logChannel = channelKvp.Value;
            else
            {
                if (channelKvp.Value.Type != DiscordChannelType.Text) continue;

                foreach (DiscordThreadChannel thread in channelKvp.Value.Threads)
                {
                    if (thread.Id == Config.values.outputChannel)
                        quoteOutputChannel = thread;
                    else if (thread.Id == Config.values.confessionalChannel)
                        confessionalChannel = thread;
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

        allChannelsRecieved?.InvokeActionSafe(allChannels);
        calledAllChannelsRecieved = true;

        await hangman.Init();
    }

    async Task Ready(DiscordClient client, SessionCreatedEventArgs args)
    {
        try
        {
            Logger.Put($"Logged in on user {User.Username}#{User.Discriminator} (ID {User.Id})");
        }
        catch (Exception e)
        {
            Logger.Put("Discord session created!");
        }
    }

    #region Message

    private async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (args.Message.Author is null || Config.values.blockedUsers.Contains(args.Message.Author.Id))
            return;
        DiscordMember? member = args.Author as DiscordMember;
        bool hasManageMessages = member is not null && member.Permissions.HasPermission(DiscordPermissions.ManageMessages);

        if (!hasManageMessages)
        {
            string? messageDeleteReason = await GetDeleteReasonIfNotAllowed(args.Message);
            if (messageDeleteReason is not null)
            {
                try
                {
                    await args.Message.DeleteAsync(messageDeleteReason);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to delete message with reason {messageDeleteReason}", ex);
                }
                return;
            }
        }

        if (Config.values.channelsWhereUsersAreProhibitedFromMedia.TryGetValue(args.Channel.Id.ToString(), out ulong[]? mediaUserIds) && mediaUserIds.Contains(args.Author.Id))
        {
            if (args.Message.Attachments.Count > 0 || args.Message.Embeds.Count > 0)
            {
                try
                {
                    await args.Message.DeleteAsync("this user gets no media in this channel. woe.");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to delete message with media from {member}! ", ex);
                }
            }
        }

        if (Config.values.channelsWhereUsersAreProhibitedFromCustomEmojis.TryGetValue(args.Channel.Id.ToString(), out ulong[]? emojiUserIds) && emojiUserIds.Contains(args.Author.Id))
        {
            string? reason = null;
            if (args.Message.Stickers?.FirstOrDefault()?.Type == DiscordStickerType.Guild) // false if no stickers
                reason = "this user gets no stickers in this channel. woe.";

            
            if (Quoter.CustomEmoji.IsMatch(args.Message.Content)) //todo: ignore emojis from args.Guild 
                reason = "this user cant use custom emojis in this channel. woe.";

            try
            {
                if (reason is not null)
                {
                    await args.Message.DeleteAsync(reason);
                    return;
                }
                
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to delete message with custom emoji from {member}! ", ex);
            }
        }

        if (Config.values.channelsWhereAllFlagsButListedAreProhibited.TryGetValue(args.Channel.Id.ToString(), out string[]? allowedFlags) && args.Message.Content.Length >= 4)
        {
            const char REG_INDIC_START = '\ud83c';
            // long story short: country flags (like :flag_us:) are just two "regional indicator" emojis
            // but each "regional indicator" emoji is a pair of two unicode characters, each starting with \ud83c
            DiscordEmoji? lastRegional = null;
            char lastChar = ' ';
            foreach (char c in args.Message.Content)
            {
                if (char.GetUnicodeCategory(c) != UnicodeCategory.Surrogate) // "flush" last regional indicator if the curr char cant be a part of a reg indic
                    lastRegional = null;
                

                if (lastChar == REG_INDIC_START && char.GetUnicodeCategory(c) == UnicodeCategory.Surrogate)
                {
                    if (DiscordEmoji.TryFromUnicode(lastChar.ToString() + c, out DiscordEmoji? regionalIndicator))
                    {
                        if (lastRegional is null)
                        {
                            // prepare to check if this is a valid flag
                            lastRegional = regionalIndicator;
                            lastChar = c;
                            continue;
                        }
                        else
                        {
                            // check if its a full valid flag
                            string flag = lastRegional.Name + regionalIndicator.Name;
                            if (DiscordEmoji.TryFromUnicode(flag, out DiscordEmoji? compoundEmoji))
                            {
                                if (allowedFlags.Contains(compoundEmoji.Name))
                                {
                                    lastRegional = null; // flush so flags can exist without a space in between
                                    lastChar = c;
                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        await args.Message.DeleteAsync("you must rep the right flag in this channel. woe.");
                                        return;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warn($"Failed to delete message with flag emoji from {member}! ", ex);
                                    }
                                }
                            }
                        }
                    }
                    else
                        Logger.Warn($"Failed to parse emoji from {lastChar}{c} - it should be a regional indicator, no? See full message: {args.Message}");
                }


                lastChar = c;
            }
        }


        if (Config.values.frogRoleActivation == FrogRoleActivation.REPLY)
            await FrogRoleMessageCreated(client, args);

        bool isBufferExempt = member is not null && member.Roles.Any(r => Config.values.bufferExemptRoles.Contains(r.Id));
        if (PersistentData.values.bufferedChannels.Contains(args.Channel.Id) && !isBufferExempt && !IsMe(args.Author))
            await BufferMessage(args.Message);
    }

    private async Task<string?> GetDeleteReasonIfNotAllowed(DiscordMessage msg)
    {
        ulong channelId = msg.Channel?.Id ?? default;
        string checkWordsAgainst = msg.Content.Replace('’', '\'').Replace("'", "");
        if (channelId == default)
            return null;

        string[] words = checkWordsAgainst.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float wordPercentage = words.Length == 0 ? 1 : words.Count(w => Config.values.theWordOrWords.Any(s => s.Equals(w, StringComparison.InvariantCultureIgnoreCase))) / (float)words.Length;
        bool wordPercTooLowInChannel = Config.values.channelsWhereMessagesMustHaveMinPercOfAWord.Contains(channelId) && wordPercentage < Config.values.wordPercentage;

        bool needsToStartWithString = Config.values.channelsWhereMessagesMustStartWith.Contains(channelId) && words.Length != 0;
        bool startsWithString = Config.values.possibleMessageStarts.Any(s => checkWordsAgainst.StartsWith(s, StringComparison.InvariantCultureIgnoreCase));

        bool hasVowelInDisallowedChannel = Config.values.channelsWhereNoVowelsAreAllowed.Contains(channelId) && msg.Content.Any(c => "aeiou".Contains(c, StringComparison.InvariantCultureIgnoreCase));

        bool isValidHangmanGuess = msg.Content.Length == 1 && char.IsLetter(msg.Content[0])
                             || msg.Content.Length == PersistentData.values.currHangmanWord.Length;
        bool replyingToHangman = msg.ReferencedMessage?.JumpLink.OriginalString == Config.values.hangmanMessageLink;
        bool isProperHangman = replyingToHangman && isValidHangmanGuess;

        if (!isProperHangman)
        {
            if (hasVowelInDisallowedChannel)
                return "has vowels. cnat edit to do that., u tried'";

            if (wordPercTooLowInChannel)
                return "message doesn't contain enough of the necessary words";

            if (needsToStartWithString && !startsWithString)
                return "doesnt start with the right string";
        }

        return null;
    }

    private async Task MessageUpdated(DiscordClient sender, MessageUpdatedEventArgs args)
    {
        if (args.Author?.IsBot ?? true)
            return;
        DiscordMember? member = args.Author as DiscordMember;
        bool hasManageMessages = member is not null && member.Permissions.HasPermission(DiscordPermissions.ManageMessages);

        if (!hasManageMessages && Config.values.channelsWhereNoVowelsAreAllowed.Contains(args.Channel.Id) && args.Message.Content.Any(c => "aeiou".Contains(c, StringComparison.InvariantCultureIgnoreCase)))
        {
            await args.Message.DeleteAsync("has vowels. cnat edit to do that., u tried'");
        }
    }

    private async Task FrogRoleMessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (frogMsg is null && !string.IsNullOrEmpty(Config.values.frogMessageLink))
        {
            Logger.Warn("Frog message not found!");
            return;
        }

        if (frogMsg is null || args.Message.ReferencedMessage is null || args.Message.ReferencedMessage != frogMsg || args.Message.Author is null)
            return;



        await AssignNewFrogKing(client, args.Guild, args.Message.Author);
    }

    #endregion

    #region Reaction
    async Task ReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args)
    {
        //todo: add caching member roles/count

        if (Config.values.blockedUsers.Contains(args.User.Id))
            return;

        if (Config.values.channelsWhereUsersAreProhibitedFromCustomEmojis.TryGetValue(args.Channel.Id.ToString(), out ulong[]? emojiUserIds) && emojiUserIds.Contains(args.User.Id))
        {
            if (args.Emoji.RequiresColons)
            {

                try
                {
                    await args.Message.DeleteReactionAsync(args.Emoji, args.User, "this user cant use custom emojis in this channel. woe.");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to delete custom emoji reaction on message from {args.User}! ", ex);
                }
            }
        }

        if (Config.values.channelsWhereAllFlagsButListedAreProhibited.TryGetValue(args.Channel.Id.ToString(), out string[]? allowedFlags))
        {
            // this is gonna stop things that are custom emojis with flag_ in them, but that's a small price to pay (and also the white flag)
            if (args.Emoji.Id == 0 && args.Emoji.GetDiscordName().Contains("flag_") && !allowedFlags.Contains(args.Emoji.Name))
            {
                try
                {
                    await args.Message.DeleteReactionAsync(args.Emoji, args.User, "you must rep the right flag in this channel. woe.");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to delete custom emoji reaction on message from {args.User}! ", ex);
                }
            }
        }

        if (Config.values.frogRoleActivation == FrogRoleActivation.REACTION)
            await FrogRoleReactionAdded(client, args);

        await BoneBoardReactionAdded(client, args);
    }


    private async Task FrogRoleReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args)
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

        await foreach(DiscordUser user in args.Message.GetReactionsAsync(args.Emoji))
        {
            if (user == args.User || user == client.CurrentUser)
                continue;


            Logger.Put($"Removing frog reaction by {user.GlobalName} ({user.Username}) from frog message.", LogType.Debug);
            await args.Message.DeleteReactionAsync(args.Emoji, user, "The king is dead, long live the king!");
        }

        await AssignNewFrogKing(client, args.Guild, args.User);

        assigningNewKing = false;
    }


    private async Task BoneBoardReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args)
    {

        if (!Config.values.requiredEmojis.Contains(args.Emoji.Id)) return;
        if (args.Channel.IsPrivate || args.User is not DiscordMember member) return;
        if (!MemberReactionCounts(member)) return;

        List<DiscordMember> membersThatReacted;
        try
        {
            membersThatReacted = new();
            await foreach (DiscordUser user in args.Message.GetReactionsAsync(args.Emoji))
            {
                //todo: remove this check and rely on internal cache maybe
                if (IsMe(user))
                    return; // already reacted

                DiscordMember memb = await args.Guild.GetMemberAsync(user.Id);
                
                membersThatReacted.Add(memb);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch reactions on " + args.Message.ToString(), ex);
            return;
        }


        if (membersThatReacted.Where(MemberReactionCounts).Count() < Config.values.requiredReactionCount)
            return;

        try
        {
            Logger.Put("Now quoting message " + args.Message);
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

    async void OccasionalAiConfessional()
    {
        while (true)
        {
            try
            {
                int dist = Config.values.confessionalCooldownHoursMax - Config.values.confessionalCooldownHoursMin;
                double hours = Config.values.confessionalCooldownHoursMin + (dist * Random.Shared.NextDouble());
                TimeSpan wait = TimeSpan.FromHours(hours);
                await Task.Delay(wait);

                Logger.Put("Time for an AI confession!");
                await SendAiConfessional();
            }
            catch (Exception ex)
            {
                Logger.Error("Exception in occasional AI confessional! " + ex);
            }
        }
    }

    internal async Task SendAiConfessional()
    {
        if (confessionalChannel is null)
            return;

        DiscordMember botInServer;
        try
        {
            botInServer = await confessionalChannel.Guild.GetMemberAsync(client.CurrentUser.Id);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Bot not found in server/other exception while trying to get member object for {client.CurrentUser} in {confessionalChannel?.Guild}! " + ex);
            return;
        }

        if (openAiClient is null)
            return;

        string mainModel = Config.values.openAiConfessionalModel;
        string mainSysPrompt = Config.values.openAiConfessionalSystemPrompt;
        string mainUserPrompt = Config.values.openAiConfessionalPrompt;
        string sanityModel = Config.values.openAiSanityModel;
        string sanitySysPrompt = Config.values.openAiConfessionalSanityPrompt;
        string sanityUserPrompt = Config.values.openAiConfessionalSanityPrompt;
        if (string.IsNullOrEmpty(mainModel) || string.IsNullOrEmpty(mainSysPrompt) || string.IsNullOrEmpty(mainUserPrompt) || string.IsNullOrEmpty(sanityModel) || string.IsNullOrEmpty(sanitySysPrompt) || string.IsNullOrEmpty(sanityUserPrompt))
            return;

        Logger.Put("Now generating an AI confession!");

        var mainClient = openAiClient.GetChatClient(Config.values.openAiConfessionalModel);
        var sanityClient = openAiClient.GetChatClient(Config.values.openAiSanityModel);
        ChatCompletionOptions mainOptions = new()
        {
            MaxTokens = 256,
            Temperature = 0.5f,
        };

        string chatResponse = "j";
        bool sanitySignoff = false;
        for (int i = 0; i < 25; i++)
        {
            var mainPrompt = new ChatMessage[]
            {
                new SystemChatMessage(mainSysPrompt),
                new UserChatMessage(mainUserPrompt)
            };
            var mainRes = await mainClient.CompleteChatAsync(mainPrompt, mainOptions);

            chatResponse = mainRes.Value.ToString();
            if (string.IsNullOrEmpty(chatResponse))
                continue;


            Logger.Put($"Got AI response for round {i + 1} - {chatResponse}");

            var altPrompt = new ChatMessage[]
            {
                new SystemChatMessage(sanitySysPrompt),
                new UserChatMessage(chatResponse)
            };

            var sanityRes = await sanityClient.CompleteChatAsync(altPrompt);
            string sanityResponse = sanityRes.Value.ToString();
            Logger.Put($"Got AI sanitychecker response for round {i + 1} - {sanityResponse}");


            if (string.IsNullOrEmpty(sanityResponse))
                continue;
            if (sanityResponse.Contains(Config.values.sanityAffirmative, StringComparison.CurrentCultureIgnoreCase))
            {
                Logger.Put("Sanity check passed, posting confession!");
                sanitySignoff = true;
                break;
            }
        }

        if (!sanitySignoff)
            return;


        DiscordMessage? message = await SendConfessional(botInServer, chatResponse);
        if (message is not null)
            confesssionsByAi.Add(message);
    }

    bool MemberReactionCounts(DiscordMember member)
    {
        if (member.IsBot || Config.values.blockedUsers.Contains(member.Id))
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
        if (msg.Author is null && msg.Channel is not null)
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

        if (msg.Author is not null && Config.values.blockedUsers.Contains(msg.Author.Id))
        {
            Logger.Put("Bailing on quote. Message author is in blocked user list");
            return;
        }

        try
        {
            if (triggeredEmoji is not null)
                await msg.CreateReactionAsync(triggeredEmoji);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Bot is blocked by {msg.Author}, cannot react so will not continue. Full exception: " + ex);
            if (logChannel is not null)
                await logChannel.SendMessageAsync($"Bot is (probably?) blocked by {msg.Author} or otherwise cannot react, so will not continue quoting {msg.JumpLink}");
        }

        string displayName = msg.Author is DiscordMember member ? member.DisplayName : msg.Author?.GlobalName ?? msg.Author?.Username ?? "A user";
        using Image? quote = await Quoter.GenerateImageFrom(msg, client);
        if (quote is null)
        {
            Logger.Warn("Failed to generate quote image for message " + msg.JumpLink);
            return;
        }

        using MemoryStream ms = new();

        await quote.SaveAsPngAsync(ms);
        ms.Position = 0;

        DiscordMessageBuilder dmb = new DiscordMessageBuilder()
                                        .AddFile("quote.png", ms)
                                        .WithContent($"[From {displayName.Replace("]", "")}]({msg.JumpLink})");

        try
        {
            await quoteOutputChannel.SendMessageAsync(dmb);
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while trying to send message for quote process's final step!", ex);
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

            foreach (var thread in guild.Channels.SelectMany(chp => chp.Value.Type == DiscordChannelType.Text ? chp.Value.Threads : Enumerable.Empty<DiscordThreadChannel>()))
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

            foreach (var thread in guild.Channels.SelectMany(chp => chp.Value.Type == DiscordChannelType.Text ? chp.Value.Threads : Enumerable.Empty<DiscordThreadChannel>()))
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

    public async Task<DiscordMessage?> SendConfessional(DiscordMember member, string text)
    {
        if (confessionalChannel is null)
            return null;


        if (!IsMe(member))
        {
            if (Config.values.confessionalRestrictions.HasFlag(ConfessionalRequirements.ROLE))
            {
                if (member.Roles.All(r => r.Id != Config.values.confessionalRole))
                    return null;
            }

            if (Config.values.confessionalRestrictions.HasFlag(ConfessionalRequirements.COOLDOWN))
            {
                if (confessions.TryGetValue(member, out DateTime lastConfession) && (DateTime.Now - lastConfession).TotalHours < 6)
                    return null;
                confessions[member] = DateTime.Now;
            }
        }

        string[] slurPatterns =
        {
            "*fag*",
            "niga",
            "niger",
            "*nigg*",
        };

        foreach (string pattern in slurPatterns)
        {
            if (Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeString(text, pattern, Microsoft.VisualBasic.CompareMethod.Text))
            {
                Logger.Put($"The confession '{text}' failed against the pattern {pattern} and is rejected.");
                return null;
            }
        }

        Logger.Put($"Going to send a msg in the confessional channel #{confessionalChannel.Name} - {text}");

        var deb = new DiscordEmbedBuilder()
            .WithTitle($"A {member.Guild.Name} confession...")
            .WithDescription(text);

        var dmb = new DiscordMessageBuilder()
            .WithAllowedMentions(Enumerable.Empty<IMention>())
            .AddEmbed(deb);

        string authorString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{member.Username} - id {member.Id}"));
        try
        {
            DiscordMessage msg = await confessionalChannel.SendMessageAsync(dmb);

            if (!IsMe(member))
                Logger.Put($"Confession sent was from the following B64-encoded user: {authorString}", LogType.Debug);
            else
                Logger.Put($"Confession sent by an AI. Be proud of your little bot. It's trying.", LogType.Debug);

            HandleConfessionVotingFor(msg);
            return msg;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send a confessional by {authorString} - {ex}");
            return null;
        }

    }

    async void HandleConfessionVotingFor(DiscordMessage msg)
    {
        if (openAiClient is null)
            return;

        DiscordEmoji? botEmoji, humanEmoji;
        try
        {
            if (!DiscordEmoji.TryFromUnicode(Config.values.aiConfessionIsBotEmoji, out botEmoji))
                if (ulong.TryParse(Config.values.aiConfessionIsBotEmoji, out ulong botEmojiId))
                    DiscordEmoji.TryFromGuildEmote(client, botEmojiId, out botEmoji);

            if (!DiscordEmoji.TryFromUnicode(Config.values.aiConfessionIsHumanEmoji, out humanEmoji))
                if (ulong.TryParse(Config.values.aiConfessionIsHumanEmoji, out ulong humanEmojiId))
                    DiscordEmoji.TryFromGuildEmote(client, humanEmojiId, out botEmoji);

            if(botEmoji is null)
            {
                Logger.Put($"Failed to get DiscordEmoji from '{Config.values.aiConfessionIsBotEmoji}' for confessional AI-or-not reactions! Bailing!");
                return;
            }

            if (humanEmoji is null)
            {
                Logger.Put($"Failed to get DiscordEmoji from '{Config.values.aiConfessionIsHumanEmoji}' for confessional AI-or-not reactions! Bailing!");
                return;
            }

            await msg.CreateReactionAsync(humanEmoji);
            await msg.CreateReactionAsync(botEmoji);
        }
        catch(Exception ex)
        {
            Logger.Warn("Exception while adding emoji for confessional AI-or-not reactions!", ex);
            return;
        }

        await Task.Delay(TimeSpan.FromHours(Config.values.confessionalAiVotingPeriodHours));

        bool isAi = confesssionsByAi.Contains(msg);

        try
        {
            DiscordEmbedBuilder deb = new(msg.Embeds[0]);
            deb.WithFooter(isAi ? "From an AI" : "From a human");
            await msg.ModifyAsync(deb.Build());
        }
        catch(Exception ex)
        {
            Logger.Warn($"Exception when editing confession message ({msg}) to reveal ground-truth!", ex);
        }

        try
        {
            DiscordEmoji correctEmoji = isAi ? botEmoji : humanEmoji;
            DiscordEmoji incorrectEmoji = isAi ? humanEmoji : botEmoji;
            var correctUsers = msg.GetReactionsAsync(correctEmoji).ToBlockingEnumerable();
            var incorrectUsers = msg.GetReactionsAsync(incorrectEmoji).ToBlockingEnumerable();
            
            foreach (DiscordUser user in correctUsers)
            {
                if (incorrectUsers.Contains(user))
                    casino.GivePoints(user, -2000);
                else
                    casino.GivePoints(user, isAi ? 1000 : 100);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Exception giving out points for confessional AI voting! ", ex);
        }
    }

    async Task BufferMessage(DiscordMessage msg)
    {
        try
        {
            if (!FileSizeLimits.TryGetValue(msg.Channel.Guild.PremiumTier, out long fileSizeLimit))
                fileSizeLimit = DEFAULT_FILE_SIZE_LIMIT;

            foreach (DiscordAttachment attachment in msg.Attachments)
            {
                if (attachment.FileSize > fileSizeLimit)
                {
                    Logger.Put($"Attachment {attachment.FileName} on message {msg.JumpLink} is too big to buffer! ({Math.Round(attachment.FileSize / 1024.0 / 1024.0, 2)}MB > {Math.Round(fileSizeLimit / 1024.0 / 1024.0, 2)}MB)");
                    continue;
                }
                if (string.IsNullOrEmpty(attachment.Url))
                {
                    Logger.Error("Attachment on message " + msg.JumpLink + " has no URL!");
                    continue;
                }

                string path = Path.GetTempFileName();
                using FileStream fs = File.OpenWrite(path);
                using Stream dlStream = await attachmentDownloadClient.GetStreamAsync(attachment.Url);
                await dlStream.CopyToAsync(fs);

                cachedQueuedAttachmentPaths[attachment.Url] = path;
            }

            if (!queuedMessages.TryGetValue(msg.Channel, out Queue<DiscordMessage>? queue))
            {
                queue = new();
                queuedMessages[msg.Channel] = queue;
            }

            queue.Enqueue(msg);

            await msg.DeleteAsync("Buffering message 😃👍"); // 😃👍
            Logger.Put($"Buffered message from {msg.Author} in {msg.Channel.Name} - {msg.Content}");
        }
        catch(Exception ex)
        {
            Logger.Error("Exception while buffering message! " + ex);
            logChannel?.SendMessageAsync("Exception while buffering message! " + ex);
        }
    }

    public void StartUnbufferTimer()
    {
        dumpMessagesTimer?.Change(Timeout.Infinite, Timeout.Infinite); // "neuter" the old timer

        TimeSpan waitTime = TimeSpan.FromMinutes(Config.values.bufferTimeMinutes);
        dumpMessagesTimer = new(_ => _ = SendBufferedMessages(), null, waitTime, waitTime);
    }

    public async Task SendBufferedMessages()
    {
        DateTime nextTime = DateTime.Now.AddMinutes(Config.values.bufferTimeMinutes);
        List<FileStream> openedFiles = new();

        foreach ((DiscordChannel channel, Queue<DiscordMessage> deletedMessages) in queuedMessages)
        {
            try
            {
                while (deletedMessages.Count > 0)
                {
                    DiscordMessage recreateMessage = deletedMessages.Dequeue();
                    Logger.Put($"Sending buffered message from {recreateMessage.Author}");
                    DiscordMessage? reference = recreateMessage.ReferencedMessage;

                    if (reference is not null && recreatedContentToReferences.TryGetValue(recreateMessage.ReferencedMessage.Content, out DiscordMessage? refMsg))
                        reference = refMsg;


                    string author = recreateMessage.Author is DiscordMember member ? mdCleaningRegex.Replace(member.DisplayName, @$"\$1").Replace("://", "\\://") : recreateMessage.Author.Username;
                    string replyingToAuthor = reference?.Author is DiscordMember replyMember ? mdCleaningRegex.Replace(replyMember.DisplayName, @$"\$1").Replace("https:", "https\\:") : reference?.Author.Username ?? "NOBODY LOL";
                    string replyingToContent = reference is not null ? Logger.EnsureShorterThan(reference.Content, 200, "(yap)").Replace("\n", "") : "";
                    string content = Logger.EnsureShorterThan(recreateMessage.Content, 2000 - replyingToAuthor.Length - replyingToContent.Length - author.Length - 50, "(Truncated due to exceessive yapping)");

                    string finalContent = (reference is not null ? $"Replying to **{replyingToAuthor}**: '{replyingToContent}'\n" : "")
                        + (!string.IsNullOrWhiteSpace(content) ? $"\"{content}\"\n" : "")
                        + $"\\- {author}";

                    recreatedContentToReferences[finalContent] = recreateMessage;

                    var builder = new DiscordMessageBuilder()
                        .WithContent(finalContent)
                        .WithAllowedMentions(Enumerable.Empty<IMention>());

                    foreach (DiscordAttachment attachment in recreateMessage.Attachments)
                    {
                        // todo: make uploads go to extraes.xyz when too big (requires skating around cloudflare's 100mb limit on free accts. try/add chunking!)
                        if (attachment.FileSize > DEFAULT_FILE_SIZE_LIMIT)
                            continue;

                        if (!cachedQueuedAttachmentPaths.TryGetValue(attachment.Url, out string? path))
                        {
                            Logger.Error("An attachment was on a queued message, but it wasn't cached on-disk!");
                            continue;
                        }

                        if (!File.Exists(path))
                        {
                            Logger.Error("An attachment supposedly queued, but it doesn't exist on-disk!");
                            continue;
                        }

                        FileStream fs = File.OpenRead(path);
                        string fileName = attachment.FileName;
                        while(builder.Files.Any(f => f.FileName == fileName))
                        {
                            
                            fileName = Random.Shared.Next(10) + "_" + fileName;
                        }
                        builder.AddFile(fileName, fs);
                        openedFiles.Add(fs);
                    }

                    await channel.SendMessageAsync(builder);

                    await Task.Delay(1000); // rate limit

                    foreach (FileStream fs in openedFiles)
                    {
                        fs.Close();
                        File.Delete(fs.Name); // 👍
                    }
                    openedFiles.Clear();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception sending buffered message! " + ex);
                logChannel?.SendMessageAsync("Exception sending buffered message! " + ex);
            }

            try
            {
                if (!PersistentData.values.bufferChannelMessages.TryGetValue(channel.Id, out ulong bufferNotifMsgId))
                {
                    Logger.Error($"Buffered channel {channel.Name} doesn't have a saved notification message ID!");
                    continue;
                }
                if (!PersistentData.values.bufferChannelMessageFormats.TryGetValue(channel.Id, out string? bufferFormat))
                {
                    Logger.Error($"Buffered channel {channel.Name} doesn't have a saved notification message format str!");
                    continue;
                }

                DiscordMessage bufferNotifMsg = await channel.GetMessageAsync(bufferNotifMsgId);
                await bufferNotifMsg.DeleteAsync("outlived its usefulness. FOLD.");

                string content = string.Format(bufferFormat, Formatter.Timestamp(nextTime, TimestampFormat.ShortTime));
                DiscordMessage msg = await channel.SendMessageAsync(content);
                PersistentData.values.bufferChannelMessages[channel.Id] = msg.Id;
                PersistentData.WritePersistentData();
            }
            catch (Exception ex)
            {
                Logger.Error("Exception while editing buffer notif msg! " + ex);
            }
        }

        TimeSpan waitTime = TimeSpan.FromMinutes(Config.values.bufferTimeMinutes);

        dumpMessagesTimer?.Change(nextTime - DateTime.Now, waitTime);
    }

    public async Task<DiscordMessage?> GetMessageFromLink(string link)
    {
        if (!link.Contains("/channels/"))
        {
            Logger.Put("Invalid message link: " + link);
            return null;
        }

        ulong? targtChannelId = null;
        ulong? targetMessageId = null;

        string[] idStrings = link.Split("/channels/");
        ulong[] ids = idStrings[1].Split('/').Skip(1).Select(ulong.Parse).ToArray();
        if (ids.Length >= 2)
        {
            targtChannelId = ids[0];
            targetMessageId = ids[1];
        }

        if (!targetMessageId.HasValue || !targtChannelId.HasValue)
            return null;

        DiscordChannel? channel;

        if (calledAllChannelsRecieved)
            channel = allChannels.SelectMany(kvp => kvp.Value).FirstOrDefault(ch => ch.Id == targtChannelId);
        else
        {
            // backup slow path
            try
            {
                // doesnt fucking work with threads AWESOME DUDE
                channel = await client.GetChannelAsync(targetMessageId.Value);
            }
            catch (Exception ex)
            {
                Logger.Warn("Caught exception while attempting to fetch channel for jump link " + link, ex);
                return null;
            }
        }


        if (channel is null)
            return null;

        try
        {
            DiscordMessage msg = await channel.GetMessageAsync(targetMessageId.Value);
            return msg;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<bool> TryReact(DiscordMessage message, params DiscordEmoji[] emojis)
    {
        try
        {
            foreach (DiscordEmoji emoji in emojis)
            {
                await message.CreateReactionAsync(emoji);

                if (emojis.Length != 1) 
                    await Task.Delay(1000); // discord is *really* tight on reaction ratelimits
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while reacting to message", ex);
            return false;
        }
    }

    [GeneratedRegex(@"(?<!\\)(\[|\]|\*|_|~|`|<|>|#)", RegexOptions.Compiled)]
    private static partial Regex MarkdownCleaningRegex();
}
