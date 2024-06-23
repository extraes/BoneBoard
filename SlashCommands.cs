using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Commands.Processors.SlashCommands;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using DSharpPlus.Commands.ContextChecks;

namespace BoneBoard;

[Command("star")]
internal class SlashCommands
{
    public const DiscordPermissions MDOERATOR_PERMS = DiscordPermissions.ManageRoles | DiscordPermissions.ManageMessages;

    public static async Task<bool> ModGuard(SlashCommandContext ctx, bool ownerOnly = false)
    {
        if (ctx.Member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return true;
        }

        if (!ctx.Member.Permissions.HasPermission(MDOERATOR_PERMS))
        {
            await ctx.RespondAsync("nuh uh", true);
            return true;
        }

        if (ownerOnly && !Config.values.owners.Contains(ctx.Member.Id))
        {
            await ctx.RespondAsync("nop", true);
            return true;
        }

        return false;
    }

    [Command("createFrogMsg")]
    [Description("Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [RequirePermissions(DiscordPermissions.AddReactions | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    public static async Task CreateFrogMessage(
        SlashCommandContext ctx,
        [Parameter("msgContent")]
        [Description("The text content of the message. Use {0} to for curr king.")]
        string content
        )
    {
        if (await ModGuard(ctx))
            return;

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "(there'll be a frog haver here)"));

        if (Config.values.frogRoleActivation == FrogRoleActivation.REACTION)
            await msg.CreateReactionAsync(DiscordEmoji.FromUnicode("🐸"));

