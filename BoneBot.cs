using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
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
using Azure.AI.OpenAI;
using DSharpPlus.Commands.Exceptions;
using DSharpPlus.Commands.Trees;
using OpenAI;

namespace BoneBoard;

public class BoneBot
{

    public static Dictionary<DiscordClient, BoneBot> Bots { get; } = new();

    internal IServiceProvider ServiceProvider;
    
    /// <summary>
    /// Will be null after client creation to avoid silent failures.
    /// </summary>
    internal DiscordClientBuilder? clientBuilder;
    internal DiscordClient client;
    DiscordUser User => client.CurrentUser;

    internal Configured<OpenAIClient?> OpenAI = new(() =>
    {
        if (string.IsNullOrWhiteSpace(Config.values.openAiToken))
            return null;

        if (string.IsNullOrWhiteSpace(Config.values.openAiAltEndpoint))
            return new OpenAIClient(new ApiKeyCredential(Config.values.openAiToken));
        else
            return new AzureOpenAIClient(new Uri(Config.values.openAiAltEndpoint),
                new ApiKeyCredential(Config.values.openAiToken));
    }, () => Config.values.openAiToken + Config.values.openAiAltEndpoint);
    
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
        clientBuilder = DiscordClientBuilder.CreateDefault(token, DiscordIntents.GuildMessages | DiscordIntents.MessageContents | DiscordIntents.GuildMessageReactions | DiscordIntents.DirectMessageReactions | DiscordIntents.Guilds | DiscordIntents.GuildMembers);
        clientBuilder.ConfigureEventHandlers(e =>
        {
            e.HandleGuildDownloadCompleted(GetGuildResources)
                .HandleSessionCreated(Ready)
                .HandleChannelCreated(ChannelCreated)
                .HandleThreadCreated(ThreadCreated)
                .HandleUnknownEvent((c, a) => Task.CompletedTask);
        });
        
        RelaunchParameters.SetupProcessStartMessage(Environment.GetCommandLineArgs(), clientBuilder);
        
        clientBuilder.ConfigureServices(x => x.AddLogging(y => y.AddConsole(clo => clo.LogToStandardErrorThreshold = LogLevel.Warning)));
        CreateModules();
        
        clientBuilder.ConfigureServices(x => x.AddSingleton(this));
        SlashCommandProcessor scp = new();
        MessageCommandProcessor mcp = new();
        
        var commandTypes = new[] { typeof(SlashCommands) }
            .Concat(ModuleBase.AllModules.Select(m => m.GetType())
            .Where(t => t.GetCustomAttribute<CommandAttribute>() is not null))
            .ToArray();
        
        clientBuilder.UseCommands((isp, ce) =>
        {
            ce.AddProcessors(scp, mcp);
            ce.AddCommands(commandTypes);
            ce.CommandErrored += CommandErrorHandler;
        }, new CommandsConfiguration
        {
            RegisterDefaultCommandProcessors = false,
            UseDefaultCommandErrorHandler = false, // annoying fuck
        });

        foreach (var module in ModuleBase.AllModules)
        {
            module.ConfigureEventHandlers();
        }
        
        client = clientBuilder.Build();
        clientBuilder = null;
        ServiceProvider = client.ServiceProvider;

        Bots.Add(client, this);
    }

#pragma warning disable CA1806
    [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
    private void CreateModules()
    {
        // Blockers 
        new ModeratorIgnore(this);
        new PerChannelTimeout(this);
        new Reslow(this);
        new CustomEmojisAndStickers(this);
        new FlagRestriction(this);
        new MustStartWith(this);
        new WordPercentage(this);
        new NoVowels(this);
        new SheOnMyTill(this);
        new Haiku(this);
        new WikiTopic(this);
                
        // Non-blockers
        new Casino(this);
        new Hangman(this);
        new FrogRole(this);
        new Confessional(this);
        new Stargrid(this);
        new MessageBuffer(this);
        new ImageRoyale(this);
        new VideoRoyale(this);
        new StickyMessages(this);
    }
#pragma warning restore CA1806

    private Task ThreadCreated(DiscordClient clint, ThreadCreatedEventArgs args)
    {
        if (!allChannels.TryGetValue(args.Guild, out var allChannelsSlice))
            allChannelsSlice = allChannels[args.Guild] = new HashSet<DiscordChannel>();

        allChannelsSlice.Add(args.Thread);
        return Task.CompletedTask;
    }

    private Task ChannelCreated(DiscordClient clint, ChannelCreatedEventArgs args)
    {
        if (!allChannels.TryGetValue(args.Guild, out var allChannelsSlice))
            allChannelsSlice = allChannels[args.Guild] = new HashSet<DiscordChannel>();

        allChannelsSlice.Add(args.Channel);
        return Task.CompletedTask;
    }

    private async Task CommandErrorHandler(CommandsExtension sender, DSharpPlus.Commands.EventArgs.CommandErroredEventArgs args)
    {
        string userResponse;
        
        // if (args.Exception is AggregateException agEx && agEx.InnerExceptions.Count == 1 &&  agEx.InnerExceptions[0] is )
        if (args.Exception is ChecksFailedException checkEx)
        {
            var errorStrings = checkEx.Errors.Select(d => d.ErrorMessage).Distinct();
            userResponse = $"One or more checks failed:\n{string.Join("\n", errorStrings)}";
        }
        else
        {
            int randomNumber = Random.Shared.Next();
            Logger.Error($" [{randomNumber}] Exception while executing command on command object {args.CommandObject}", args.Exception);
            userResponse = $"Exception while running your command! Tell the host/developer to look for {randomNumber} in the log! (Exception type: {args.Exception.GetType().FullName})" +
                           $"```\n{Logger.EnsureShorterThan(args.Exception.ToString(), 1750, "\n[cut off for Discord]")}```";
        }

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
        if (clientBuilder is null)
            return;
        //throw new InvalidOperationException("Cannot add events after client is built!");

        clientBuilder.ConfigureEventHandlers(action);
    }

    // Don't care about async void warnings. This gets called during init, so if it fails the entire program goes down.
    // ReSharper disable once AsyncVoidMethod
    public async void Init()
    {
        await client.ConnectAsync();
    }

    private async Task GetGuildResources(DiscordClient clint, GuildDownloadCompletedEventArgs args)
    {
        foreach (var channelKvp in args.Guilds.Values.SelectMany(dg => dg.Channels))
        {
            if (!allChannels.TryGetValue(channelKvp.Value.Guild, out var allChannelsSlice))
                allChannelsSlice = allChannels[channelKvp.Value.Guild] = new HashSet<DiscordChannel>();

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

    Task Ready(DiscordClient clint, SessionCreatedEventArgs args)
    {
        Logger.Put($"Logged in on user {User.Username}#{User.Discriminator} (ID {User.Id})");
        return Task.CompletedTask;
    }

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
