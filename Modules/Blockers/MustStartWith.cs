using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class MustStartWith : ModuleBase
{
    public MustStartWith(BoneBot bot) : base(bot) { }

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

    private bool MessageCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;

        if (!Config.values.channelsWhereMessagesMustStartWith.Contains(msg.ChannelId))
            return false;

        string checkWordsAgainst = msg.Content.Replace('’', '\'').Replace("'", "");
        bool startsWithNeededString = Config.values.possibleMessageStarts.Any(s => checkWordsAgainst.StartsWith(s, StringComparison.InvariantCultureIgnoreCase));
        return !startsWithNeededString;
    }
}