        Config.values.frogMessageLink = msg.JumpLink.OriginalString;
        Config.values.frogMessageBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].FetchFrogMessage();

        await ctx.RespondAsync("Created KOTF message!", true);
    }

    [Command("setFrogUnavailableText")]
    [Description("Set the text that will be displayed when someone tries to get the frog role when it's not available.")]
    [RequirePermissions(DiscordPermissions.AddReactions | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    public static async Task SetFrogUnavailableText(
        SlashCommandContext ctx,
        [Parameter("msgContent")]
        [Description("The text content of the message. Use {0} to for curr king, and {1} available day of week.")]
        string content
        )
    {
        if (await ModGuard(ctx))
            return;

        Config.values.frogMessageClosedBase = content;
        Config.WriteConfig();

        await ctx.RespondAsync("Set frog unavailable text!", true);
    }

    [Command("createFrogLeaderboardMsg")]
    [Description("Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [RequirePermissions(DiscordPermissions.AddReactions | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    public static async Task CreateFrogLeaderboardMsg(
        SlashCommandContext ctx,
        [Parameter("msgContent")]
        [Description("Use {0} to indicate where the leaderboard goes (Will be surrounded by multilines)")]
        string content
       )
    {
        if (await ModGuard(ctx))
            return;

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "\n(There'll be a leaderboard here...)\n"));

        Config.values.frogLeaderboardLink = msg.JumpLink.OriginalString;
        Config.values.frogLeaderboardBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].FetchFrogLeaderboardMsg();

        await ctx.RespondAsync("Created KOTF leaderboard message! You may need to delete the old one!", true);
    }


    [Command("removeUserFromLeaderboard")]
    [Description("Remove user from frog role leaderboard. De-role/block/ban them before use.")]
    [RequirePermissions(DiscordPermissions.AddReactions | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    public static async Task RemoveUserFromLeaderboard(
        SlashCommandContext ctx,
        [Parameter("user")]
        [Description("Whose ID to remove from the leaderboard. Won't trigger leaderboard refresh.")]
        DiscordUser? user = null,
        [Parameter("userId")]
        [Description("The ID to remove from the leaderboard. Won't trigger leaderboard refresh.")]
        long? userId = null
       )
    {
        if (await ModGuard(ctx))
            return;

        ulong? id = (ulong?)userId ?? user?.Id;
        if (!id.HasValue)
        {
            await ctx.RespondAsync("care to elaborate on whos gettin the ax?", true);
            return;
        }

        bool wasInData = PersistentData.values.frogRoleTimes.Remove(id.Value);
        PersistentData.WritePersistentData();

        await ctx.RespondAsync(wasInData ? "Removed that user from the frog leaderboard." : "That user wasn't in the leaderboard (yet?).", true);
    }


    [Command("reloadCfg")]
    [Description("Reloads the config. This may not have any impact on things that are cached at startup.")]
    [RequirePermissions(DiscordPermissions.AddReactions | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    public static async Task ReloadConfig(
        SlashCommandContext ctx
       )
    {
        if (await ModGuard(ctx))
            return;

        Config.ReadConfig();

        await ctx.RespondAsync("Read config!", true);
    }


    [Command("confess")]
    [Description("confess your sins my child")]
    public static async Task SendConfessional(
        SlashCommandContext ctx,
        [Parameter("message")] string text
       )
    {
        if (ctx.Member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        DiscordMessage? msg = await BoneBot.Bots[ctx.Client].SendConfessional(ctx.Member, text);

        if (msg is null)
        {
            await ctx.RespondAsync("There was either an error or you tried sending a message before your 6 hours are up. Or you dont have the required role. LOL.", true);
            return;
        }

        await ctx.RespondAsync("Confessed successfully. May the holy spirit cleanse you of your sins.", true);
    }


    [Command("setBufferedChannel"), Description("Toggle whether this channel is un/buffered")]
    [RequirePermissions(DiscordPermissions.None, MDOERATOR_PERMS)]
    public static async Task SetBufferedChannel(
        SlashCommandContext ctx,
        [Parameter("add"), Description("True to add, false to remove.")] bool add,
        [Parameter("bufferMessageText"), Description("{0} will be replaced by timestamp.")] string bufferMessage = ""
        )
    {
        if (await ModGuard(ctx))
            return;

        ulong channelId = ctx.Channel.Id;

        if (add)
        {
            if (PersistentData.values.bufferedChannels.Contains(channelId))
            {
                await ctx.RespondAsync("This channel is already buffered.", true);
                return;
            }
            if (string.IsNullOrWhiteSpace(bufferMessage))
            {
                await ctx.RespondAsync("You know... if you want to make a channel buffered, it's best if the people know that it's buffered.", true);
                return;
            }

            string timestamp = "(a yet unknown time)";
            PersistentData.values.bufferedChannels.Add(channelId);
            DiscordMessage msg = await ctx.Channel.SendMessageAsync(string.Format(bufferMessage, timestamp));
            PersistentData.values.bufferChannelMessages[channelId] = msg.Id;
            PersistentData.values.bufferChannelMessageFormats[channelId] = bufferMessage;

            await ctx.RespondAsync("Added channel to buffer list.", true);
        }
        else
        {
            if (!PersistentData.values.bufferedChannels.Contains(channelId))
            {
                await ctx.RespondAsync("This channel isn't buffered.", true);
                return;
            }
            PersistentData.values.bufferedChannels.Remove(channelId);
            PersistentData.values.bufferChannelMessages.Remove(channelId);
            await ctx.RespondAsync("Removed channel from buffer list.", true);
        }

        PersistentData.WritePersistentData();
    }

    [Command("startUnbufferTimer"), Description("Starts the timer to un-buffer messages sent during buffer-time")]
    [RequirePermissions(DiscordPermissions.None, MDOERATOR_PERMS)]
    public static async Task StartUnbufferTimer(SlashCommandContext ctx)
    {
        if (await ModGuard(ctx))
            return;

        BoneBot.Bots[ctx.Client].StartUnbufferTimer();
        string timestamp = Formatter.Timestamp(DateTime.Now.AddMinutes(Config.values.bufferTimeMinutes), TimestampFormat.ShortTime);

        StringBuilder sb = new($"Started unbuffer timer. Check back @ {timestamp}\n");

        foreach (ulong bufferedChannelId in PersistentData.values.bufferedChannels)
        {
            try
            {
                DiscordChannel channel = await ctx.Client.GetChannelAsync(bufferedChannelId);
                DiscordMessage msg = await channel.GetMessageAsync(PersistentData.values.bufferChannelMessages[bufferedChannelId]);
                await msg.ModifyAsync(string.Format(PersistentData.values.bufferChannelMessageFormats[bufferedChannelId], timestamp));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"!!! Error editing channel's buffer notif msg (ID {bufferedChannelId}): {ex.Message}");
            }
        }

        await ctx.RespondAsync(sb.ToString(), true);
    }


    [Command("flushBufferedMessages"), Description("Immediately flushes buffered messages. Doesn't stop the timer.")]
    [RequirePermissions(DiscordPermissions.None, MDOERATOR_PERMS)]
    public static async Task FlushBufferedMessages(SlashCommandContext ctx)
    {
        if (await ModGuard(ctx))
            return;

        await ctx.DeferResponseAsync(true);

        await BoneBot.Bots[ctx.Client].SendBufferedMessages();

        var builder = new DiscordFollowupMessageBuilder().WithContent("Flushed buffered messages in all servers 👍.");
        await ctx.FollowupAsync(builder);
    }
}
