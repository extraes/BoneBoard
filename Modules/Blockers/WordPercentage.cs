using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class WordPercentage : ModuleBase
{
    public WordPercentage(BoneBot bot) : base(bot) { }

    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return MessageCheck(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return MessageCheck(msgUpdatedArgs.Message);
        }

        return false;
    }

    bool MessageCheck(DiscordMessage msg)
    {
        if (msg.Channel is null || !Config.values.channelsWhereMessagesMustHaveMinPercOfAWord.Contains(msg.Channel.Id))
        {
            return false;
        }
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant

        if (WordPercentageIsTooLow(msg.Content))
        {
            
            TryDeleteDontCare(msg, "you must use more of the designated words in this channel. woe.");

            return true;
        }

        return false;
    }

    private static bool WordPercentageIsTooLow(string message)
    {
        string checkWordsAgainst = message.Replace('’', '\'').Replace("'", "");
        var words = checkWordsAgainst.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var wordPerc = words.Length == 0 ? 1 : words.Count(w => Config.values.theWordOrWords.Any(s => s.Equals(w, StringComparison.InvariantCultureIgnoreCase))) / (float)words.Length;
        bool wordPercTooLow = wordPerc < Config.values.wordPercentage;
        return wordPercTooLow;
    }
}
