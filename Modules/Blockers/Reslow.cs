using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace BoneBoard.Modules.Blockers;

[Command("reslow")]
internal class Reslow(BoneBot bot) : ModuleBase(bot)
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
        
        if (!Config.values.channelsWhereBotReimplementsSlowmode.Contains(msg.ChannelId))
            return false;

        if (msg.IsEdited) // dont check "slowmode" on msg edits, lol
            return false;

        if (msg.Channel is null || msg.Author is not DiscordMember member)
            return false;

        if (!msg.Channel.PerUserRateLimit.HasValue)
            return false; // channel not ratelimited

        if (!msg.Channel.PermissionsFor(member).HasPermission(DiscordPermission.BypassSlowmode))
            return false; // means discord is already handling the slowmode

        if (msg.Channel.PermissionsFor(member).HasPermission(DiscordPermission.ManageMessages))
            return false; // dont re-slowmode moderators
        
        // completely ignore
        if (PersistentData.values.ignoreReslowingUntil.TryGetValue(msg.ChannelId, out var ignoreDict)
            && ignoreDict.TryGetValue(member.Id, out var ignoreExpiry)
            && ignoreExpiry > DateTime.Now)
        {
            return false;
        }

        if (!PersistentData.values.lastReslowedMessages.TryGetValue(msg.ChannelId, out var usersDict))
        {
            // channel hasnt had any messages reslowed in it. let the user go
            PersistentData.values.lastReslowedMessages[msg.ChannelId] = new()
            { 
                { member.Id , msg.Id }
            };
            return false;
        }

        if (usersDict.TryGetValue(member.Id, out ulong lastMsgId))
        {
            var lastTime = lastMsgId.GetSnowflakeTime();
            var now = DateTime.Now;
            // their last message was sent less than (ratelimit) seconds ago
            if (lastTime + TimeSpan.FromSeconds(msg.Channel.PerUserRateLimit.Value) > now)
            {
                // delete and block propagation
                TryDeleteDontCare(msg);
                return true;
            }

            // user hasnt sent any messages in this channel
            // usersDict[member.Id] = msg.Id; done later in this method anyway
        }
        
        usersDict[member.Id] = msg.Id;
        PersistentData.WritePersistentData();
        return false;
    }

    [Command("ignoreme"), Description("Makes the bot ignore your messages in THIS channel (for re-slowmode only)")]
    [RequireGuild]
    public static async Task Ignore(SlashCommandContext ctx, double hours = 1)
    {
        if (!ctx.Channel.PerUserRateLimit.HasValue
            || !Config.values.channelsWhereBotReimplementsSlowmode.Contains(ctx.Channel.Id))
        {
            await ctx.RespondAsync("This channel doesn't have a slowmode/bot-implemented slowmode.", true);
        }
        
        if (!ctx.Channel.PermissionsFor(ctx.Member!).HasPermission(DiscordPermission.BypassSlowmode))
        {
            await ctx.RespondAsync("You don't even bypass slowmode in this channel... what?", true);
            return;
        }
        
        if (hours < 1)
        {
            await ctx.RespondAsync("You can't buy less than an hour of ignorance.", true);
            return;
        }

        if (hours > 6)
        {
            await ctx.RespondAsync("You can't buy more than 6 hours of ignorance", true);
            return;
        }

        int balance = PersistentData.values.casinoPoints.GetValueOrDefault(ctx.User.Id);
        
        const double MULT = 1337; // fun number
        const int QUANTIZE_TO = 50;
        double priceCalc = Math.Log(hours) / Math.Log(1.6) + 1; // idk i fucked with shit in Desmos until it looked good
        priceCalc *= MULT;
        priceCalc += balance * 0.01 * hours; // wealth tax lmao
        int price = (int)Math.Round(priceCalc / QUANTIZE_TO) * QUANTIZE_TO;
        if (balance < price)
        {
            await ctx.RespondAsync($"You don't have enough points. You have {balance}, but you need {price}", true);
            return;
        }

        balance -= price;
        PersistentData.values.casinoPoints[ctx.User.Id] = balance;

        if (!PersistentData.values.ignoreReslowingUntil.TryGetValue(ctx.Channel.Id, out var userDict))
        {
            userDict = new();
            PersistentData.values.ignoreReslowingUntil[ctx.Channel.Id] = userDict;
        }

        bool alreadyInvuln = userDict.TryGetValue(ctx.User.Id, out DateTime expireAt) && expireAt > DateTime.Now;

        expireAt = DateTime.Now.AddHours(hours);
        userDict[ctx.User.Id] = expireAt;
        PersistentData.WritePersistentData();
        Logger.Put($"{ctx.User} was charged {price} points for a {hours:0.00} hr slowmode exemption. "
                   + $"They now have {balance} points");
        
        string respondStr = $"Okay! You'll be noticed again {Formatter.Timestamp(expireAt)}, but until then, post away!";
        respondStr += $"\n You were charged {price} points for this btw. You now have {balance} points.";
        if (alreadyInvuln)
            respondStr += "\n-# You were also still ignored btw. lol.";
        await ctx.RespondAsync(respondStr, true);
    }
}
