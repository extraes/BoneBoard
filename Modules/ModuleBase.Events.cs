using DSharpPlus;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules;

// done to avoid this reflection shit being in scope everywhere
file static class ReflectionHelper
{
    const BindingFlags EVENT_FLAGS = BindingFlags.Instance | BindingFlags.NonPublic;

    public static MethodInfo GetMethod(string name) => typeof(ModuleBase).GetMethod(name, EVENT_FLAGS) ?? throw new MissingMethodException(name);
}

internal abstract partial class ModuleBase
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    // bot is set from the public ctor
    private ModuleBase()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
        GuildDownloadCompletedEvent =   async (c, a) => { if (!DontPropagate.Contains(a)) await GuildDownloadCompleted(c, a);         };
        MessageCreatedEvent =           async (c, a) => { if (!DontPropagate.Contains(a)) await MessageCreated(c, a);         };
        MessageUpdatedEvent =           async (c, a) => { if (!DontPropagate.Contains(a)) await MessageUpdated(c, a);         };
        ReactionAddedEvent =            async (c, a) => { if (!DontPropagate.Contains(a)) await ReactionAdded(c, a);          };
        ReactionRemovedEvent =          async (c, a) => { if (!DontPropagate.Contains(a)) await ReactionRemoved(c, a);        };
        GuildMemberUpdatedEvent =       async (c, a) => { if (!DontPropagate.Contains(a)) await GuildMemberUpdated(c, a);     };
        ChannelCreatedEvent =           async (c, a) => { if (!DontPropagate.Contains(a)) await ChannelCreated(c, a);         };
        ThreadCreatedEvent =            async (c, a) => { if (!DontPropagate.Contains(a)) await ThreadCreated(c, a);          };
        SessionCreatedEvent =           async (c, a) => { if (!DontPropagate.Contains(a)) await SessionCreated(c, a);         };
        UnknownEventEvent =             async (c, a) => { if (!DontPropagate.Contains(a)) await UnknownEvent(c, a);           };
    }

    static readonly MethodInfo BaseGuildDownloadCompleted = ReflectionHelper.GetMethod(nameof(GuildDownloadCompleted));
    private Func<DiscordClient, GuildDownloadCompletedEventArgs, Task> GuildDownloadCompletedEvent;
    protected virtual Task GuildDownloadCompleted(DiscordClient client, GuildDownloadCompletedEventArgs args) => Task.CompletedTask;

    static readonly MethodInfo BaseMessageCreated = ReflectionHelper.GetMethod(nameof(MessageCreated));
    private Func<DiscordClient, MessageCreatedEventArgs, Task> MessageCreatedEvent;
    protected virtual Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args) => Task.CompletedTask;
    
    static readonly MethodInfo BaseMessageUpdated = ReflectionHelper.GetMethod(nameof(MessageUpdated));
    private Func<DiscordClient, MessageUpdatedEventArgs, Task> MessageUpdatedEvent;
    protected virtual Task MessageUpdated(DiscordClient client, MessageUpdatedEventArgs args) => Task.CompletedTask;
    
    static readonly MethodInfo BaseReactionAdded = ReflectionHelper.GetMethod(nameof(ReactionAdded));
    private Func<DiscordClient, MessageReactionAddedEventArgs, Task> ReactionAddedEvent;
    protected virtual Task ReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args) => Task.CompletedTask;
    
    static readonly MethodInfo BaseReactionRemoved = ReflectionHelper.GetMethod(nameof(ReactionRemoved));
    private Func<DiscordClient, MessageReactionRemovedEventArgs, Task> ReactionRemovedEvent;
    protected virtual Task ReactionRemoved(DiscordClient client, MessageReactionRemovedEventArgs args) => Task.CompletedTask;
    
    static readonly MethodInfo BaseGuildMemberUpdated = ReflectionHelper.GetMethod(nameof(GuildMemberUpdated));
    private Func<DiscordClient, GuildMemberUpdatedEventArgs, Task> GuildMemberUpdatedEvent;
    protected virtual Task GuildMemberUpdated(DiscordClient client, GuildMemberUpdatedEventArgs args) => Task.CompletedTask;

    static readonly MethodInfo BaseChannelCreated = ReflectionHelper.GetMethod(nameof(ChannelCreated));
    private Func<DiscordClient, ChannelCreatedEventArgs, Task> ChannelCreatedEvent;
    protected virtual Task ChannelCreated(DiscordClient client, ChannelCreatedEventArgs args) => Task.CompletedTask;

    static readonly MethodInfo BaseThreadCreated = ReflectionHelper.GetMethod(nameof(ThreadCreated));
    private Func<DiscordClient, ThreadCreatedEventArgs, Task> ThreadCreatedEvent;
    protected virtual Task ThreadCreated(DiscordClient client, ThreadCreatedEventArgs args) => Task.CompletedTask;

    static readonly MethodInfo BaseSessionCreated = ReflectionHelper.GetMethod(nameof(SessionCreated));
    private Func<DiscordClient, SessionCreatedEventArgs, Task> SessionCreatedEvent;
    protected virtual Task SessionCreated(DiscordClient client, SessionCreatedEventArgs args) => Task.CompletedTask;

    static readonly MethodInfo BaseUnknownEvent = ReflectionHelper.GetMethod(nameof(UnknownEvent));
    private Func<DiscordClient, UnknownEventArgs, Task> UnknownEventEvent;
    protected virtual Task UnknownEvent(DiscordClient client, UnknownEventArgs args) => Task.CompletedTask;
}
