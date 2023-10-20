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

internal class SlashCommands : ApplicationCommandModule
{
    const Permissions FORCE_QUOTE_PERMS = Permissions.ManageRoles | Permissions.ManageMessages;
    
    [SlashCommand("createFrogMsg", "Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [SlashRequireUserPermissions(FORCE_QUOTE_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task CreateFrogMessage(
        InteractionContext ctx,
        [Option("msgContent", "The text content of the message. Use {0} to indicate where the leaderboard goes.")]
        string content
        )
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

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}","(there'll be a frog haver here)"));
        
        if (Config.values.frogRoleActivation == FrogRoleActivation.REACTION)
            await msg.CreateReactionAsync(DiscordEmoji.FromUnicode("🐸"));

        Config.values.frogMessageLink = msg.JumpLink.OriginalString;
        Config.values.frogMessageBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].FetchFrogMessage();

        await ctx.CreateResponseAsync("Created KOTF message!", true);
    }

    [SlashCommand("createFrogLeaderboardMsg", "Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [SlashRequireUserPermissions(FORCE_QUOTE_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task CreateFrogLeaderboardMsg(
       InteractionContext ctx,
       [Option("msgContent", "The text content of the message. Use {0} to indicate where the leaderboard goes (after a multiline).")]
        string content
       )
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

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "\n(There'll be a leaderboard here...)\n"));
        //await msg.CreateReactionAsync(DiscordEmoji.FromUnicode("🐸"));

        Config.values.frogLeaderboardLink = msg.JumpLink.OriginalString;
        Config.values.frogLeaderboardBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].FetchFrogLeaderboardMsg();

        await ctx.CreateResponseAsync("Created KOTF message!", true);
    }

    //public async Task SetFrogThread(
    //    inter
    //    )
}
