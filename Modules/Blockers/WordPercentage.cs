using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal partial class WordPercentage : ModuleBase
{
    public WordPercentage(BoneBot bot) : base(bot) { }

    private static readonly Regex Whitespace = WhitespaceRegex();
    internal static readonly Regex Link = LinkRegex();
    

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
        if (msg.Author?.IsBot ?? true)
            return false;
        
        if (msg.Channel is null || !Config.values.channelsWhereMessagesMustHaveMinPercOfAWord.Contains(msg.Channel.Id))
        {
            return false;
        }
        
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant
        
        // will ignore link-only messages & attachment-only no-content messages
        if (string.IsNullOrWhiteSpace(Link.Replace(msg.Content, "")))
            return false;

        string checkWordsAgainst = msg.Content.Replace('’', '\'').Replace("'", "");
        var words = Whitespace.Split(checkWordsAgainst);
        if (WordPercentageIsTooLow(words))
        {
            TryDeleteDontCare(msg, "you must use more of the designated words in this channel. woe.");

            return true;
        }

        if (AlLWordsClumpedTogether(words))
        {
            TryDeleteDontCare(msg, "you .");

            return true;
        }

        return false;
    }

    private bool AlLWordsClumpedTogether(string[] words)
    {
        bool[] wordsThatCount = words.Select(w =>
            Config.values.theWordOrWords.Any(s => w.Contains(s, StringComparison.InvariantCultureIgnoreCase))
            ).ToArray();

        List<int> chunkSizeLengths = new(4);
        int currChunkSize = 0;
        foreach (bool counts in wordsThatCount)
        {
            if (counts)
            {
                currChunkSize++;
            }
            else
            {
                if (currChunkSize > 0)
                    chunkSizeLengths.Add(currChunkSize);
                
                currChunkSize = 0;
            }
        }
        
        Logger.Put($"message {string.Join(", ", words)} has the following chunk sizes: {string.Join(", ", chunkSizeLengths)}");

        if (chunkSizeLengths is [1])
            return false;
        else if (chunkSizeLengths.Count == 1 && chunkSizeLengths[0] > 3 && wordsThatCount.EndsWith(true))
            return true;

        return false;
    }

    private static bool WordPercentageIsTooLow(string[] words)
    {
        var wordPerc = words.Length == 0 ? 1 : words.Count(w => Config.values.theWordOrWords.Any(s => w.Contains(s, StringComparison.InvariantCultureIgnoreCase))) / (float)words.Length;
        bool wordPercTooLow = wordPerc < Config.values.wordPercentage;
        return wordPercTooLow;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"\w+://\S+", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex LinkRegex();
}
