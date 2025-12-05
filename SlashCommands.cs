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
    public static readonly DiscordPermissions ModeratorPerms = new DiscordPermissions(DiscordPermission.ManageRoles, DiscordPermission.ManageMessages);

    public static async Task<bool> ModGuard(SlashCommandContext ctx, bool ownerOnly = false)
    {
        if (ctx.Member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return true;
        }

        if (!ctx.Member.Permissions.HasAllPermissions(ModeratorPerms))
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

    [Command("reloadCfg")]
    [Description("Reloads the config. This may not have any impact on things that are cached at startup.")]
    [RequireApplicationOwner]
    public static async Task ReloadConfig(
        SlashCommandContext ctx
       )
    {
        if (await ModGuard(ctx))
            return;

        Config.ReadConfig();

        await ctx.RespondAsync("Read config!", true);
    }


    [Command("setBufferedChannel"), Description("Toggle whether this channel is un/buffered")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
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
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task StartUnbufferTimer(SlashCommandContext ctx)
    {
        if (await ModGuard(ctx))
            return;

        BoneBot.Bots[ctx.Client].msgBuffer.StartUnbufferTimer();
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
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task FlushBufferedMessages(SlashCommandContext ctx)
    {
        if (await ModGuard(ctx))
            return;

        await ctx.DeferResponseAsync(true);

        await BoneBot.Bots[ctx.Client].msgBuffer.SendBufferedMessages();

        var builder = new DiscordFollowupMessageBuilder().WithContent("Flushed buffered messages in all servers 👍.");
        await ctx.FollowupAsync(builder);
    }
    
    [Command("baltop"), Description("Prints out the people with the fattest pockets.")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task BalTop(SlashCommandContext ctx)
    {
        if (await ModGuard(ctx))
            return;

        await ctx.DeferResponseAsync(true);

        StringBuilder sb = new();
        sb.AppendLine("# Monopoly Men");
        foreach (var kvp in PersistentData.values.casinoPoints.OrderBy(kvp => kvp.Value))
        {
            if (sb.Length > 1800)
                break;
            string username = "[Someone not found]";
            try
            {
                var member = await ctx.Guild!.GetMemberAsync(kvp.Key);
                username = member.Username;
            }
            catch { }
            sb.AppendLine($"**{username}** (<@{kvp.Key}>): {kvp.Value:N}");
        }

        var allBalances = PersistentData.values.casinoPoints.Values
            .ToList();
        allBalances.Sort();
        int firstOver5k = allBalances.FindIndex(b => b > 5000);
        int countOver5k = allBalances.Count - firstOver5k;
        
        sb.AppendLine("# Stats");
        sb.AppendLine($"~Median (over 5k points): {allBalances[firstOver5k + countOver5k / 2]}");
        sb.AppendLine($"~Median (all): {allBalances[allBalances.Count / 2]}");

        var builder = new DiscordFollowupMessageBuilder().WithContent(sb.ToString());
        await ctx.FollowupAsync(builder);
    }
}
