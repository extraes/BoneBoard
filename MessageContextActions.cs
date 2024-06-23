using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("msgctx")]
internal class MessageContextActions
{
    const DiscordPermissions FORCE_QUOTE_PERMS = DiscordPermissions.ManageRoles | DiscordPermissions.ManageMessages;

    //[Command("Force Quote w/o rxn")]
    [Command("forceQuoteNoRxn")]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [DirectMessageUsage(DirectMessageUsage.DenyDMs)]
    [RequirePermissions(DiscordPermissions.None, FORCE_QUOTE_PERMS)]
    public static async Task ForceQuoteSilent(SlashCommandContext ctx, DiscordMessage msg)
    {
        if (await SlashCommands.ModGuard(ctx, false))
            return;

        if (ctx.Member is null || !ctx.Member.Permissions.HasPermission(FORCE_QUOTE_PERMS))
        {
            await ctx.RespondAsync("nuh uh", true);
            return;
        }
        
        await ctx.DeferResponseAsync(true);
        Logger.Put($"Quote forced from {ctx.Member} on message {msg.Author}");
        await BoneBot.Bots[ctx.Client].PerformQuote(msg, null);
        await ctx.FollowupAsync("Done! Hopefully.", true);
    }

    [Command("forceQuote")]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [DirectMessageUsage(DirectMessageUsage.DenyDMs)]
    [RequirePermissions(DiscordPermissions.AddReactions, FORCE_QUOTE_PERMS)]
    public static async Task ForceQuote(SlashCommandContext ctx, DiscordMessage msg)
    {
        if (await SlashCommands.ModGuard(ctx, false))
            return;

        if (ctx.Member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(FORCE_QUOTE_PERMS))
        {
            await ctx.RespondAsync("nuh uh", true);
            return;
        }

        await ctx.DeferResponseAsync(true);
        Logger.Put($"Quote forced from {ctx.Member} on message {msg}");

        if (!DiscordEmoji.TryFromGuildEmote(ctx.Client, Config.values.requiredEmojis.First(), out var emoji))
        {
            var dfmbFail = new DiscordFollowupMessageBuilder()
                        .WithContent("Unable to get reaction emoji... Try using the silent option.")
                        .AsEphemeral();
            await ctx.FollowupAsync(dfmbFail);
            return;
        }

        await BoneBot.Bots[ctx.Client].PerformQuote(msg, emoji);

        await ctx.FollowupAsync("Done! Hopefully.", true);
    }
}
