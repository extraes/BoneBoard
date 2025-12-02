using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using DSharpPlus.Entities;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Commands.ContextChecks;
using Skeleton;

namespace BoneBoard.Modules;


[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("videoroyale")]
internal class VideoRoyale : ModuleBase
{
    public VideoRoyale(BoneBot bot) : base(bot) { }

    [ThreadStatic] static HttpClient? _clint;
    static HttpClient Clint
    {
        get
        {
            _clint ??= new HttpClient();
            return _clint;
        }
    }

    static Timer? sendTimer;
    TimeOnly sendTime;
    DateTime lastSend;
    DiscordChannel? voteChannel;
    DiscordChannel? outputChannel;
    DiscordEmoji? voteEmoji;

    protected override async Task FetchGuildResources()
    {
        if (!TimeOnly.TryParse(Config.values.videoRoyaleSendTime, out TimeOnly time))
            Logger.Warn("Unable to parse video royale send time");

        if (time != default && time != sendTime)
        {
            sendTime = time;

            DateTime now = DateTime.Now;
            DateTime nextSendTime = new(new DateOnly(now.Year, now.Month, now.Day), sendTime, DateTimeKind.Local);
            
            if (nextSendTime.DayOfWeek < Config.values.videoRoyaleSendDay)
                nextSendTime = nextSendTime.AddDays(Config.values.videoRoyaleSendDay - nextSendTime.DayOfWeek);
            else if (nextSendTime.DayOfWeek > Config.values.videoRoyaleSendDay)
                nextSendTime = nextSendTime.AddDays(7 - (nextSendTime.DayOfWeek - Config.values.videoRoyaleSendDay));
            else if (nextSendTime.TimeOfDay > sendTime.ToTimeSpan())
                nextSendTime = nextSendTime.AddDays(7);

            TimeSpan waitTime = nextSendTime - now;
            Logger.Put($"Waiting {waitTime.Days}d {waitTime} to send videoroyale", LogType.Debug);
            sendTimer ??= new(SendTopVideo, null, nextSendTime - now, TimeSpan.FromDays(7));
            sendTimer.Change(nextSendTime - now, TimeSpan.FromDays(7));
        }

        try
        {
            if (Config.values.videoRoyaleVotingChannel != default)
                voteChannel = await bot.client.GetChannelAsync(Config.values.videoRoyaleVotingChannel);
            else
                Logger.Warn("No voting channel set for video royale");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to fetch voting channel for video royale", e);
        }

        try
        {
            if (Config.values.videoRoyaleSendChannel != default)
                outputChannel = await bot.client.GetChannelAsync(Config.values.videoRoyaleSendChannel);
            else
                Logger.Warn("No output channel set for video royale");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to fetch output channel for video royale", e);
        }

        try
        {
            if (ulong.TryParse(Config.values.royaleVoteEmoji, out ulong emojiId))
                voteEmoji = DiscordEmoji.FromGuildEmote(bot.client, emojiId);
            else if (DiscordEmoji.TryFromUnicode(Config.values.royaleVoteEmoji, out DiscordEmoji unicodeEmoji))
                voteEmoji = unicodeEmoji;
            else
                Logger.Warn("No (valid) vote emoji set for video royale");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to set vote emoji for video royale", e);
        }

    }

