using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.ComponentModel;

namespace BoneBoard.Modules;


[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("pollster")]
internal class Pollster : ModuleBase
{
    public const DiscordPermissions MDOERATOR_PERMS = DiscordPermissions.ManageRoles | DiscordPermissions.ManageMessages;

    readonly HashSet<ulong> polledThreads = new();
    readonly Dictionary<ulong, CancellationToken> roundRobiningThreads = new();

    public Pollster(BoneBot bot) : base(bot)
    { }

    //private async Task StartPeriodicTasks(DiscordClient client, GuildDownloadCompletedEventArgs args)
    //{
    //    if (Config.values.roundRobinBumpForumId != 0)
    //    {
    //        foreach (DiscordGuild guild in args.Guilds.Values)
    //        {
    //            if (guild.Channels.TryGetValue(Config.values.roundRobinBumpForumId, out DiscordChannel? channel) && channel is DiscordForumChannel forum)
    //            {


    //            }
    //        }
    //    }
    //}

    //private async void RoundRobinForum(DiscordForumChannel forum)
    //{
    //    if (roundRobiningThreads.ContainsKey(forum.Id))
    //        return;
    //    CancellationTokenSource cts = new();
    //    roundRobiningThreads.Add(forum.Id, cts.Token);
    //    TimeSpan waitTime = TimeSpan.FromMinutes(10); //todo: unhardcode
    //    try
    //    {
    //        while (!cts.Token.IsCancellationRequested)
    //        {
    //            foreach (DiscordThreadChannel thread in forum.Threads)
    //            {
    //                await Task.Delay(); // rate limit dodging
    //                if (cts.Token.IsCancellationRequested)
    //                    return;

    //                try
    //                {

    //                await thread.JoinThreadAsync();

    //                }
    //            }
    //        }
    //    }
    //    catch (Exception e)
    //    {
    //        Logger.Warn("Exception while round robinning", e);
    //    }
    //    finally
    //    {
    //        roundRobiningThreads.Remove(forum.Id);
    //    }
    //}

    // break glass in case of emergency
    //async async void PeriodicChecker()
    //{
    //    DiscordForumChannel? forum = null;
    //    DateTime lastFetched = DateTime.Now;
    //    try
    //    {
    //        while (true)
    //        {
    //            await Task.Delay(TimeSpan.FromMinutes(1));

    //            if (Config.values.pollsterAutoWatchForumId == 0)
    //                continue; // not set up yet (or disabled)

    //            try
    //            {
    //                Logger.Warn("Fetching autowatch forum");
    //                forum = await bot.client.GetChannelAsync(Config.values.pollsterAutoWatchForumId) as DiscordForumChannel;
    //                lastFetched = DateTime.Now;
    //            }
    //            catch (Exception e)
    //            {
    //                Logger.Error("Failed to get forum channel", e);
    //                continue;
    //            }

    //            if (forum is null)
    //            {
    //                Logger.Warn("Failed to get forum channel because it wasn't a forum channel");
    //                continue;
    //            }
    //            foreach (DiscordThreadChannel item in forum.Threads.Where(t => t.CreationTimestamp > lastFetched))
    //            {
    //                Logger.Put($"Joining thread {item}");
    //                await item.JoinThreadAsync();
    //            }
    //        }
    //    }
    //    catch (Exception e)
    //    {
    //        Logger.Error("Exception in pollster periodic checker!", e);
    //    }
    //}

    protected override async Task ThreadCreated(DiscordClient client, ThreadCreatedEventArgs args)
    {
        if (args.Thread.ParentId == Config.values.pollsterAutoWatchForumId)
            await SendPollOrGetFailReason(args.Thread);
    }

