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
    const Permissions GUARDED_CMD_PERMS = Permissions.ManageRoles | Permissions.ManageMessages;
    
    [SlashCommand("createFrogMsg", "Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [SlashRequireUserPermissions(GUARDED_CMD_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task CreateFrogMessage(
        InteractionContext ctx,
        [Option("msgContent", "The text content of the message. Use {0} to indicate where the current king goes.")]
        string content
        )
    {
        if (ctx.Member is null)
        {
            await ctx.CreateResponseAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(GUARDED_CMD_PERMS))
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

    [SlashCommand("setFrogUnavailableText", "Set the text that will be displayed when someone tries to get the frog role when it's not available.")]
    [SlashRequireUserPermissions(GUARDED_CMD_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task SetFrogUnavailableText(
        InteractionContext ctx,
        [Option("msgContent", "The text content of the message. Use {0} to for curr king, and {1} available day of week.")]
        string content
        )
    {
        if (ctx.Member is null)
        {
            await ctx.CreateResponseAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(GUARDED_CMD_PERMS))
        {
            await ctx.CreateResponseAsync("nuh uh", true);
            return;
        }

        Config.values.frogMessageClosedBase = content;
        Config.WriteConfig();

        await ctx.CreateResponseAsync("Set frog unavailable text!", true);
    }

    [SlashCommand("createFrogLeaderboardMsg", "Creates the message that must be reacted to for KOTF (King of the Frog)")]
    [SlashRequireUserPermissions(GUARDED_CMD_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task CreateFrogLeaderboardMsg(
        InteractionContext ctx,
        [Option("msgContent", "Use {0} to indicate where the leaderboard goes (Will be surrounded by multilines)")]
        string content
       )
    {
        if (ctx.Member is null)
        {
            await ctx.CreateResponseAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(GUARDED_CMD_PERMS))
        {
            await ctx.CreateResponseAsync("nuh uh", true);
            return;
        }

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "\n(There'll be a leaderboard here...)\n"));

        Config.values.frogLeaderboardLink = msg.JumpLink.OriginalString;
        Config.values.frogLeaderboardBase = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].FetchFrogLeaderboardMsg();

        await ctx.CreateResponseAsync("Created KOTF leaderboard message! You may need to delete the old one!", true);
    }


    [SlashCommand("removeUserFromFrogLeaderboard", "Remove user from frog role leaderboard. De-role/block/ban them before use.")]
    [SlashRequireUserPermissions(GUARDED_CMD_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task RemoveUserFromLeaderboard(
        InteractionContext ctx,
        [Option("user", "Whose ID to remove from the leaderboard. Won't trigger leaderboard refresh.")]
        DiscordUser? user = null,
        [Option("userId","The ID to remove from the leaderboard. Won't trigger leaderboard refresh.")]
        long? userId = null
       )
    {
        if (ctx.Member is null)
        {
            await ctx.CreateResponseAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(GUARDED_CMD_PERMS))
        {
            await ctx.CreateResponseAsync("nuh uh", true);
            return;
        }

        ulong? id = (ulong?)userId ?? user?.Id;
        if (!id.HasValue)
        {
            await ctx.CreateResponseAsync("care to elaborate on whos gettin the ax?", true);
            return;
        }

        bool wasInData = PersistentData.values.frogRoleTimes.Remove(id.Value);
        PersistentData.WritePersistentData();

        await ctx.CreateResponseAsync(wasInData ? "Removed that user from the frog leaderboard." : "That user wasn't in the leaderboard (yet?).", true);
    }


    [SlashCommand("reloadCfg", "Reloads the config. This may not have any impact on things that are cached at startup.")]
    [SlashRequireUserPermissions(GUARDED_CMD_PERMS, false)]
    [SlashRequireBotPermissions(Permissions.AddReactions | Permissions.ManageMessages, false)]
    public async Task ReloadConfig(
        InteractionContext ctx
       )
    {
        if (ctx.Member is null)
        {
            await ctx.CreateResponseAsync("😂👎", true);
            return;
        }

        if (!ctx.Member.Permissions.HasPermission(GUARDED_CMD_PERMS))
        {
            await ctx.CreateResponseAsync("nuh uh", true);
            return;
        }

        Config.ReadConfig();

        await ctx.CreateResponseAsync("Read config!", true);
    }

    //public async Task SetFrogThread(
    //    inter
    //    )
}
