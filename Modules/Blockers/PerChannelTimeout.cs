using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.ContextChecks.ParameterChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BoneBoard.Modules.Blockers;

[Command("channeltimeout")]
internal class PerChannelTimeout(BoneBot bot) : ModuleBase(bot)
{
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs mcea)
        {
            if (MessageCheck(mcea.Message))
            {
                var dmb = new DiscordMessageBuilder()
                    .WithContent($"{mcea.Author.Mention} https://cdn.discordapp.com/attachments/1058836999311728680/1472137806422872105/tenor.gif")
                    .WithAllowedMention(new UserMention(mcea.Author));
                try
                {
                    var msgTask = mcea.Channel.SendMessageAsync(dmb);
                    Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith((_) => TryDeleteDontCare(msgTask.IsCompletedSuccessfully ? msgTask.Result : null));
                }
                catch
                {
                    // dnc
                }
                
                return true;
            }
        }
        else if (eventArgs is MessageUpdatedEventArgs muea)
        {
            return MessageCheck(muea.Message);
        }

        return false;
    }

    protected bool MessageCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author) || msg.Author is null)
            return false;
        
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant

        if (!PersistentData.values.channelTimeoutEndTimes.TryGetValue(msg.ChannelId, out var userEndTimesInChannel))
            return false;

        if (!userEndTimesInChannel.TryGetValue(msg.Author.Id, out var endTime))
            return false;

        if (endTime <= DateTime.Now)
            return false;
        
        TryDeleteDontCare(msg, "User is timed out in this channel");
        return true;
    }

    [Command("setmute")]
    [RequireGuild]
    [RequirePermissions([DiscordPermission.ManageMessages], [DiscordPermission.ModerateMembers])]
    public async Task Timeout(SlashCommandContext ctx, [RequireHigherBotHierarchy] DiscordMember member, TimeSpan duration)
    {
        if (!PersistentData.values.channelTimeoutEndTimes.TryGetValue(ctx.Channel.Id, out var userEndTimesInChannel))
        {
            userEndTimesInChannel = [];
            PersistentData.values.channelTimeoutEndTimes[ctx.Channel.Id] = userEndTimesInChannel;
        }

        userEndTimesInChannel[member.Id] = DateTime.Now + duration;

        await ctx.RespondAsync($"Got it! They'll be unmuted {Formatter.Timestamp(userEndTimesInChannel[member.Id])}", true);
    }
    
    
    [Command("unmute")]
    [RequireGuild]
    [RequirePermissions([DiscordPermission.ManageMessages], [DiscordPermission.ModerateMembers])]
    public async Task Untimeout(SlashCommandContext ctx, [RequireHigherBotHierarchy] DiscordMember member)
    {
        if (!PersistentData.values.channelTimeoutEndTimes.TryGetValue(ctx.Channel.Id, out var userEndTimesInChannel))
        {
            userEndTimesInChannel = [];
            PersistentData.values.channelTimeoutEndTimes[ctx.Channel.Id] = userEndTimesInChannel;
        }

        userEndTimesInChannel[member.Id] = DateTime.Now - TimeSpan.FromDays(1);

        await ctx.RespondAsync($"Got it! They're now unmuted!", true);
    }
}