    private async Task<string?> SendPollOrGetFailReason(DiscordChannel thread)
    {
        if (Config.values.pollsterCategories.Length == 0)
        {
            return "Pollster categories are not set up. Bailing.";
        }
        if (Config.values.pollsterMaxVal <= 0 || Config.values.pollsterMaxVal > 10)
        {
            return "Pollster max val needs to be between 0 and 10. Bailing.";
        }

        double pollDuration = (Config.values.pollsterEndTime - DateTime.Now).TotalHours;
        if (pollDuration < 0)
        {
            Logger.Warn("Pollster end time is in the past. Defaulting to 24 hours.");
            pollDuration = 24;
        }


        if (polledThreads.Contains(thread.Id))
            return "Polls have already been sent in this thread";
        polledThreads.Add(thread.Id); // prevent double polling

        try
        {
            if (thread is DiscordThreadChannel threadChannel)
                await threadChannel.JoinThreadAsync();
        }
        catch (Exception e)
        {
            Logger.Warn("Failed to join thread", e);
            return e.ToString();
        }

        foreach (string category in Config.values.pollsterCategories)
        {
            DiscordPollBuilder poll = new DiscordPollBuilder()
                .WithQuestion(category)
                .WithDuration((int)Math.Ceiling(pollDuration)) // discord only supports integers for hours
                .AsMultipleChoice(false);

            // start at 1 because users shouldnt be shown "indices", theyre voting, not programming
            for (int i = 1; i <= Config.values.pollsterMaxVal; i++)
            {
                poll.AddOption(i.ToString());
            }


            DiscordMessageBuilder dmb = new DiscordMessageBuilder()
                .WithPoll(poll);

            try
            {
                await thread.SendMessageAsync(dmb);
            }
            catch (Exception e)
            {
                Logger.Warn($"Failed to send poll for {category} in {thread}", e);
                return e.ToString();
            }
        }

        Logger.Put("Sent polls in " + thread);
        return null;
    }

    [Command("sendHere")]
    [RequirePermissions(DiscordPermissions.AttachFiles | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    [Description("Sends polls here")]
    public async Task SendPollsHere(SlashCommandContext ctx)
    {
        string? failReason = await SendPollOrGetFailReason(ctx.Channel);
        if (failReason is not null)
        {
            await ctx.RespondAsync(failReason, true);
        }
        else await ctx.RespondAsync("Sent polls successfully", true);
    }

    [Command("sendForAllInForum")]
    [RequirePermissions(DiscordPermissions.AttachFiles | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    [Description("Sends polls to all threads in a given forum")]
    public async Task SendPollsToForum(SlashCommandContext ctx, DiscordChannel forum)
    {
        if (forum.Type != DiscordChannelType.GuildForum || forum is not DiscordForumChannel forumChannel)
        {
            await ctx.RespondAsync("That's not a forum.", true);
            return;
        }

        // iterates a forum and sends polls to all threads
        await ctx.DeferResponseAsync(true);
        try
        {
            foreach (DiscordThreadChannel thread in forum.Threads)
            {
                await Task.Delay(5000); // rate limit dodging
                string? failReason = await SendPollOrGetFailReason(thread);
                if (failReason is not null)
                {
                    await ctx.FollowupAsync($"Failed to send to \"{thread}\" - " + failReason, true);
                }
            }

            await ctx.FollowupAsync("Sent polls to all threads in forum");
        }
        catch (Exception e)
        {
            Logger.Warn($"Exception while sending polls to forum {forumChannel}", e);
            try
            {
                await ctx.FollowupAsync($"Exception while sending polls: {e}", true);
            }
            catch { }
            return;
        }
    }

    //[Command("startRoundRobinMessages")]
    //[RequirePermissions(DiscordPermissions.AttachFiles | DiscordPermissions.ManageMessages, MDOERATOR_PERMS)]
    //public async Task StartRoundRobinMessages(SlashCommandContext ctx, DiscordChannel forum, [Description("In addition to configured ignored threads")] ulong ignore1, [Description("In addition to configured ignored threads")] ulong ignore2)
    //{

    //}
}
