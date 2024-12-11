using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class CustomEmojisAndStickers : ModuleBase
{
    public CustomEmojisAndStickers(BoneBot bot) : base(bot) { }

    protected override async Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageReactionAddedEventArgs rxnArgs)
        {
            if (!IsProhibitedIn(rxnArgs.User, rxnArgs.Channel))
                return false;

            if (rxnArgs.Emoji.Id == 0)
            {
                try
                {
                    await rxnArgs.Message.DeleteReactionAsync(rxnArgs.Emoji, rxnArgs.User, "this user cant use custom emojis in this channel. woe.");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to delete custom emoji reaction on message from {rxnArgs.User}! ", ex);
                }

                return true;
            }
        }
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return await MessageCheckAsync(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return await MessageCheckAsync(msgUpdatedArgs.Message);
        }

        return false;
    }

    async Task<bool> MessageCheckAsync(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;

        if (!IsProhibitedIn(msg.Author, msg.Channel))
        {
            return false;
        }

        string? reason = null;
        if (msg.Stickers?.FirstOrDefault()?.Type == DiscordStickerType.Guild) // false if no stickers
            reason = "this user gets no stickers in this channel. woe.";


        if (Quoter.CustomEmoji.IsMatch(msg.Content)) //todo: ignore emojis from args.Guild 
            reason = "this user cant use custom emojis in this channel. woe.";

        if (reason is null)
            return false;

        try
        {
            await msg.DeleteAsync(reason);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to delete message with custom emoji/sticker from {msg.Author}! ", ex);
        }
        return true;
    }

    static bool IsProhibitedIn(DiscordUser? user, DiscordChannel? channel)
    {
        if (user is null || channel is null)
            return false;

        return Config.values.channelsWhereUsersAreProhibitedFromCustomEmojis.TryGetValue(channel.Id.ToString(), out ulong[]? emojiUserIds) && !emojiUserIds.Contains(user.Id);
    }
}
