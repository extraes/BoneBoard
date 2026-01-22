using BoneBoard.Modules;
using BoneBoard.Modules.Blockers;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using System.Reflection;
using System.Text.RegularExpressions;
using DSharpPlus.Commands.Trees;

namespace BoneBoard;

internal class BoneBot
{

    public static Dictionary<DiscordClient, BoneBot> Bots { get; } = new();

    internal ModuleBase[] blockers;
    internal Casino casino;
    internal Hangman hangman;
    internal FrogRole frogRole;
    internal Confessional confessions;
    internal Stargrid stargrid;
    internal MessageBuffer msgBuffer;
    internal ImageRoyale imageRoyale;
    internal VideoRoyale videoRoyale;
    internal StickyMessages stickyMessages;
    
    internal DiscordClientBuilder clientBuilder;
    internal DiscordClient client;
    DiscordUser User => client.CurrentUser;

    internal Dictionary<DiscordGuild, HashSet<DiscordChannel>> allChannels = new();

    // activity agnostic
    internal DiscordChannel? logChannel;

    private bool calledAllChannelsReceived;
    private Action<Dictionary<DiscordGuild, HashSet<DiscordChannel>>>? allChannelsReceived;
    internal event Action<Dictionary<DiscordGuild, HashSet<DiscordChannel>>> AllChannelsReceived
    {
        add
        {
            if (calledAllChannelsReceived)
                value(allChannels);
            allChannelsReceived += value;
        }
        remove { allChannelsReceived -= value; }
    }

    public BoneBot(string token)
    {
        //Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.addconsole)
        clientBuilder = DiscordClientBuilder.CreateDefault(token, DiscordIntents.GuildMessages | DiscordIntents.MessageContents | DiscordIntents.GuildMessageReactions | DiscordIntents.DirectMessageReactions | DiscordIntents.Guilds | DiscordIntents.GuildMembers);
        clientBuilder.ConfigureEventHandlers(e =>
        {
            e.HandleGuildDownloadCompleted(GetGuildResources)
                .HandleSessionCreated(Ready)
                .HandleMessageCreated(MessageCreated)
                .HandleMessageUpdated(MessageUpdated)
                .HandleChannelCreated(ChannelCreated)
                .HandleThreadCreated(ThreadCreated)
                .HandleUnknownEvent((c, a) => Task.CompletedTask);
        });
        
        Relauncher.RelaunchParameters.SetupProcessStartMessage(Environment.GetCommandLineArgs(), clientBuilder);
        //clientBuilder.SetLogLevel(LogLevel.Trace);
        //clientBuilder.ConfigureGatewayClient(c => c.GatewayCompressionLevel = GatewayCompressionLevel.None);
        clientBuilder.ConfigureServices(x => x.AddLogging(y => y.AddConsole(clo => clo.LogToStandardErrorThreshold = LogLevel.Warning)));
        blockers =
        [
            new ModeratorIgnore(this),
            new Reslow(this),
            new CustomEmojisAndStickers(this),
            new FlagRestriction(this),
            new MustStartWith(this),
            new WordPercentage(this),
            new NoVowels(this),
            new SheOnMyTill(this),
            new Haiku(this),
            new WikiTopic(this),
        ];
        casino = new(this);
        hangman = new(this);
        frogRole = new(this);
        confessions = new(this);
        stargrid = new(this);
        msgBuffer = new(this);
        imageRoyale = new(this);
        videoRoyale = new(this);
        stickyMessages = new(this);
        // clientBuilder.ConfigureServices(x => x.AddSingleton(typeof(StickyMessages), stickyMessages));
        
        clientBuilder.ConfigureServices(x => x.AddSingleton(typeof(BoneBot), this));
        SlashCommandProcessor scp = new();
        MessageCommandProcessor mcp = new();

        
        
        Type[] commandTypes = new[] { typeof(SlashCommands) }
            .Concat(ModuleBase.AllModules.Select(m => m.GetType())
            .Where(t => t.GetCustomAttribute<CommandAttribute>() is not null))
            .ToArray();

        // doesnt matter anyway lol D#+ creates a new instance for every invocation, even on static commands
        // for (int i = commandTypes.Length - 1; i >= 0; i--)
        // {
        //     Type type =  commandTypes[i];
        //     var instanceCommands = type.GetMethods().Where(m => !m.IsStatic && m.GetCustomAttribute<CommandAttribute>() != null);
        //     foreach (var cmd in instanceCommands)
        //     {
        //         Logger.Warn($"DICKHEAD!!!! DONT MAKE INSTANCED COMMANDS!!!!! Offender: {type.FullName ?? "<Anonymous type>"}.{cmd.Name}");
        //     }
        // }
        
        // IEnumerable<CommandBuilder> commandBuilders = new[] { typeof(SlashCommands) }
        //     .Concat(ModuleBase.AllModules.Select(m => m.GetType())
        //         .Where(t => t.GetCustomAttribute<CommandAttribute>() is not null))
        //     .Select(CommandBuilder.From);
        //
        // clientBuilder.ConfigureEventHandlers(x =>
        //     /* Discord's docs state:
        //      * "When connecting to the gateway as a bot user, guilds that the bot is a part of will start out as
        //      * unavailable. [...] As guilds become available to you, you will receive Guild Create events."
        //      */
        //     x.HandleGuildCreated(async (client, args) =>
        //     {
        //         foreach (var command in commandBuilders.SelectMany(cb => cb.Flatten()).Select(cb => cb.Build()))
        //         {
        //             args.Guild.CreateApplicationCommandAsync(command);
        //                 
        //         }
        //         args.Guild.BulkOverwriteApplicationCommandsAsync()
        //     })
        // );
        //
        // DSharpPlus.Commands.Trees.CommandBuilder.From()
        clientBuilder.UseCommands((isp, ce) =>
        {
            ce.AddProcessors(scp, mcp);
            ce.AddCommands(commandTypes);
            ce.CommandErrored += CommandErrorHandler;
        }, new()
        {
            RegisterDefaultCommandProcessors = false,
            UseDefaultCommandErrorHandler = false, // annoying fuck
            
        });

        foreach (var module in ModuleBase.AllModules)
        {
            module.ConfigureEventHandlers();
        }
        
        client = clientBuilder.Build();

        Bots.Add(client, this);
    }

