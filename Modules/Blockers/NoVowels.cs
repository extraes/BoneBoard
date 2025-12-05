using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class NoVowels : ModuleBase
{
    public NoVowels(BoneBot bot) : base(bot) { }

    protected override async Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return await MessageCheckAsync(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return await MessageCheckAsync(msgUpdatedArgs.Message);
        }
        else if (eventArgs is MessageReactionAddedEventArgs reactionArgs)
        {
            string? regionalIndicator = reactionArgs.Emoji.GetDiscordName();
            regionalIndicator = regionalIndicator.Contains("regional_indicator_") ? regionalIndicator.Replace("regional_indicator_", "") : null;
            if (regionalIndicator is null || regionalIndicator.Length != 1)
                return false;

            if (!Config.values.channelsWhereNoVowelsAreAllowed.Contains(reactionArgs.Channel.Id) || !"aeiou".Contains(regionalIndicator[0], StringComparison.InvariantCultureIgnoreCase))
                return false;
            
            try
            {
                await reactionArgs.Message.DeleteReactionAsync(reactionArgs.Emoji, reactionArgs.User, "'no vowels' includes writing in reactions. woe.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to delete reaction from {reactionArgs.User}! ", ex);
            }

            return true;
            
        }

        return false;
    }

    private async Task<bool> MessageCheckAsync(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;

        bool hasVowelInDisallowedChannel = Config.values.channelsWhereNoVowelsAreAllowed.Contains(msg.ChannelId) && msg.Content.Any(c => "aeiou".Contains(c, StringComparison.InvariantCultureIgnoreCase));

        if (hasVowelInDisallowedChannel)
        {
            try
            {
                await msg.DeleteAsync("you must not use vowels in this channel. woe.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to delete message from {msg.Author}! ", ex);
            }

            return true;
        }

        return false;
    }
}
