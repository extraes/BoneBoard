using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using SixLabors.ImageSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules;

internal class Stargrid : ModuleBase
{
    DiscordChannel? outputChannel;
    public Stargrid(BoneBot bot) : base(bot)
    {
        //todo: remove this, somehow reaction add events dont get fucking registered or their events arent propagated? idk
        bot.clientBuilder.ConfigureEventHandlers(x => x.HandleMessageReactionAdded(ReactionAdded));
    }

    protected override Task FetchGuildResources()
    {
        foreach (HashSet<DiscordChannel> channels in bot.allChannels.Values)
        {
            DiscordChannel? ch = channels.FirstOrDefault(x => x.Id == Config.values.outputChannel);
            if (ch is not null)
            {
                outputChannel = ch;
                break;
            }
        }

        return Task.CompletedTask;
    }

    public async Task PerformQuote(DiscordMessage msg, DiscordEmoji? triggeredEmoji)
    {
        if (msg.Author is null && msg.Channel is not null)
        {
            DiscordMessage? message = await TryFetchMessage(msg.Channel, msg.Id);
            if (message is not null) msg = message;
        }

        if (outputChannel is null)
        {
            Logger.Error("Unable to perform quote! Output channel is null!");
            return;
        }
        if (bot.logChannel is null)
        {
            Logger.Warn("Unrecommended to continue performing quotes! Log channel is null!");
        }

        if (msg.Author is not null && Config.values.blockedUsers.Contains(msg.Author.Id))
        {
            Logger.Put("Bailing on quote. Message author is in blocked user list");
            return;
        }

        if (triggeredEmoji is not null)
        {
            bool reactionSuccess = await BoneBot.TryReact(msg, triggeredEmoji);
            if (!reactionSuccess)
            {
                Logger.Warn($"Bot is blocked by {msg.Author} or cannot react, cannot react so will not continue quoting.");
                bot.logChannel?.SendMessageAsync($"Bot is (probably?) blocked by {msg.Author} or otherwise cannot react, so will not continue quoting {msg.JumpLink}");
            }
        }


        string displayName = msg.Author is DiscordMember member ? member.DisplayName : msg.Author?.GlobalName ?? msg.Author?.Username ?? "A user";
        using Image? quote = await Quoter.GenerateImageFrom(msg, bot.client);
        if (quote is null)
        {
            Logger.Warn("Failed to generate quote image for message " + msg.JumpLink);
            return;
        }

        using MemoryStream ms = new();

        await quote.SaveAsPngAsync(ms);
        ms.Position = 0;

        DiscordMessageBuilder dmb = new DiscordMessageBuilder()
                                        .AddFile("quote.png", ms)
                                        .WithContent($"[From {displayName.Replace("]", "")}]({msg.JumpLink})");

        try
        {
            await outputChannel.SendMessageAsync(dmb);
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while trying to send message for quote process's final step!", ex);
        }
    }

    protected override async Task ReactionAdded(DiscordClient client, MessageReactionAddedEventArgs args)
    {
        await HandleQuoteeDeleteRequest(args);

        if (!Config.values.requiredEmojis.Contains(args.Emoji.Id)) return;
        if (args.Channel.IsPrivate || args.User is not DiscordMember member) return;
        if (!MemberReactionCounts(member)) return;

        List<DiscordMember> membersThatReacted;
        try
        {
            membersThatReacted = new();
            await foreach (DiscordUser user in args.Message.GetReactionsAsync(args.Emoji))
            {
                //todo: remove this check and rely on internal cache maybe
                if (bot.IsMe(user))
                    return; // already reacted

                DiscordMember memb = await args.Guild.GetMemberAsync(user.Id);

                membersThatReacted.Add(memb);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch reactions on " + args.Message.ToString(), ex);
            return;
        }


        int validReactions = membersThatReacted.Where(MemberReactionCounts).Count();
        Logger.Put($"Valid reactions on {args.Message}: {validReactions}");
        if (validReactions < Config.values.requiredReactionCount)
            return;

        try
        {
            Logger.Put("Now quoting message " + args.Message);
            await PerformQuote(args.Message, args.Emoji);
        }
        catch (Exception ex)
        {
            if (bot.logChannel is not null)
                await bot.logChannel.SendMessageAsync($"Failed to quote [a message]({args.Message.JumpLink}), see exception below for details\n" + ex);

            Logger.Error($"Failed to quote a message {args.Message.JumpLink}", ex);
        }
    }

    bool MemberReactionCounts(DiscordMember member)
    {
        if (member.IsBot || Config.values.blockedUsers.Contains(member.Id))
            return false;

        foreach (DiscordRole role in member.Roles)
        {
            if (Config.values.requiredRoles.Contains(role.Id)) return true;
        }

        return false;
    }

    private async Task HandleQuoteeDeleteRequest(MessageReactionAddedEventArgs args)
    {
        if (args.Channel.Id != Config.values.outputChannel)
            return;

        DiscordMessage msg = args.Message;
        bool isCached = !(msg.Author is null && string.IsNullOrEmpty(msg.Content));

        // handles reactions on uncached messages
        if (!isCached)
        {
            try
            {
                DiscordMessage? message = await TryFetchMessage(args.Channel, msg.Id);
                if (message is not null) msg = message;
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to fetch potential quote message for ReactionAdded event: ", ex);
                return;
            }
        }


        bool isQuote = bot.IsMe(msg.Author) && msg.Attachments.Count == 1;

        if (isQuote && (args.Emoji.Id != 0 ? args.Emoji.Id.ToString() : args.Emoji.Name) == Config.values.quoteeDeleteEmoji)
        {
            int parenIdx = msg.Content.LastIndexOf('(');
            string jumpLink = msg.Content.Substring(parenIdx + 1, msg.Content.Length - parenIdx - 2);
            DiscordMessage? originalMessage = await bot.GetMessageFromLink(jumpLink);
            if (originalMessage is not null && originalMessage.Author is not null && args.User == originalMessage.Author)
            {
                Logger.Put("Deleting quote message " + msg + " because the quotee/original author requested it.");
                await msg.DeleteAsync();
            }
        }
    }
}
