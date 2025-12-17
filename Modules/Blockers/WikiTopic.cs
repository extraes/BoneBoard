using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WikiClientLibrary;
using WikiClientLibrary.Client;
using WikiClientLibrary.Generators;
using WikiClientLibrary.Pages;
using WikiClientLibrary.Sites;
using static System.Net.WebRequestMethods;

namespace BoneBoard.Modules.Blockers;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("wikitopic")]
internal partial class WikiTopic : ModuleBase
{
    public WikiTopic(BoneBot bot) : base(bot)
    {
        Config.ConfigChanged += () => { clint = null; wikiClint = null; topicStr = null; };
    }
    const int HOURS_PER_TOPIC_CHANGE = 4;
    Dictionary<ulong, string> whyAUsersMessageWasDeleted = new();
    Dictionary<ulong, DiscordMessage> statusMessages = new();
    WikiClient? wikiClint;
    OpenAIClient? clint;
    string? topicStr;
    CancellationTokenSource topicRollover = new();

    protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        bool isTopicStale = PersistentData.values.lastTopicSwitchTime.AddHours(HOURS_PER_TOPIC_CHANGE) < DateTime.Now;
        if (isTopicStale || string.IsNullOrEmpty(PersistentData.values.currentWikiTopic))
            await SetNewTopic();
        else if (string.IsNullOrWhiteSpace(topicStr) && !string.IsNullOrEmpty(PersistentData.values.currentWikiTopic))
        {
            var saved = PersistentData.values.lastTopicSwitchTime;
            await SetNewTopic(PersistentData.values.currentWikiTopic, false);
            PersistentData.values.lastTopicSwitchTime = saved; // dont reset the timer when starting up
        }

