using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class FlagRestriction : ModuleBase
{
    public FlagRestriction(BoneBot bot) : base(bot) { }

    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageReactionAddedEventArgs rxnArgs)
        {
            if (bot.IsMe(rxnArgs.User))
                return false;

            if (Config.values.channelsWhereAllFlagsButListedAreProhibited.TryGetValue(rxnArgs.Channel.Id.ToString(), out string[]? allowedFlags))
            {
                // this is gonna stop things that are custom emojis with flag_ in them, but that's a small price to pay (and also the white flag)
                if (rxnArgs.Emoji.Id == 0 && rxnArgs.Emoji.GetDiscordName().Contains("flag_") && !allowedFlags.Contains(rxnArgs.Emoji.Name))
                {

                    _ = TryDeleteAsync(rxnArgs.Message, rxnArgs.Emoji, rxnArgs.User,
                        "you must rep the right flag in this channel. woe.");
                    return true;
                }
            }
        }
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return MessageFlagCheck(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return MessageFlagCheck(msgUpdatedArgs.Message);
        }

        return false;
    }

    private bool MessageFlagCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;

        if (msg.Channel is null || !Config.values.channelsWhereAllFlagsButListedAreProhibited.TryGetValue(msg.Channel.Id.ToString(), out string[]? allowedFlags) || msg.Content.Length < 4)
        {
            return false;
        }

        if (ContainsDisallowedFlag(msg.Content, allowedFlags))
        {

            TryDeleteDontCare(msg, "you must rep the right flag in this channel. woe.");

            return true;
        }

        return false;
    }

    static bool ContainsDisallowedFlag(string str, string[] allowedFlags)
    {

        const char REG_INDIC_START = '\ud83c';

        // long story short: country flags (like :flag_us:) are just two "regional indicator" emojis
        // but each "regional indicator" emoji is a pair of two unicode characters, each starting with \ud83c

        DiscordEmoji? lastRegional = null;
        char lastChar = ' ';

        foreach (char c in str)
        {
            if (char.GetUnicodeCategory(c) != UnicodeCategory.Surrogate) // "flush" last regional indicator if the curr char cant be a part of a reg indic
                lastRegional = null;


            bool isPotentialFlagPart = lastChar == REG_INDIC_START && char.GetUnicodeCategory(c) == UnicodeCategory.Surrogate;
            if (!isPotentialFlagPart)
            {
                lastChar = c;
                continue;
            }


            if (!DiscordEmoji.TryFromUnicode(lastChar.ToString() + c, out DiscordEmoji? regionalIndicator))
            {
                Logger.Warn($"Failed to parse emoji from {lastChar}{c} - it should be a regional indicator, no? See full message content: {str}");
                lastChar = c;
                continue;
            }


            if (lastRegional is null)
            {
                // prepare to check if this is a valid flag
                lastRegional = regionalIndicator;
                lastChar = c;
                continue;
            }

            // check if its a full valid flag
            string flag = lastRegional.Name + regionalIndicator.Name;
            if (!DiscordEmoji.TryFromUnicode(flag, out DiscordEmoji? compoundEmoji))
            {
                lastChar = c;
                continue;
            }

            if (allowedFlags.Contains(compoundEmoji.Name))
            {
                lastRegional = null; // flush so flags can exist without a space in between
                lastChar = c;
                continue;
            }

            return true;
        }

        return false;
    }
}
