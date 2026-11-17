using DSharpPlus.Exceptions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace BoneBoard.Modules;

public partial class ModuleBase
{
    [DebuggerStepThrough]
    protected async void TryDeleteDontCare(DiscordMessage? msg, string? reason = null)
    {
        try
        {
            if (msg is null)
                return;
            
            if (reason is not null)
                Logger.Put($"Deleting message {msg} for reason '{reason}'");
            await msg.DeleteAsync(reason);
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete message (DiscordException) {msg}", ex);
        }
        catch (Exception e)
        {
            Logger.Warn($"{GetType().Name} failed to delete message (Unknown exception) {msg}", e);
        }
    }
    
    [DebuggerStepThrough]
    protected async void TryDeleteDontCare(DiscordMessage? msg, DiscordUser? user, DiscordEmoji? emoji, string? reason = null)
    {
        try
        {
            if (msg is null || user is null || emoji is null)
                return;
            if (reason is not null)
                Logger.Put($"Deleting {emoji.GetDiscordName()} reaction from {user} on {msg} for reason '{reason}'");
            
            await msg.DeleteReactionAsync(emoji, user, reason);
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete message (DiscordException) {msg}", ex);
        }
        catch (Exception e)
        {
            Logger.Warn($"{GetType().Name} failed to delete message (Unknown exception) {msg}", e);
        }
    }
    
    [DebuggerStepThrough]
    protected async Task<bool> TryDeleteAsync(DiscordMessage? msg, string? reason = null)
    {
        try
        {
            if (msg is null)
            {
                Logger.Warn("Can't delete a null message");
                return false;
            }
            
            if (reason is not null)
                Logger.Put($"Deleting message {msg} for reason '{reason}'");
            await msg.DeleteAsync(reason);
            return true;
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete message {msg}", ex);
            return false;
        }
    }

    [DebuggerStepThrough]
    protected async Task<bool> TryDeleteAsync(DiscordMessage msg, DiscordEmoji reaction, DiscordUser user, string? reason = null)
    {
        try
        {
            await msg.DeleteReactionAsync(reaction, user, reason);
            return true;
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete {reaction} reaction from {user} on message {msg}", ex);
            return false;
        }
    }
    
    [DebuggerStepThrough]
    protected async Task<bool> TryReact(DiscordMessage msg, DiscordEmoji reaction)
    {
        try
        {
            await msg.CreateReactionAsync(reaction);
            return true;
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to react with {reaction} to message {msg}", ex);
            return false;
        }
    }

    [DebuggerStepThrough]
    protected async Task<DiscordMessage?> TryFetchMessage(DiscordChannel channel, ulong id, bool skipCache = false)
    {
        try
        {
            return await channel.GetMessageAsync(id, skipCache);
        }
        catch
        {
            Logger.Put($"Ignore the above log, {channel} had no message with the ID {id}", LogType.Debug);
            return null;
        }
    }

    [SuppressMessage("ReSharper", "EmptyGeneralCatchClause")]
    protected DiscordGuild? GetGuild(DiscordEventArgs args)
    {
        dynamic dynargs = args;

        try
        {
            return dynargs.Guild;
        }
        catch { }

        try
        {
            return dynargs.GuildAfter;
        }
        catch { }

        try
        {
            return dynargs.Interaction.Guild;
        }
        catch { }

        try
        {
            return dynargs.Rule.Guild;
        }
        catch { }

        try
        {
            return dynargs.Thread.Guild;
        }
        catch { }

        try
        {
            return dynargs.Event.Guild;
        }
        catch { }

        try
        {
            return dynargs.EventAfter.Guild;
        }
        catch { }

        try
        {
            return dynargs.PollVoteUpdate.Guild;
        }
        catch { }

        try
        {
            return dynargs.PresenceAfter.Guild;
        }
        catch { }

        return null;
    }

    protected enum EventUserKind
    {
        Associated,
        Actor,
        Subject
    }

    // Associated preserves the original meaning. Actor/Subject opt into an explicit role.
    // A missing actor (for example, the moderator behind a ban) is never inferred.
    protected DiscordUser? GetUser(DiscordEventArgs args, EventUserKind kind = EventUserKind.Associated)
    {
        if (args is ContextMenuInteractionCreatedEventArgs context)
            return kind == EventUserKind.Subject
                ? context.TargetUser
                : context.User;

        if (args is GuildAuditLogCreatedEventArgs audit && kind != EventUserKind.Subject)
            return audit.AuditLogEntry.UserResponsible;

        if (kind == EventUserKind.Actor &&
            args is GuildBanAddedEventArgs
            or GuildBanRemovedEventArgs
            or GuildMemberAddedEventArgs
            or GuildMemberRemovedEventArgs
            or GuildMemberUpdatedEventArgs
            or ThreadMemberUpdatedEventArgs
            or VoiceStateUpdatedEventArgs
            or MessageDeletedEventArgs
            or MessageReactionRemovedEventArgs
            or MessageReactionRemovedEmojiEventArgs
            or MessageReactionsClearedEventArgs)
            return null;

        if (args is VoiceStateUpdatedEventArgs voice)
        {
            // ReSharper disable once ConvertTypeCheckPatternToNullCheck
            if (voice.GuildId is ulong guildId &&
                bot.client.Guilds.TryGetValue(guildId, out var guild) &&
                guild.Members.TryGetValue(voice.UserId, out var member))
                return member;
            return null;
        }

        dynamic dynArgs = args;

        return Read(() => dynArgs.User)
               ?? Read(() => dynArgs.Member)
               ?? Read(() => dynArgs.Author)
               ?? Read(() => dynArgs.UserAfter)
               ?? Read(() => dynArgs.MemberAfter)
               ?? Read(() => dynArgs.Interaction.User)
               ?? Read(() => dynArgs.PollVoteUpdate.User)
               ?? Read(() => dynArgs.Creator)
               ?? Read(() => dynArgs.ThreadMember.Member)
               ?? Read(() => dynArgs.Message.Author);

        DiscordUser? Read(Func<DiscordUser?> read)
        {
            try
            {
                return read();
            }
            catch { return null; }
        }
    }
}
