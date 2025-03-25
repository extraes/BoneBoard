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

namespace BoneBoard.Modules;


[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("imageroyale")]
internal class ImageRoyale : ModuleBase
{
    public ImageRoyale(BoneBot bot) : base(bot) { }

    [ThreadStatic] static HttpClient _clint;
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
        if (!TimeOnly.TryParse(Config.values.imageRoyaleSendTime, out TimeOnly time))
            Logger.Warn("Unable to parse image royale send time");

        if (time != default && time != sendTime)
        {
            sendTime = time;

            DateTime now = DateTime.Now;
            DateTime next = new(now.Year, now.Month, sendTime.ToTimeSpan() < now.TimeOfDay ? now.Day + 1 : now.Day, sendTime.Hour, sendTime.Minute, sendTime.Second);
            if (sendTimer is null)
            {
                Logger.Warn("Creating new ImageRoyale timer, heres the callstack: " + Environment.StackTrace);
                sendTimer = new(SendTopImage, null, next - now, TimeSpan.FromDays(1));
            }
            sendTimer.Change(next - now, TimeSpan.FromDays(1));

            Logger.Put($"Waiting {next - now} to send imageroyale", LogType.Debug);
        }

        try
        {
            if (Config.values.imageRoyaleVotingChannel != default)
                voteChannel = await bot.client.GetChannelAsync(Config.values.imageRoyaleVotingChannel);
            else
                Logger.Warn("No voting channel set for image royale");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to fetch voting channel for image royale", e);
        }

        try
        {
            if (Config.values.imageRoyaleSendChannel != default)
                outputChannel = await bot.client.GetChannelAsync(Config.values.imageRoyaleSendChannel);
            else
                Logger.Warn("No output channel set for image royale");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to fetch output channel for image royale", e);
        }

