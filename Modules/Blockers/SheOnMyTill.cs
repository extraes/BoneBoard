﻿using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class SheOnMyTill : ModuleBase
{
    public SheOnMyTill(BoneBot bot) : base(bot) { }

    protected override async Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs mcea)
        {
            return await MessageCheckAsync(mcea.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs muea)
        {
            return await MessageCheckAsync(muea.Message);
        }

        return false;
    }

    protected async Task<bool> MessageCheckAsync(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;

        if (!Config.values.channelsWhereMessagesMustConformToFormat.Contains(msg.ChannelId))
            return false;

        if (string.IsNullOrWhiteSpace(Formatter.Strip(msg.Content)))
            return false;

        if (Quoter.Link.Replace(msg.Content, "") == "")
            return false;

        int lastIdx = 0;
        foreach (string formatPart in Config.values.theFormatInQuestion)
        {
            int fmtIdx = msg.Content.IndexOf(formatPart, lastIdx, StringComparison.InvariantCultureIgnoreCase);
            if (fmtIdx == -1)
            {
                string fullFormat = string.Join(" [...] ", Config.values.theFormatInQuestion);

                await TryDeleteAsync(msg, $"Does not conform to {fullFormat}");
                return true;
            }

            lastIdx = fmtIdx + formatPart.Length;
        }

        return false;
    }
}
