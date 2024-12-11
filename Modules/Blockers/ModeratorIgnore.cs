using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;
using System.Reflection;
using System.Diagnostics;
using DSharpPlus.Commands.ContextChecks;
using System.ComponentModel;
using DSharpPlus;

namespace BoneBoard.Modules.Blockers;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("starignore")]
internal class ModeratorIgnore : ModuleBase
{
    static int eventsProcessed = 0;
    static Stopwatch timeWasted = new();
    public enum TimeUnit
    {
        Seconds,
        Minutes,
        Hours
    }

    struct IgnoreData
    {
        public DateTime? ignoreTime;
        public int? ignoreCount;
    }

    public ModeratorIgnore(BoneBot bot) : base(bot) { }
    static Dictionary<DiscordUser, IgnoreData> ignoreCounts = new();

    protected override async Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        timeWasted.Start();
        DiscordUser? user = GetUser(eventArgs);
        eventsProcessed++;
        timeWasted.Stop();
        if (user is null)
            return false;

        if (!ignoreCounts.TryGetValue(user, out IgnoreData data))
            return false;

        if (data.ignoreTime.HasValue)
        {
            if (data.ignoreTime.Value < DateTime.Now)
            {
                ignoreCounts.Remove(user);
                return false;
            }

            return true;
        }

        if (data.ignoreCount.HasValue)
        {
            if (data.ignoreCount.Value <= 0)
            {
                ignoreCounts.Remove(user);
                return false;
            }

            data.ignoreCount--;
            ignoreCounts[user] = data;

            return true;
        }

        Logger.Warn("Both ignorecount and ignoretime are null! This should not happen!");
        if (Debugger.IsAttached)
            Debugger.Break();
        return false;
    }

    [Command("forTime"), Description("Ignores you/someone else for a given length of time")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MODERATOR_PERMS)]
    public static async Task IgnoreFor(SlashCommandContext ctx, int count, TimeUnit unit, DiscordMember? member = null, bool overwrite = false)
    {
        member ??= ctx.Member;
        if (member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        if (count <= 0)
        {
            await ctx.RespondAsync("fuck i look like a time traveler??", true);
            return;
        }

        if (ignoreCounts.ContainsKey(member) && !overwrite)
        {
            await ctx.RespondAsync($"{member.DisplayName} is already ignored", true);
        }

        TimeSpan addedTime = unit switch
        {
            TimeUnit.Seconds => TimeSpan.FromSeconds(count),
            TimeUnit.Minutes => TimeSpan.FromMinutes(count),
            TimeUnit.Hours => TimeSpan.FromHours(count),
            _ => TimeSpan.FromMinutes(count),
        };

        IgnoreData newData = new()
        {
            ignoreTime = DateTime.Now + addedTime
        };

        ignoreCounts[member] = newData;
        await ctx.RespondAsync($"{member.DisplayName} will now be ignored until {Formatter.Timestamp(newData.ignoreTime.Value, TimestampFormat.ShortDateTime)}", true);
    }

    [Command("forCount"), Description("Ignores you/someone else for a given number of events")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MODERATOR_PERMS)]
    public static async Task IgnoreFor(SlashCommandContext ctx, int count, DiscordMember? member = null, bool overwrite = false)
    {
        member ??= ctx.Member;
        if (member is null || BoneBot.Bots[ctx.Client].IsMe(member))
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        if (count <= 0)
        {
            await ctx.RespondAsync("i cant change whats been done bro", true);
            return;
        }

        if (ignoreCounts.ContainsKey(member) && !overwrite)
        {
            await ctx.RespondAsync($"{member.DisplayName} is already ignored", true);
        }

        IgnoreData newData = new()
        {
            ignoreCount = count
        };

        ignoreCounts[member] = newData;
        await ctx.RespondAsync($"{member.DisplayName} will now be ignored for the next {count} event(s) that are associated with them.", true);
    }

    [Command("profilington"), Description("Check how long this shitass code has executed!")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MODERATOR_PERMS)]
    public static async Task DumpTimes(SlashCommandContext ctx)
    {
        await ctx.RespondAsync($"GetUser has executed {eventsProcessed} time(s) and wasted {timeWasted.Elapsed.TotalSeconds} seconds of CPU time!", true);
    }
}
