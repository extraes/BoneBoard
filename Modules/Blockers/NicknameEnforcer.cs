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

internal partial class NicknameEnforcer(BoneBot bot) : ModuleBase(bot)
{
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
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant
        if (!Config.values.channelsWhereNicknameMustFollowFormat.Contains(msg.ChannelId))
            return false;
        if (Config.values.nicknameFormatInQuestion.Length == 0
            || Config.values.nicknameFormatInQuestion.All(string.IsNullOrWhiteSpace))
        {
            Logger.Warn($"Message sent in a nickname-enforced channel ({msg.Channel}), but the format is blank ([{string.Join(", ", Config.values.nicknameFormatInQuestion)}])");
            return false;
        }

        if (msg.Author is not DiscordMember member)
        {
            Logger.Warn($"Message sent in a nickname-enforced channel ({msg.Channel}), but the author wasn't sent as a DiscordMember!");

            return false;
        }
        
        
        Regex symbols = SymbolRegex();


        string cleanNickname = symbols.Replace(member.DisplayName, "");

        int lastIdx = 0;
        foreach (string formatPart in Config.values.theFormatInQuestion)
        {
            var part = symbols.Replace(formatPart, "");
            int fmtIdx = cleanNickname.IndexOf(part, lastIdx, StringComparison.InvariantCultureIgnoreCase);
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