    private Task ThreadCreated(DiscordClient client, ThreadCreatedEventArgs args)
    {
        if (!allChannels.TryGetValue(args.Guild, out var allChannelsSlice))
            allChannelsSlice = allChannels[args.Guild] = new();

        allChannelsSlice.Add(args.Thread);
        return Task.CompletedTask;
    }

    private Task ChannelCreated(DiscordClient client, ChannelCreatedEventArgs args)
    {
        if (!allChannels.TryGetValue(args.Guild, out var allChannelsSlice))
            allChannelsSlice = allChannels[args.Guild] = new();

        allChannelsSlice.Add(args.Channel);
        return Task.CompletedTask;
    }

    private async Task CommandErrorHandler(CommandsExtension sender, DSharpPlus.Commands.EventArgs.CommandErroredEventArgs args)
    {
        int randomNumber = Random.Shared.Next();
        Logger.Error($" [{randomNumber}] Exception while executing command on command object {args.CommandObject}", args.Exception);
        string userResponse = $"Exception while running your command! Tell the host/developer to look for {randomNumber} in the log!```\n{Logger.EnsureShorterThan(args.Exception.ToString(), 1750, "\n[cut off for Discord]")}```";


        if (args.Context is SlashCommandContext sctx)
        {
            switch (sctx.Interaction.ResponseState)
            {
                case DiscordInteractionResponseState.Unacknowledged:
                    {
                        await sctx.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral().WithContent(userResponse));
                    }
                    break;
                case DiscordInteractionResponseState.Replied:
                    {
                        await sctx.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(userResponse));
                    }
                    break;
                case DiscordInteractionResponseState.Deferred:
                    {
                        await sctx.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(userResponse));
                    }
                    break;
            }
        }
        else
            await args.Context.RespondAsync(userResponse);

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
    }

    private async Task GetGuildResources(DiscordClient client, GuildDownloadCompletedEventArgs args)
    {
        foreach (var channelKvp in args.Guilds.Values.SelectMany(dg => dg.Channels))
        {
            if (!allChannels.TryGetValue(channelKvp.Value.Guild, out var allChannelsSlice))
                allChannelsSlice = allChannels[channelKvp.Value.Guild] = new();

            allChannelsSlice.Add(channelKvp.Value);
            if (channelKvp.Value.Type is DiscordChannelType.Text or DiscordChannelType.GuildForum or DiscordChannelType.GuildMedia or DiscordChannelType.News)
            {
                foreach (DiscordThreadChannel thread in channelKvp.Value.Threads)
                {
                    allChannelsSlice.Add(thread);
                }
            }

            if (channelKvp.Key == Config.values.logChannel)
                logChannel = channelKvp.Value;
            else
            {
                if (channelKvp.Value.Type != DiscordChannelType.Text) continue;

                foreach (DiscordThreadChannel thread in channelKvp.Value.Threads)
                {
                    if (thread.Id == Config.values.logChannel)
                        logChannel = thread;
                }
            }
        }

        allChannelsReceived?.InvokeActionSafe(allChannels);
        calledAllChannelsReceived = true;
    }

    async Task Ready(DiscordClient client, SessionCreatedEventArgs args)
    {
        Logger.Put($"Logged in on user {User.Username}#{User.Discriminator} (ID {User.Id})");
    }

    #region Message

    private async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (args.Message.Author is null || Config.values.blockedUsers.Contains(args.Message.Author.Id))
            return;
        DiscordMember? member = args.Author as DiscordMember;
        bool hasManageMessages = member is not null && member.Permissions.HasPermission(DiscordPermission.ManageMessages);

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
        

        
    }

    private async Task MessageUpdated(DiscordClient sender, MessageUpdatedEventArgs args)
    {
        if (args.Author?.IsBot ?? true)
            return;
        DiscordMember? member = args.Author as DiscordMember;
        bool hasManageMessages = member is not null && member.Permissions.HasPermission(DiscordPermission.ManageMessages);

        if (!hasManageMessages && Config.values.channelsWhereNoVowelsAreAllowed.Contains(args.Channel.Id) && args.Message.Content.Any(c => "aeiou".Contains(c, StringComparison.InvariantCultureIgnoreCase)))
        {
            await args.Message.DeleteAsync("has vowels. cnat edit to do that., u tried'");
        }
    }

    #endregion

    internal bool IsMe(DiscordUser? user) => user is not null && user == User;

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

        if (calledAllChannelsReceived)
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
}
