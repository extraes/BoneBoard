using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules;

internal partial class ModuleBase
{
    [DebuggerStepThrough]
    protected async Task<bool> TryDeleteAsync(DiscordMessage msg, string? reason = null)
    {
        try
        {
            await msg.DeleteAsync(reason);
            return true;
        }
        catch (DiscordException ex)
        {
            //could possibly return it?
            Logger.Warn($"{GetType().Name} failed to delete message {msg}\n\t{ex}");
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
            Logger.Warn($"{GetType().Name} failed to delete {reaction} reaction from {user} on message {msg}\n\t{ex}");
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

    protected DiscordUser? GetUser(DiscordEventArgs args)
    {
        dynamic dynargs = args;
        try
        {
            return dynargs.User;
        }
        catch { }

        try
        {
            return dynargs.Member;
        }
        catch { }


        try
        {
            return dynargs.Author;
        }
        catch { }

        try
        {
            return dynargs.UserAfter;
        }
        catch { }

        return null;
    }
}
