using CircularBuffer;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Skeleton;

public abstract partial class SkModuleBase
{

    #region Statics

    protected const bool BLOCK_PROPAGATION = true;
    protected const bool ALLOW_PROPAGATION = false;

    /// <summary>
    /// Hook <see cref="GlobalConfigBase{T}.OnConfigChanged"/> to this.
    /// </summary>
    public static void FetchOnAllModules()
    {
        Task.Run(ConfigChanged);
    }
    
    static async Task ConfigChanged()
    {
        foreach (SkModuleBase module in AllModules)
        {
            // refresh cached config values
            await module.FetchGuildResources();
        }
    }

    internal static readonly List<SkModuleBase> AllModules = new();
    static readonly CircularBuffer<DiscordEventArgs> DontPropagate = new(4096);

    public static void DontPropagateEvent(DiscordEventArgs args)
    {
        DontPropagate.PushBack(args);
    }
    #endregion

    protected ISklient bot { get; private set; }
    bool inited;

    public SkModuleBase(ISklient bot) : this()
    {
        var builder = bot.GetClientBuilder();
        if (builder is null)
            throw new ArgumentException("You should not be adding modules after connecting to Discord!");
        
        this.bot = bot;
        AllModules.Add(this);
        builder.ConfigureServices(x => x.AddSingleton(this.GetType(), this));
        builder.ConfigureEventHandlers(x => x.HandleGuildDownloadCompleted(CheckAndInit));

        builder.ConfigureEventHandlers(x => x
            .HandleGuildDownloadCompleted(AllEventsHandler)
            .HandleMessageCreated(AllEventsHandler)
            .HandleMessageUpdated(AllEventsHandler)
            .HandleMessageReactionAdded(AllEventsHandler)
            .HandleChannelCreated(AllEventsHandler)
            .HandleThreadCreated(AllEventsHandler)
            .HandleSessionCreated(AllEventsHandler)
            .HandleUnknownEvent(AllEventsHandler));

        //if (GetType().GetCustomAttribute<CommandAttribute>() is not null)
        //    bot.clientBuilder.UseCommands(ce => ce.AddCommands(GetType()));
    }

    private async Task CheckAndInit(DiscordClient client, GuildDownloadCompletedEventArgs args)
    {
        await FetchGuildResources();
        if (inited)
            return;

        await InitOneShot(args);
        inited = true;
    }

    private async Task AllEventsHandler(DiscordClient client, DiscordEventArgs args)
    {
        // if a previous handler already stopped propagation, don't bother
        if (DontPropagate.Contains(args))
            return;

        bool needStop = await GlobalStopEventPropagation(args);

        if (needStop)
        {
            Logger.Put($"Event type {args.GetType().Name} was blocked from propagation by {GetType().Name}");
            DontPropagate.PushBack(args);
        }
    }
    
    // ReSharper disable once EntityNameCapturedOnly.Local
    private MethodInfo InstanceMethod(Delegate target, [CallerArgumentExpression(nameof(target))] string methodName = "")
        => GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(methodName);

    public void ConfigureEventHandlers()
    {
        var builder = bot.GetClientBuilder();
        if (builder is null)
            throw new InvalidOperationException("You should not be adding modules after connecting to Discord!");
        
        bool hasBlocker = InstanceMethod(GlobalStopEventPropagation).DeclaringType == GetType();
        var handlersThatNeedRegistering = new List<(bool needsRegister, Action<EventHandlingBuilder> builder)>
        {
            (InstanceMethod(GuildDownloadCompleted).DeclaringType == GetType()  , x => x.HandleGuildDownloadCompleted(GuildDownloadCompletedEvent)),
            (InstanceMethod(MessageCreated).DeclaringType == GetType()          , x => x.HandleMessageCreated(MessageCreatedEvent)                ),
            (InstanceMethod(MessageUpdated).DeclaringType == GetType()          , x => x.HandleMessageUpdated(MessageUpdatedEvent)                ),
            (InstanceMethod(ReactionAdded).DeclaringType == GetType()           , x => x.HandleMessageReactionAdded(ReactionAddedEvent)           ),
            (InstanceMethod(ChannelCreated).DeclaringType == GetType()          , x => x.HandleChannelCreated(ChannelCreatedEvent)                ),
            (InstanceMethod(ThreadCreated).DeclaringType == GetType()           , x => x.HandleThreadCreated(ThreadCreatedEvent)                  ),
            (InstanceMethod(SessionCreated).DeclaringType == GetType()          , x => x.HandleSessionCreated(SessionCreatedEvent)                ),
            (InstanceMethod(UnknownEvent).DeclaringType == GetType()            , x => x.HandleUnknownEvent(UnknownEventEvent)                    ),
        };

        foreach (var (needsRegister, eventRegisterer) in handlersThatNeedRegistering)
        {
            if (needsRegister)
            {
                builder.ConfigureEventHandlers(eventRegisterer);
            }
        }

        Logger.Put($"Configured event handlers for {GetType().Name}, adding {handlersThatNeedRegistering.Count(x => x.needsRegister)} events{(hasBlocker ? " in addition to its blocker" : "")}.");
    }


    // actual API shit that children are gonna use
    public void NotifyMeWhenCfgChanges<TConfig>() where TConfig : PerServerConfigBase
    {
        var filterFor = typeof(TConfig);
        PerServerConfigBase.OnAnyConfigChanged += (cfg) =>
        {
            if (cfg.GetType() != filterFor)
                return;
            Task.Run(FetchGuildResources);
        };
    }
    
    protected virtual Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        return Task.FromResult(false);
    }

    protected virtual Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        return Task.CompletedTask;
    }

    protected virtual Task FetchGuildResources()
    {
        return Task.CompletedTask;
    }
}
