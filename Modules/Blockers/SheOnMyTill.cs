using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal partial class SheOnMyTill(BoneBot bot) : ModuleBase(bot)
{
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs mcea)
        {
            return MessageCheck(mcea.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs muea)
        {
            return MessageCheck(muea.Message);
        }

        return false;
    }

    protected bool MessageCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;
        
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant

        if (!Config.values.channelsWhereMessagesMustConformToFormat.Contains(msg.ChannelId))
            return false;

        if (string.IsNullOrWhiteSpace(Formatter.Strip(msg.Content)))
            return false;

        if (Quoter.Link.Replace(msg.Content, "") == "")
            return false;
        Regex symbols = SymbolRegex();


        string cleanContent = symbols.Replace(msg.Content, "");

        int lastIdx = 0;
        foreach (string formatPart in Config.values.theFormatInQuestion)
        {
            var part = symbols.Replace(formatPart, "");
            int fmtIdx = cleanContent.IndexOf(part, lastIdx, StringComparison.InvariantCultureIgnoreCase);
            if (fmtIdx == -1)
            {
                string fullFormat = string.Join(" [...] ", Config.values.theFormatInQuestion);

                TryDeleteDontCare(msg, $"Does not conform to: {fullFormat}");
                return true;
            }

            lastIdx = fmtIdx + part.Length;
        }

        return false;
    }

    [GeneratedRegex(@"["",.']")]
    private static partial Regex SymbolRegex();
}
