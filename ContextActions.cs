using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

internal class ContextActions : ApplicationCommandModule
{
    const Permissions FORCE_QUOTE_PERMS = Permissions.ManageRoles | Permissions.ManageMessages;

    [ContextMenu(ApplicationCommandType.MessageContextMenu, "Force Quote w/o rxn")]
    [SlashRequireUserPermissions(FORCE_QUOTE_PERMS, false)]
    public async Task ForceQuoteSilent(ContextMenuContext ctx)
    {
        if (!ctx.Member.Permissions.HasPermission(FORCE_QUOTE_PERMS))
        {
            await ctx.CreateResponseAsync("nuh uh", true);
            return;
        }

        await ctx.DeferAsync(true);
        Logger.Put($"Quote forced from {ctx.Member} on message {ctx.TargetMessage}");
        await BoneBot.Bots[ctx.Client].PerformQuote(ctx.TargetMessage, null);
        var dfmb = new DiscordFollowupMessageBuilder()
                        .WithContent("Done! Hopefully.")
                        .AsEphemeral();
        await ctx.FollowUpAsync(dfmb);
    }

    [ContextMenu(ApplicationCommandType.MessageContextMenu, "Force Quote")]
    [SlashRequireUserPermissions(FORCE_QUOTE_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions, false)]
    public async Task ForceQuote(ContextMenuContext ctx)
    {
        if (ctx.Member is null)
        {
            await ctx.CreateResponseAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(FORCE_QUOTE_PERMS))
        {
            await ctx.CreateResponseAsync("nuh uh", true);
            return;
        }

        await ctx.DeferAsync(true);
        Logger.Put($"Quote forced from {ctx.Member} on message {ctx.TargetMessage}");

        if (!DiscordEmoji.TryFromGuildEmote(ctx.Client, Config.values.requiredEmojis.First(), out var emoji))
        {
            var dfmbFail = new DiscordFollowupMessageBuilder()
                        .WithContent("Unable to get reaction emoji... Try using the silent option.")
                        .AsEphemeral();
            await ctx.FollowUpAsync(dfmbFail);
            return;
        }

        await BoneBot.Bots[ctx.Client].PerformQuote(ctx.TargetMessage, emoji);

        var dfmb = new DiscordFollowupMessageBuilder()
                        .WithContent("Done! Hopefully.")
                        .AsEphemeral();
        await ctx.FollowUpAsync(dfmb);
    }
}