    private async void SendTopVideo(object? state)
    {
        if (lastSend > DateTime.Now - TimeSpan.FromMinutes(1))
        {
            Logger.Warn("Image Royale send timer triggered too soon, ignoring");
            Logger.Warn("Callstack: " + Environment.StackTrace);
            sendTimer?.Change(TimeSpan.FromDays(1), TimeSpan.FromDays(1));
            return;
        }
        lastSend = DateTime.Now;

        // locally declared because .NET GC can handle it and i want to be able to modify these with hot code replace :^)
        string[] possibleMessageStrings = ImageRoyale.GetRoyaleStrings();

        try
        {
            if (voteChannel is null || outputChannel is null)
            {
                Logger.Warn("Unable to send top video, voting or output channel is null");
                return;
            }

            if (voteEmoji is null)
            {
                Logger.Warn("Unable to send top video, vote emoji is null");
                return;
            }

            int topVotes = 0;
            DiscordMessage? topMessage = null;

            // ensures more recent submissions are processed later
            foreach (ulong messageId in PersistentData.values.videoRoyaleSubmissions.Values.OrderBy(id => id))
            {
                DiscordMessage? msg = await TryFetchMessage(voteChannel, messageId, true);
                if (msg is null)
                {
                    Logger.Warn($"Unable to find message w/ ID {messageId} for video royale -- ignoring");
                    continue;
                }
                int voteCount = await GetReactionsThatCount(msg);

                // if a more recent submission got the same number of votes, make it the frontrunner instead
                if (voteCount >= topVotes)
                {
                    topVotes = voteCount;
                    topMessage = msg;
                }

                try
                {
                    await msg.UnpinAsync();
                }
                catch (Exception ex)
                {
                    Logger.Warn("exception unpinning image royale message from vote channel!", ex);
                }
            }


            if (topMessage is null)
            {
                Logger.Warn("No top message found for video royale");
                return;
            }

            foreach (ulong userId in PersistentData.values.videoRoyaleSubmissions.Keys)
            {
                if (PersistentData.values.videoRoyaleSubmissions.TryGetValue(userId, out ulong msgId) && msgId == topMessage.Id)
                {
                    Logger.Put($"Giving user w/ ID {userId} points from winning Image Royale!");
                    bot.casino.GivePoints(userId, 10 * 1000);
                }
            }
            
            Logger.Put($"Top videoroyale message recieved {topVotes} votes @ {topMessage.JumpLink}");


            var builder = new DiscordMessageBuilder()
                .WithContent($"[A gift from your benefactors...]({topMessage.Attachments[0].Url ?? throw new Exception("Discord's API is some ass! Who knew? An attachment URL was somehow NULL. bruh!")})" +
                "\n-# " + possibleMessageStrings.Random());

            var outputMsg = await outputChannel.SendMessageAsync(builder);
            Logger.Put($"Sent video royale message! Link: {outputMsg.JumpLink}");

            PersistentData.values.videoRoyaleSubmissions.Clear();
            PersistentData.WritePersistentData();

            try
            {
                await voteChannel.SendMessageAsync($"the winrar was {topMessage.JumpLink}, now go check out the post in {outputChannel.Name}, {outputMsg.JumpLink}");
            }
            catch(Exception ex)
            {
                Logger.Warn("exception sending notification message in vote channel!", ex);
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to send top video", e);
        }
    }

    private async Task<int> GetReactionsThatCount(DiscordMessage msg)
    {
        if (voteEmoji is null)
            return 0;

        int counter = 0;
        try
        {
            await foreach (DiscordUser reactor in msg.GetReactionsAsync(voteEmoji))
            {
                if (reactor.IsBot)
                    continue;

                if (PersistentData.values.videoRoyaleSubmissions.TryGetValue(reactor.Id, out ulong msgId) && msgId == msg.Id)
                {
                    Logger.Put($"lol {reactor} reacted to their own message, laugh at them, that shit not counted");
                    continue;
                }

                counter++;
            }

            return counter;
        }
        catch(Exception ex)
        {
            Logger.Error($"Exception while counting ractions on {msg}", ex);

            return 0;
        }
    }

    // not needed - mutually exclusive voting not needed 
    //protected override Task ReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args)
    //{
    //    throw new NotImplementedException();
    //}

    [Command("sendnow"), Description("Force-sends the top video immediately")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task SendNow(SlashCommandContext ctx)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        BoneBot.Bots[ctx.Client].videoRoyale.SendTopVideo(null);
    }

    [Command("submit"), Description("Submit a video!")]
    public static async Task Submit(SlashCommandContext ctx, DiscordAttachment video)
    {
        if (ctx.User is not DiscordMember member || ctx.Guild is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        if (!member.Roles.Any(r => r.Id == Config.values.videoRoyaleSubmitRole))
        {
            await ctx.RespondAsync("https://tenor.com/view/ignore-this-pls-gif-24452155", true);
            return;
        }

        VideoRoyale royale = BoneBot.Bots[ctx.Client].videoRoyale;

        if (royale.voteChannel is null)
        {
            await ctx.RespondAsync("voting channel not set", true);
            return;
        }

        if (PersistentData.values.videoRoyaleSubmissions.TryGetValue(member.Id, out ulong preexistingMsgId))
        {
            await ctx.RespondAsync($"you already submitted a video, see it [here](https://discord.com/channels/{ctx.Guild.Id}/{royale.voteChannel.Id}/{preexistingMsgId})", true);
            return;
        }

        if (!(video.MediaType?.StartsWith("video") ?? false))
        {
            Logger.Put($"Non-video file (MIME '{video.MediaType ?? "null!!"}') submitted to video royale  " + video.Url);
            await ctx.RespondAsync("not a video bwomp", true);
            return;
        }
        
        long filesizeLimit = ctx.Guild.PremiumTier switch
        {
            DiscordPremiumTier.Tier_1 => 10 * 1024 * 1024,
            DiscordPremiumTier.Tier_2 => 50 * 1024 * 1024,
            DiscordPremiumTier.Tier_3 => 100 * 1024 * 1024,
            _ => 10 * 1024 * 1024
        };

        if (video.FileSize > filesizeLimit)
        {
            await ctx.RespondAsync("file too big", true);
            return;
        }

        string? extension = Path.GetExtension(video.FileName)?.Split('?')[0];
        if (extension is null)
        {
            Logger.Put("Couldn't find a file extension for video submission @ " + video.Url);
            await ctx.RespondAsync("uhhhh no extension? tell the dev to look for the url " + video.Url, true);
            return;
        }

        string tmpPath = Path.GetTempFileName();

        try
        {
            using var fs = File.OpenWrite(tmpPath);
            using var stream = await Clint.GetStreamAsync(video.ProxyUrl);
            await stream.CopyToAsync(fs);
        }
        catch (Exception e)
        {
            Logger.Error("Failed to write video to temp file for video submission", e);
            await ctx.RespondAsync("failed to write video to temp file", true);
            return;
        }

        try
        {
            using var fs = File.OpenRead(tmpPath);

            var dumb = new DiscordMessageBuilder()
                .WithContent($"VIDEO ROYALE CONTENDER {PersistentData.values.videoRoyaleSubmissions.Count + 1}")
                .AddFile("videoroyale" + extension, fs);

            Logger.Put($"{member} submitted a file originally named {video.FileName} to video royale");
                
            DiscordMessage msg = await royale.voteChannel.SendMessageAsync(dumb);
            PersistentData.values.videoRoyaleSubmissions[member.Id] = msg.Id;
            PersistentData.WritePersistentData();

            if (royale.voteEmoji is not null)
                await BoneBot.TryReact(msg, royale.voteEmoji);

            await ctx.RespondAsync($"submitted video, check https://discord.com/channels/{ctx.Guild.Id}/{royale.voteChannel.Id}", true);

            try
            {
                await msg.PinAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn("exception pinning video royale message in vote channel!", ex);
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to send video to voting channel", e);
            try
            {
                await ctx.RespondAsync("failed to send video to voting channel", true);
            }
            catch { }
            return;
        }
        
        if (File.Exists(tmpPath))
            File.Delete(tmpPath);
    }
}