        try
        {
            if (ulong.TryParse(Config.values.royaleVoteEmoji, out ulong emojiId))
                voteEmoji = DiscordEmoji.FromGuildEmote(bot.client, emojiId);
            else if (DiscordEmoji.TryFromUnicode(Config.values.royaleVoteEmoji, out DiscordEmoji unicodeEmoji))
                voteEmoji = unicodeEmoji;
            else
                Logger.Warn("No (valid) vote emoji set for image royale");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to set vote emoji for image royale", e);
        }

    }

    private async void SendTopImage(object? state)
    {
        if (lastSend > DateTime.Now - TimeSpan.FromMinutes(1))
        {
            Logger.Warn("Image Royale send timer triggered too soon, ignoring");
            Logger.Warn("Callstack: " + Environment.StackTrace);
            sendTimer?.Change(TimeSpan.FromDays(1), TimeSpan.FromDays(1));
            return;
        }
        lastSend = DateTime.Now;
        Logger.Warn("Currently sending ImageRoyale, heres the callstack: " + Environment.StackTrace);

        // locally declared because .NET GC can handle it and i want to be able to modify these with hot code replace :^)
        string[] possibleMessageStrings = GetRoyaleStrings();

        try
        {
            if (voteChannel is null || outputChannel is null)
            {
                Logger.Warn("Unable to send top image, voting or output channel is null");
                return;
            }

            if (voteEmoji is null)
            {
                Logger.Warn("Unable to send top image, vote emoji is null");
                return;
            }

            int topVotes = 0;
            DiscordMessage? topMessage = null;

            // Order by submission order, so that the most recent submissions are last
            foreach (ulong messageId in PersistentData.values.imageRoyaleSubmissions.Values.OrderBy(id => id))
            {
                DiscordMessage? msg = await TryFetchMessage(voteChannel, messageId, true);
                if (msg is null)
                {
                    Logger.Warn($"Unable to find message w/ ID {messageId} for image royale -- ignoring");
                    continue;
                }
                int voteCount = await GetReactionsThatCount(msg);

                // ensures that if a message recieves the same number of votes, it still wins if it was more recently posted
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
                PersistentData.values.imageRoyaleSubmissions.Clear();
                PersistentData.WritePersistentData();
                Logger.Warn("No top message found for image royale");
                return;
            }

            foreach (ulong userId in PersistentData.values.imageRoyaleSubmissions.Keys)
            {
                if (PersistentData.values.imageRoyaleSubmissions.TryGetValue(userId, out ulong msgId) && msgId == topMessage.Id)
                {
                    Logger.Put($"Giving user w/ ID {userId} points from winning Image Royale!");
                    bot.casino.GivePoints(userId, 10 * 1000);
                }
            }

            Logger.Put($"Top imageroyale message recieved {topVotes} votes @ {topMessage.JumpLink}");

            var builder = new DiscordMessageBuilder()
                .WithContent("-# " + possibleMessageStrings.Random())
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithImageUrl(topMessage.Attachments[0].Url ?? throw new Exception("Discord's API is some ass! Who knew? An attachment URL was somehow NULL. bruh!"))
                    .WithFooter("A gift from your benefactors..."));

            var outputMsg = await outputChannel.SendMessageAsync(builder);
            Logger.Put($"Sent image royale message! Link: {outputMsg.JumpLink}");

            PersistentData.values.imageRoyaleSubmissions.Clear();
            PersistentData.WritePersistentData();

            try
            {
                await voteChannel.SendMessageAsync($"the winrar was {topMessage.JumpLink}, now go check out the post in {outputChannel.Name}, {outputMsg.JumpLink}");
            }
            catch (Exception ex)
            {
                Logger.Warn("exception sending notification message in vote channel!", ex);
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to send top image", e);
        }
    }

    public static string[] GetRoyaleStrings()
    {
        return [
            "Consider the following",
            "yall seen this?",
            "what yall know bout this?",
            "your mom just sent me this",
            "they found this on bin laden's hard drive",
            "im gonna get pulled over and show the cop this on my phone",
            "ummm i uhhh ummmm uhmmm uhhhh",
            "woaw",
            "i paid some dude $200 for the newest iphone but he ghosted me and sent me this",
            "scientists attempted to reconstruct what someone was looking at after capturing brain signals. this is the result",
            "this is the last thing my wife sent me before the divorce",
            "As previously stated,",
            "I hope this email finds you well,",
            "chat is this real",
            "i got an email from someone i dont know, and it had this in it... at least it wasnt a screamer",
            "As per my previous correspondance,",
            "so why dont *i* have slowmode?",
            "wtf is this shit bro",
            "ts is frying me 😭😭😭😭💔💔",
            "ts pmo icl",
            "google 'jerry seinfeld 17 38' to learn about his collab with fetty wap",
            "EXPLODING YOU WITH MY MIND 💥💥💥",
        ];
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

                if (PersistentData.values.imageRoyaleSubmissions.TryGetValue(reactor.Id, out ulong msgId) && msgId == msg.Id)
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

    [Command("sendnow"), Description("Force-sends the top image immediately")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MODERATOR_PERMS)]
    public static async Task SendNow(SlashCommandContext ctx)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        BoneBot.Bots[ctx.Client].imageRoyale.SendTopImage(null);
    }

    [Command("submit"), Description("Submit an image or gif!")]
    public static async Task Submit(SlashCommandContext ctx, DiscordAttachment image)
    {
        if (ctx.User is not DiscordMember member || ctx.Guild is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        if (!member.Roles.Any(r => r.Id == Config.values.imageRoyaleRole))
        {
            await ctx.RespondAsync("https://tenor.com/view/ignore-this-pls-gif-24452155", true);
            return;
        }

        ImageRoyale royale = BoneBot.Bots[ctx.Client].imageRoyale;

        if (royale.voteChannel is null)
        {
            await ctx.RespondAsync("voting channel not set", true);
            return;
        }

        if (PersistentData.values.imageRoyaleSubmissions.TryGetValue(member.Id, out ulong preexistingMsgId))
        {
            await ctx.RespondAsync($"you already submitted an image, see it [here](https://discord.com/channels/{ctx.Guild.Id}/{royale.voteChannel.Id}/{preexistingMsgId})", true);
            return;
        }

        if (!(image.MediaType?.StartsWith("image") ?? false))
        {
            Logger.Put($"Non-image file (MIME '{image.MediaType ?? "null!!"}') submitted to image royale  " + image.Url);
            await ctx.RespondAsync("not an image bwomp", true);
            return;
        }
        
        long filesizeLimit = ctx.Guild.PremiumTier switch
        {
            DiscordPremiumTier.Tier_1 => 10 * 1024 * 1024,
            DiscordPremiumTier.Tier_2 => 50 * 1024 * 1024,
            DiscordPremiumTier.Tier_3 => 100 * 1024 * 1024,
            _ => 10 * 1024 * 1024
        };

        if (image.FileSize > filesizeLimit)
        {
            await ctx.RespondAsync("file too big", true);
            return;
        }

        string? extension = Path.GetExtension(image.FileName)?.Split('?')[0];
        if (extension is null)
        {
            Logger.Put("Couldn't find a file extension for image submission @ " + image.Url);
            await ctx.RespondAsync("uhhhh no extension? tell the dev to look for the url " + image.Url, true);
            return;
        }

        string tmpPath = Path.GetTempFileName();

        try
        {
            using var fs = File.OpenWrite(tmpPath);
            using var stream = await Clint.GetStreamAsync(image.ProxyUrl);
            await stream.CopyToAsync(fs);
        }
        catch (Exception e)
        {
            Logger.Error("Failed to write image to temp file for image submission", e);
            await ctx.RespondAsync("failed to write image to temp file", true);
            return;
        }

        try
        {
            using var fs = File.OpenRead(tmpPath);

            var dumb = new DiscordMessageBuilder()
                .WithContent($"CONTENDER {PersistentData.values.imageRoyaleSubmissions.Count + 1}")
                .AddFile("imageroyale" + extension, fs);

            Logger.Put($"{member} submitted a file originally named {image.FileName} to image royale");
                
            DiscordMessage msg = await royale.voteChannel.SendMessageAsync(dumb);
            PersistentData.values.imageRoyaleSubmissions[member.Id] = msg.Id;
            PersistentData.WritePersistentData();

            if (royale.voteEmoji is not null)
                await BoneBot.TryReact(msg, royale.voteEmoji);

            await ctx.RespondAsync($"submitted image, check https://discord.com/channels/{ctx.Guild.Id}/{royale.voteChannel.Id}", true);

            try
            {
                await msg.PinAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn("exception pinning image royale message in vote channel!", ex);
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to send image to voting channel", e);
            try
            {
                await ctx.RespondAsync("failed to send image to voting channel", true);
            }
            catch { }
            return;
        }
        
        if (File.Exists(tmpPath))
            File.Delete(tmpPath);
    }
}
