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

namespace BoneBoard.Modules;

internal abstract partial class ModuleBase
{

    #region Statics

    protected const bool STOP_EVENT_PROPAGATION = true;
    protected const bool DONT_STOP_EVENT_PROPAGATION = true;
    
    //todo: HANDLE EVENT DISPATCHING MYSELF
    private const int EVENT_BLOCKED = -1;

    private static List<ModuleBase> allBlockers = [];

    private static Dictionary<DiscordEventArgs, int> eventsRanThruBlockers = new();

    static ModuleBase()
    {
        Config.ConfigChanged += () => Task.Run(ConfigChanged);
    }

    static async Task ConfigChanged()
    {
        foreach (ModuleBase module in AllModules)
        {
            // refresh cached config values
            await module.FetchGuildResources();
        }
    }

    internal static readonly List<ModuleBase> AllModules = new();
    static readonly CircularBuffer<DiscordEventArgs> DontPropagate = new(4096);

    public static void DontPropagateEvent(DiscordEventArgs args)
    {
        DontPropagate.PushBack(args);
    }
    #endregion

    protected BoneBot bot { get; private set; }
    bool inited;

    public ModuleBase(BoneBot bot) : this()
    {
        this.bot = bot;
        AllModules.Add(this);
        bot.clientBuilder.ConfigureServices(x => x.AddSingleton(this.GetType(), this));
        bot.ConfigureEvents(x => x.HandleGuildDownloadCompleted(CheckAndInit));

        bot.ConfigureEvents(x => x
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

    private Task AllEventsHandler(DiscordClient client, DiscordEventArgs args)
    {
        // if a previous handler already stopped propagation, don't bother
        if (DontPropagate.Contains(args))
            return Task.CompletedTask;

        bool needStop = false;

        // quick filtering for blocked users
        if (args is MessageCreatedEventArgs msgCreatedArgs)
            needStop = needStop || Config.values.blockedUsers.Contains(msgCreatedArgs.Author.Id);
        else if (args is MessageUpdatedEventArgs msgUpdatedArgs)
            needStop = needStop || Config.values.blockedUsers.Contains(msgUpdatedArgs.Author.Id);
        else if (args is MessageReactionAddedEventArgs rxnArgs)
            needStop = needStop || Config.values.blockedUsers.Contains(rxnArgs.User.Id);

        needStop = needStop || GlobalStopEventPropagation(args);

        if (needStop)
        {
            Logger.Put($"Event type {args.GetType().Name} was blocked from propagation by {GetType().Name}");
            DontPropagate.PushBack(args);
        }
        
        return Task.CompletedTask;
    }

    private MethodInfo InstanceMethod(Delegate target, [CallerArgumentExpression(nameof(target))] string methodName = "") => GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                                                                                                                                ?? throw new MissingMethodException(methodName);

    public void ConfigureEventHandlers()
    {
        bool hasBlocker = InstanceMethod(GlobalStopEventPropagation).DeclaringType == GetType();
        var handlersThatNeedRegistering = new List<(bool needsRegister, Action<EventHandlingBuilder> builder, string eventName)>
        {
            (InstanceMethod(GuildDownloadCompleted).DeclaringType == GetType()  , builder => builder.HandleGuildDownloadCompleted(GuildDownloadCompletedEvent), "GuildDownloadCompleted"),
            (InstanceMethod(MessageCreated).DeclaringType == GetType()          , builder => builder.HandleMessageCreated(MessageCreatedEvent)                , "MessageCreated"),
            (InstanceMethod(MessageUpdated).DeclaringType == GetType()          , builder => builder.HandleMessageUpdated(MessageUpdatedEvent)                , "MessageUpdated"),
            (InstanceMethod(ReactionAdded).DeclaringType == GetType()           , builder => builder.HandleMessageReactionAdded(ReactionAddedEvent)           , "ReactionAdded"),
            (InstanceMethod(ChannelCreated).DeclaringType == GetType()          , builder => builder.HandleChannelCreated(ChannelCreatedEvent)                , "ChannelCreated"),
            (InstanceMethod(ThreadCreated).DeclaringType == GetType()           , builder => builder.HandleThreadCreated(ThreadCreatedEvent)                  , "ThreadCreated"),
            (InstanceMethod(SessionCreated).DeclaringType == GetType()          , builder => builder.HandleSessionCreated(SessionCreatedEvent)                , "SessionCreated"),
            (InstanceMethod(UnknownEvent).DeclaringType == GetType()            , builder => builder.HandleUnknownEvent(UnknownEventEvent)                    , "UnknownEvent"),
        };

        Logger.Put($"Now registering event handlers for {GetType().Name}", LogType.Debug);
        foreach (var (needsRegister, builder, name) in handlersThatNeedRegistering)
        {
            if (needsRegister)
            {
                bot.ConfigureEvents(builder);
                Logger.Put($" * Registering event {name}...", LogType.Debug);
            }
        }

        Logger.Put($"Configured event handlers for {GetType().Name}, adding {handlersThatNeedRegistering.Count(x => x.needsRegister)} events{(hasBlocker ? " in addition to its blocker" : "")}.");
        
    }

    // actual API shit that children are gonna use
    protected virtual bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        return false;
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
