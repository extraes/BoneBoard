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
using WikiClientLibrary.Client;
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

    Dictionary<ulong, DiscordMessage> statusMessages = new();
    WikiClient? wikiClint;
    OpenAIClient? clint;
    string? topicStr;
    CancellationTokenSource topicRollover = new();

    protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        bool isTopicStale = PersistentData.values.lastTopicSwitchTime.AddHours(12) < DateTime.Now;
        if (isTopicStale || string.IsNullOrEmpty(PersistentData.values.currentWikiTopic))
            await SetNewTopic();
        else if (string.IsNullOrWhiteSpace(topicStr) && !string.IsNullOrEmpty(PersistentData.values.currentWikiTopic))
            await SetNewTopic(PersistentData.values.currentWikiTopic);

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
            TimeSpan timeToWait = PersistentData.values.lastTopicSwitchTime.AddHours(12) - DateTime.Now;
            if (timeToWait.TotalMilliseconds > 0)
            {
                try
                {
                    await Task.Delay(timeToWait, token);
                }
                catch { }
            }
            if (token.IsCancellationRequested)
                break;
            await SetNewTopic();
        }
    }

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

    private async Task<bool> MessageCheckAsync(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
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
                await SetNewTopic(PersistentData.values.currentWikiTopic);
            }
        }

        if (topicStr is null)
        {
            Logger.Warn("SetNewTopic failed to set a topic");
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
                await TryDeleteAsync(msg, "Off topic");
                return true;
            }
        }

        return false;
    }



    private async Task SetNewTopic(string? articleTitle = null)
    {
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
            await page.RefreshAsync(PageQueryOptions.ResolveRedirects);
        }
        else
        {
            bool needReroll = true;
            while (page is null || needReroll)
            {
                page = new WikiPage(site, "Special:Random");
                await page.RefreshAsync(PageQueryOptions.ResolveRedirects);
                Logger.Put($"Random wiki topic selected: {page.Title}");
                
                var views = await GetArticlePageviewsAsync(wikiClint, "en.wikipedia.org", Uri.EscapeDataString(page.Title!), DateOnly.FromDateTime(DateTime.Now.AddDays(-30)), DateOnly.FromDateTime(DateTime.Now));
                int sum = views.items.Sum(i => i.views);
                Logger.Put($"Wiki topic '{page.Title}' view count in past 30 days: {sum}");
                needReroll = sum < Config.values.wikiTopicMinMonthlyViews;
                needReroll |= (page.Content?.Length ?? int.MaxValue) > Config.values.wikiTopicMaxLengthChars;
            }
        }

        Logger.Put($"Settled on wiki topic {page.Title} ({page.Content?.Length ?? -1} char long)");
        topicStr = page.Content;
        PersistentData.values.lastTopicSwitchTime = DateTime.Now;
        PersistentData.values.currentWikiTopic = page.Title ?? "";
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
            var sentMsg = await channel.SendMessageAsync($"New topic!!! better read up on [{page.Title}](https://en.wikipedia.org/wiki/{Uri.EscapeDataString(page.Title!)}) {Formatter.Timestamp(TimeSpan.FromHours(12), TimestampFormat.RelativeTime)}");
            statusMessages[channelId] = sentMsg;
            PersistentData.values.wikiTopicAnnounceMessages[channelId] = sentMsg.Id;
        }
        Logger.Put("Successfully changed wiki topic");
    }

    [Command("set")]
    [Description("Set the topic for the next 12 hours")]
    public static async Task SetTopic(
        SlashCommandContext ctx,
        [Parameter("articleTitle")] string articleTitle)
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

        await wikitopic.SetNewTopic(articleTitle);
        await ctx.FollowupAsync("Changed wiki topic!");
    }
}
