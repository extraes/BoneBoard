using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Skeleton;

namespace BoneBoard.Modules.Blockers;

internal class WordPercentage : ModuleBase
{
    public WordPercentage(BoneBot bot) : base(bot) { }

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

        return false;
    }

    static async Task<bool> MessageCheckAsync(DiscordMessage msg)
    {
        if (msg.Channel is null || !Config.values.channelsWhereMessagesMustHaveMinPercOfAWord.Contains(msg.Channel.Id))
        {
            return false;
        }

        if (WordPercentageIsTooLow(msg.Content))
        {
            try
            {
                await msg.DeleteAsync("you must use more of the designated words in this channel. woe.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to delete message from {msg.Author}! ", ex);
            }

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