        TopicRolloverLoop(topicRollover.Token);
    }

    void RestartRollover()
    {
        topicRollover.Cancel();
        topicRollover = new CancellationTokenSource();
        TopicRolloverLoop(topicRollover.Token);
    }

    async void TopicRolloverLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan timeToWait = PersistentData.values.lastTopicSwitchTime.AddHours(HOURS_PER_TOPIC_CHANGE) - DateTime.Now;
            // check every 60sec for a channel to be set in case one gets set while running 
            if (timeToWait.TotalMilliseconds <= 0)
            {
                timeToWait = TimeSpan.FromMinutes(1);
            }
            
            try
            {
                await Task.Delay(timeToWait, token);
            }
            catch { }
            if (token.IsCancellationRequested)
            {
                Logger.Put($"Topic rollover cancelled! Presumably a new loop has started up? Hopefully at least!");
                return;
            }
            await SetNewTopic();
        }
    }

    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        { 
            _ = MessageCheckAsync(msgCreatedArgs.Message, eventArgs);
        }
        
        if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            _ = MessageCheckAsync(msgUpdatedArgs.Message, eventArgs);
        }

        return false;
    }

    private async Task<bool> MessageCheckAsync(DiscordMessage msg, DiscordEventArgs args)
    {
        if (bot.IsMe(msg.Author) || msg.Author is null)
            return false;
        if (!Config.values.channelsWhereMessagesMustBeOnTopic.Contains(msg.ChannelId))
            return false;


        string content = msg.Content;
        if (msg.Reference?.Type == DiscordMessageReferenceType.Forward && (msg.MessageSnapshots?.Count ?? 0) > 0)
            content = string.Join('\n', msg.MessageSnapshots!.Select(m => m.Message.Content));
        if (content.Length == 0)
            return false;
        if (string.IsNullOrWhiteSpace(topicStr))
        {
            if (string.IsNullOrWhiteSpace(PersistentData.values.currentWikiTopic))
                return false;
            else
            {
                var saved = PersistentData.values.lastTopicSwitchTime;
                await SetNewTopic(PersistentData.values.currentWikiTopic);
                PersistentData.values.lastTopicSwitchTime = saved; // dont reset the timer just bc someone sent a message
            }
        }

        if (topicStr is null)
        {
            Logger.Warn("No topic currently set!");
            return false;
        }

        string cleanContent = DSharpPlus.Formatter.Strip(content);

        clint ??= new OpenAIClient(Config.values.openAiToken);

        var chatClint = clint.GetChatClient(Config.values.wikiTopicModel);
        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(Config.values.wikiTopicSystemPrompt),
            ChatMessage.CreateUserMessage(topicStr),
            ChatMessage.CreateUserMessage(cleanContent),
        };
        var res = await chatClint.CompleteChatAsync(messages);

        foreach (var part in res.Value.Content)
        {
            if (part.Text.Contains("\"on_topic\": false"))
            {
                Logger.Put($"Message {msg.Id} in channel {msg.ChannelId} was demmed to be off topic. See below for details\n{part.Text}", LogType.Normal, cleanMultiline: false);
                whyAUsersMessageWasDeleted[msg.Author.Id] = $"Beamed for the following:```\n{content.Replace("```", "'''")}```{part.Text}";
                await TryDeleteAsync(msg, "Off topic");
                DontPropagateEvent(args);
                return true;
            }
        }

        return false;
    }



    private async Task SetNewTopic(string? articleTitle = null, bool sendNewMessage = true)
    {
        if (Config.values.channelsWhereMessagesMustBeOnTopic.Count == 0)
        {
            Logger.Warn("No channels configured for wiki topic enforcement, skipping topic set");
            return;
        }

        wikiClint ??= new WikiClient()
        {
            ClientUserAgent = "boneboard/1.0",
        };
        
        var site = new WikiSite(wikiClint, "https://en.wikipedia.org/w/api.php");
        await site.Initialization;
        WikiPage? page = null;
        if (articleTitle is not null)
        {
            page = new WikiPage(site, articleTitle);
            await page.RefreshAsync(PageQueryOptions.FetchContent);
        }
        else
        {
            bool needReroll = true;
            while (page is null || needReroll)
            {
                var pageGen = new RecentChangesGenerator(site);
                pageGen.NamespaceIds = [ BuiltInNamespaces.Main ];
                await foreach (var randomPage in pageGen.EnumPagesAsync(PageQueryOptions.FetchContent))
                {
                    Logger.Put($"\"Random\" (recently edited, lol) wiki topic selected: {randomPage.Title} (Namespace id {randomPage.NamespaceId})");
                
                    var views = await GetArticlePageviewsAsync(wikiClint, "en.wikipedia.org", randomPage.Title!, DateOnly.FromDateTime(DateTime.Now.AddDays(-30)), DateOnly.FromDateTime(DateTime.Now));
                    
                    int sum = views?.items.Sum(i => i.views) ?? -1;
                    if (sum == -1)
                        continue;
                    Logger.Put($"Wiki topic '{randomPage.Title}' view count in past 30 days: {sum}");
                    needReroll = sum < Config.values.wikiTopicMinMonthlyViews;
                    needReroll |= (randomPage.Content?.Length ?? int.MaxValue) > Config.values.wikiTopicMaxLengthChars;

                    if (!needReroll)
                    {
                        page = randomPage;
                        break;
                    }
                    else
                    {
                        Logger.Put($"Rerolling wiki topic (views {sum}, length {randomPage.Content?.Length ?? -1})");
                    }
                }

                if (page is null)
                {
                    await Task.Delay(250); // give the world some time to edit more pages, lol
                }
            }

        }

        PersistentData.values.lastTopicSwitchTime = DateTime.Now;
        Logger.Put($"Settled on wiki topic {page.Title} ({page.Content?.Length ?? -1} char long)");
        topicStr = page.Content;
        PersistentData.values.currentWikiTopic = page.Title ?? "";
        if (sendNewMessage)
        {
            foreach (ulong channelId in Config.values.channelsWhereMessagesMustBeOnTopic)
            {
                // dont need to update message
                //if (!statusMessages.TryGetValue(channelId, out var oldMsg) && PersistentData.values.wikiTopicAnnounceMessages.TryGetValue(channelId, out ulong oldMsgId))
                //{
                //    try
                //    {
                //        var channel = await bot.client.GetChannelAsync(channelId);
                //        var msg = TryFetchMessage(channel, oldMsgId);
                //    }
                //    catch { }
                //}
                Logger.Put($"Posting new topic message in channel {channelId}");
                var channel = await bot.client.GetChannelAsync(channelId);
                var sentMsg = await channel.SendMessageAsync($"New topic!!! better read up on [{page.Title}](https://en.wikipedia.org/wiki/{Uri.EscapeDataString(page.Title!)}) {Formatter.Timestamp(TimeSpan.FromHours(HOURS_PER_TOPIC_CHANGE), TimestampFormat.RelativeTime)}");
                statusMessages[channelId] = sentMsg;
                PersistentData.values.wikiTopicAnnounceMessages[channelId] = sentMsg.Id;
            }
        }
        PersistentData.WritePersistentData();
        Logger.Put("Successfully changed wiki topic");
    }

    [Command("set")]
    [Description("Set the topic for the next few hours (until the next topic switch should occur)")]
    public static async Task SetTopic(
        SlashCommandContext ctx,
        [Parameter("articleTitle")] string? articleTitle = null)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;


        await ctx.DeferResponseAsync(true);
        var wikitopic = BoneBot.Bots[ctx.Client].blockers.OfType<WikiTopic>().FirstOrDefault();
        if (wikitopic is null)
        {
            await ctx.FollowupAsync("uhhhhh check for sum error or some shit in the log bc i cant find where the topic module is... uhhhhhhhhhhhhhh good luck man");
            return;
        }

        wikitopic.RestartRollover();
        await wikitopic.SetNewTopic(articleTitle, true);
        await ctx.FollowupAsync("Changed wiki topic!");
    }

    [Command("ermmmmmMods")]
    [Description("@extraes WHY WAS MY MESSAGE DELETED I WAS ON TOPIC")] 
    public static async Task AntiWhine(SlashCommandContext ctx, [Parameter("otherUser"), Description("Only usable if you're a moderator")] DiscordUser? target = null)
    {
        if (target != null && await SlashCommands.ModGuard(ctx))
            return;
        target ??= ctx.User;

        await ctx.DeferResponseAsync(true);

        var wikitopic = BoneBot.Bots[ctx.Client].blockers.OfType<WikiTopic>().FirstOrDefault();
        if (wikitopic is null)
        {
            await ctx.FollowupAsync("uhhhhh check for sum error or some shit in the log bc i cant find where the topic module is... uhhhhhhhhhhhhhh good luck man");
            return;
        }

        if (wikitopic.whyAUsersMessageWasDeleted.TryGetValue(target.Id, out string? reason))
        {
            await ctx.FollowupAsync(reason);
            return;
        }

        await ctx.FollowupAsync("nothing was found for you, get beamed");
    }
}
