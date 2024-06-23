using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Commands.Processors.SlashCommands;
using System.ComponentModel;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Trees.Metadata;

namespace BoneBoard;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("casino")]
internal class Casino
{
    const string BLACKJACK_NO_OP = "bb.bj.noop";
    // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action} (.Split idx's)
    const string BLACKJACK_INTERACTION_FORMAT = "bb.bj.{0}.{1}.{2}.{3}.{4}.{5}"; // .Format idx's
    const string BLACKJACK_ACTION_HIT = "h";
    const string BLACKJACK_ACTION_STAND = "s";

    string[] slotMachineEmojis = // courtesy of chatgpt
    {
        "🍒", // Cherry
        "🍋", // Lemon
        "🍇", // Grapes
        "🍊", // Orange
        "🍉", // Watermelon
        "🍓", // Strawberry
        "🔔", // Bell
        "💰", // Money Bag
        "💎", // Gem
        "🎰"  // Slot Machine
    };

    static TimeSpan pointsTimeout = TimeSpan.FromMinutes(15);
    Dictionary<DiscordUser, DateTime> lastTimePointsGotten = new();
    BoneBot bot;

    public Casino(BoneBot bot)
    {
        this.bot = bot;

        bot.ConfigureEvents(e =>
        {
            e.HandleMessageCreated(HandleCasino)
            .HandleComponentInteractionCreated(DispatchInteractions);
        });
    }

    private async Task DispatchInteractions(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Interaction.Data.CustomId))
            return;

        if (!args.Interaction.Data.CustomId.StartsWith("bb"))
            return;

        string[] splits = args.Interaction.Data.CustomId.Split('.');
        switch(splits[1])
        {
            case "bj":
                await HandleBlackjackInteraction(args.Interaction, args.Message, splits);
                break;
        }
    }

    private async Task HandleCasino(DiscordClient sender, MessageCreatedEventArgs args)
    {
        if (args.Author is not DiscordMember member)
            return;

        if (member.Roles.All(r => !Config.values.casinoRoleIds.Contains(r.Id)))
            return;

        if (lastTimePointsGotten.TryGetValue(member, out var lastTime) && DateTime.Now - lastTime < pointsTimeout)
            return;

        lastTimePointsGotten[member] = DateTime.Now;

        int points = Random.Shared.Next(1, 5) * 100;
        GivePoints(member, points);
    }

    internal void GivePoints(DiscordMember member, int points)
    {
        if (PersistentData.values.casinoPoints.TryGetValue(member.Id, out var currentPoints))
            PersistentData.values.casinoPoints[member.Id] = currentPoints + points;
        else
            PersistentData.values.casinoPoints[member.Id] = points;

        PersistentData.WritePersistentData();
    }

    internal async Task CheckPoints(SlashCommandContext ctx, bool ephemeral = true)
    {
        if (!PersistentData.values.casinoPoints.TryGetValue(ctx.User.Id, out int points) || points == 0)
        {
            //await ctx.RespondAsync("You don't have any points to gamble!");
            await ctx.RespondAsync("https://tenor.com/view/broke-no-cash-gif-25565154", ephemeral);
            return;
        }

        await ctx.RespondAsync($"{((ephemeral || ctx.Member is null) ? "You" : ctx.Member.DisplayName)} {(ephemeral ? "have" : "has")} {points} point{(points == 1 ? "" : "s")}!", ephemeral);
    }

    internal async Task GambleSlots(SlashCommandContext ctx, int amountGambled, bool ephemeral = true)
    {
        try
        {
            if (!PersistentData.values.casinoPoints.TryGetValue(ctx.User.Id, out int hasPoints) || hasPoints < amountGambled)
            {
                await ctx.RespondAsync($"You don't have {amountGambled} points to gamble! You only have {hasPoints} points!", ephemeral);
                return;
            }
            else if (amountGambled < 100)
            {
                await ctx.RespondAsync("lol. broke bitch. try over a hundred next time.", ephemeral);
                return;
            }

            await ctx.DeferResponseAsync();

            int winAmount = amountGambled * 20;
            
            DiscordMessage followup = await ctx.FollowupAsync("Rolling...", ephemeral);

            var builder = new DiscordWebhookBuilder();

            int spinCount = Random.Shared.Next(2, 9);

            for (int i = 0; i < spinCount; i++)
            {
                await Task.Delay(1000);

                string emojis = slotMachineEmojis.Random() + " " + slotMachineEmojis.Random() + " " + slotMachineEmojis.Random();
                builder.WithContent("Rolling...\n" + emojis);
                await ctx.EditFollowupAsync(followup.Id, builder);
            }


            await Task.Delay(2000);

            string emojisFinal = slotMachineEmojis.Random() + " " + slotMachineEmojis.Random() + " " + slotMachineEmojis.Random();
            //if (Config.values.owners.Contains(ctx.User.Id))
            //    emojisFinal = "🎰 🎰 🎰";
            string[] emojisFinalSplit = emojisFinal.Split(' ').ToArray();
            string[] emojisFinalDistinct = emojisFinalSplit.Distinct().ToArray();
            builder.WithContent("Rolling....\n" + emojisFinal);
            await ctx.EditFollowupAsync(followup.Id, builder);
            await Task.Delay(1000);


            // as if the above code wasnt bad... below is even fucking worse. BLUGH.
            if (emojisFinalDistinct.Length == 1)
            {
                if (emojisFinalDistinct[0] == "🎰")
                    winAmount *= 20;
                builder.WithContent($"{builder.Content}\nYou won {winAmount} points! You now have {hasPoints + winAmount}!");
                PersistentData.values.casinoPoints[ctx.User.Id] += winAmount;
            }
            else if (emojisFinalDistinct.Length == 2)
            {
                if (emojisFinalSplit.Count(str => str == "🎰") == 2)
                {

                    if (emojisFinalSplit[0] == emojisFinalSplit[2])
                    {
                        builder.WithContent($"{builder.Content}\nTwo slots, double your points! You have {hasPoints + amountGambled} points!");
                        PersistentData.values.casinoPoints[ctx.User.Id] += amountGambled;
                    }
                    else
                    {
                        builder.WithContent($"{builder.Content}\nTwo slots in a row, you quintuple your points! You now have {hasPoints + amountGambled * 5} points!");
                        PersistentData.values.casinoPoints[ctx.User.Id] += amountGambled * 5;
                    }

                }
                else
                {
                    if (emojisFinalSplit[0] == emojisFinalSplit[2])
                        builder.WithContent($"{builder.Content}\nTwo of a kind, you keep your points! You have {hasPoints} points!");
                    else
                    {
                        builder.WithContent($"{builder.Content}\nTwo in a row, you double your points! You now have {hasPoints + amountGambled} points!");
                        PersistentData.values.casinoPoints[ctx.User.Id] += amountGambled;
                    }
                }
            }
            else
            {
                builder.WithContent($"{builder.Content}\nYou lost {amountGambled} points! You now have {hasPoints - amountGambled}");
                PersistentData.values.casinoPoints[ctx.User.Id] -= amountGambled;
            }

            await ctx.EditFollowupAsync(followup.Id, builder);
            PersistentData.WritePersistentData();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error while gambling with {ctx.User}", ex);
        }
    }

    internal async Task BeginBlackjack(SlashCommandContext ctx, int wager, bool ephemeral)
    {
        if (!PersistentData.values.casinoPoints.TryGetValue(ctx.User.Id, out int hasPoints) || hasPoints < wager)
        {
            await ctx.RespondAsync($"You don't have {wager} points to gamble! You only have {hasPoints} points!", true);
            return;
        }
        else if (wager == 100)
        {
            await ctx.RespondAsync("what do you think this is, a slot machine???? drop and gimme 500!", true);
            return;
        }
        else if (wager < 500)
        {
            await ctx.RespondAsync("some time you gotta bag bag to boss up. try half a grand next time.", true);
            return;
        }
        
        var deck = new Cards.Deck();
        deck.Shuffle();

        await ctx.DeferResponseAsync(ephemeral);

        var dfmb = new DiscordFollowupMessageBuilder()
            .AsEphemeral(ephemeral)
            .WithContent("Starting...");

        DiscordMessage followup = await ctx.FollowupAsync(dfmb);

        Logger.Put("Followup ID: " + followup.Id);

        // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action}
        // const string BLACKJACK_INTERACTION_FORMAT = "bb.bj.{0}.{1}.{2}.{3}.{4}.{5}";

        string buttonIdBase = string.Format(BLACKJACK_INTERACTION_FORMAT, followup.Id, ctx.User.Id, "{0}", "{1}", wager, "{2}");

        var hitButtonGrayed = new DiscordButtonComponent(DiscordButtonStyle.Danger, BLACKJACK_NO_OP + ".a", "Hit", true);
        var stayButtonGrayed = new DiscordButtonComponent(DiscordButtonStyle.Primary, BLACKJACK_NO_OP + ".b", "Stay", true);

        List<Cards.Card> dealerHand = new();
        List<Cards.Card> playerHand = new();
        string display = GenerateBlackjackString(wager, dealerHand, playerHand);
        var builder = new DiscordWebhookBuilder()
            .AddComponents(hitButtonGrayed, stayButtonGrayed);

        // yes i know local functions are bad. i dont care.
        async Task UpdateDisplay()
        {
            display = GenerateBlackjackString(wager, dealerHand, playerHand);
            builder.WithContent(display);
            await ctx.EditFollowupAsync(followup.Id, builder);
        }

        await UpdateDisplay();

        await Task.Delay(1500);

        dealerHand.Add(deck.Draw());
        await UpdateDisplay();

        await Task.Delay(1500);

        playerHand.Add(deck.Draw());
        await UpdateDisplay();

        await Task.Delay(1500);

        playerHand.Add(deck.Draw());
        
        // update with workable buttons
        string hitId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand), BLACKJACK_ACTION_HIT);
        string standId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand), BLACKJACK_ACTION_STAND);

        var hitButton = new DiscordButtonComponent(DiscordButtonStyle.Danger, hitId, "Hit", false);
        var stayButton = new DiscordButtonComponent(DiscordButtonStyle.Primary, standId, "Stay", false);

        builder.ClearComponents();
        builder.AddComponents(hitButton, stayButton);
        await UpdateDisplay();
    }

    internal async Task HandleBlackjackInteraction(DiscordInteraction interaction, DiscordMessage msg, string[] strings)
    {
        bool isEphemeral = msg.Flags.HasValue && msg.Flags.Value.HasFlag(DiscordMessageFlags.Ephemeral);
        ulong createdFollowupId = default;

        async Task Respond(IDiscordMessageBuilder dmb)
        {
            if (isEphemeral)
            {
                await interaction.EditFollowupMessageAsync(createdFollowupId, (DiscordWebhookBuilder)dmb);
            }
            else
            {
                Logger.Put("Editing message in response to " + interaction.Data.CustomId);
                await msg.ModifyAsync((DiscordMessageBuilder)dmb);
                Logger.Put("Finished editing!");
                if (createdFollowupId != 1)
                    await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("👍").AsEphemeral(true));
                createdFollowupId = 1;
            }
        }

        IDiscordMessageBuilder builder = isEphemeral ? new DiscordWebhookBuilder() : new DiscordMessageBuilder();
        
        if (!ulong.TryParse(strings[2], out ulong followupId))
        {
            await Respond(builder.WithContent("Failed to parse a message ID from " + interaction.Data.CustomId));
            return;
        }

        if (!ulong.TryParse(strings[3], out ulong userId))
        {
            await Respond(builder.WithContent("Failed to parse a User ID from " + interaction.Data.CustomId));
            return;
        }
        else if (userId != interaction.User.Id)
        {
            await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("You can't play blackjack for someone else!").AsEphemeral(true));
            return;
        }
        
        await interaction.DeferAsync(true);

        if (!int.TryParse(strings[6], out int wager))
        {
            await Respond(builder.WithContent("Failed to parse a wager from " + interaction.Data.CustomId));
        }

        if (PersistentData.values.casinoPoints.TryGetValue(interaction.User.Id, out int points) && points < wager)
        {
            await Respond(builder.WithContent("You don't have enough points to wager!"));
            return;
        }

        if (!Cards.TryParse(strings[4], out var dealerHand))
        {
            await Respond(builder.WithContent("Failed to parse the dealer's hand from " + interaction.Data.CustomId));
            return;
        }

        if (!Cards.TryParse(strings[5], out var playerHand))
        {
            await Respond(builder.WithContent("Failed to parse your hand from " + interaction.Data.CustomId));
            return;
        }

        if (isEphemeral)
        {
            createdFollowupId = (await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Continuing blackjack"))).Id;
        }
        Logger.Put("Followup ID: " + followupId + ", created followup ID: " + createdFollowupId);
        //var ms = await interaction.Channel.GetMessageAsync(followupId);
        //var ms = await interaction.GetOriginalResponseAsync();
        //await interaction.EditOriginalResponseAsync(builder.WithContent("SUCK ME"));
        //await interaction.EditFollowupMessageAsync(followupId, builder.WithContent("SUCK ME"));

        // yes i still know local funcs are shit. i still dont care.
        async Task UpdateDisplay()
        {
            string display = GenerateBlackjackString(wager, dealerHand, playerHand);
            builder.WithContent(display);
            //await interaction.EditFollowupMessageAsync(followupId, builder);
            await Respond(builder);
        }

        // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action}
        // const string BLACKJACK_INTERACTION_FORMAT = "bb.bj.{0}.{1}.{2}.{3}.{4}.{5}";
        string buttonIdBase = string.Format(BLACKJACK_INTERACTION_FORMAT, followupId, userId, "{0}", "{1}", wager, "{2}");

        List<DiscordComponent> disabledButtons = new()
        {
            new DiscordButtonComponent(DiscordButtonStyle.Danger, BLACKJACK_NO_OP + ".a", "Hit", true),
            new DiscordButtonComponent(DiscordButtonStyle.Primary, BLACKJACK_NO_OP + ".b", "Stay", true),
        };

        Cards.Deck deck = new();
        deck.Exclude(dealerHand);
        deck.Exclude(playerHand);
        deck.Shuffle();

        if (dealerHand.Count == 1)
        {
            dealerHand.Add(deck.Draw());

            await UpdateDisplay();

            await Task.Delay(1000);
        }

        switch (strings[7])
        {
            case BLACKJACK_ACTION_HIT:
                playerHand.Add(deck.Draw());
                await UpdateDisplay();

                await Task.Delay(1000);
                
                builder.ClearComponents();

                int playerValHit = Cards.HandValue(playerHand);
                int dealerValHit = Cards.HandValue(dealerHand);
                if (playerValHit > 21)
                {
                    // lose
                    PersistentData.values.casinoPoints[interaction.User.Id] -= wager;
                    PersistentData.WritePersistentData();
                    builder.AddComponents(disabledButtons);
                    await Respond(builder.WithContent(builder.Content + $"\nYou lost your {wager} points! You now have {PersistentData.values.casinoPoints[interaction.User.Id]} points!"));
                    return;
                }
                //else if (playerValHit == dealerValHit)
                //{
                //    // tie
                //    builder.AddComponents(disabledButtons);
                //    await Respond(builder.WithContent(builder.Content + "\nIt's a tie! You get your points back."));
                //    return;
                //}


                string hitId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand), BLACKJACK_ACTION_HIT);
                string standId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand), BLACKJACK_ACTION_STAND);

                var hitButton = new DiscordButtonComponent(DiscordButtonStyle.Danger, hitId, "Hit", false);
                var stayButton = new DiscordButtonComponent(DiscordButtonStyle.Primary, standId, "Stay", false);

                builder.AddComponents(hitButton, stayButton);

                await UpdateDisplay();
                break;
            case BLACKJACK_ACTION_STAND:

                while (Cards.HandValue(dealerHand) < 17 || Cards.HandValue(dealerHand) < Cards.HandValue(playerHand))
                {
                    dealerHand.Add(deck.Draw());

                    await Task.Delay(1000);

                    await UpdateDisplay();
                }


                builder.ClearComponents();
                builder.AddComponents(disabledButtons);

                int playerValStand = Cards.HandValue(playerHand);
                int dealerValStand = Cards.HandValue(dealerHand);
                if (playerValStand < dealerValStand && dealerValStand < 22)
                {
                    // lose
                    PersistentData.values.casinoPoints[interaction.User.Id] -= wager;
                    PersistentData.WritePersistentData();
                    builder.WithContent(builder.Content + $"\nYou lost your {wager} points! You now have {PersistentData.values.casinoPoints[interaction.User.Id]} points!");

                }
                else if (playerValStand == dealerValStand)
                {
                    // tie
                    builder.WithContent(builder.Content + "\nIt's a tie! You get your points back.");
                }
                else
                {
                    // win
                    PersistentData.values.casinoPoints[interaction.User.Id] += wager;
                    PersistentData.WritePersistentData();
                    builder.WithContent(builder.Content + $"\nYou won your {wager} point wager! You now have {PersistentData.values.casinoPoints[interaction.User.Id]} points!");
                }
                await Respond(builder);
                break;
            default:
                await Respond(builder.WithContent("Failed to retrieve a valid action from " + interaction.Data.CustomId));
                //await interaction.EditFollowupMessageAsync(followupId, builder.WithContent("Failed to retrieve a valid action from " + interaction.Data.CustomId));
                return;
        }
    }

    private static string GenerateBlackjackString(int wager, List<Cards.Card> dealerHand, List<Cards.Card> playerHand)
    {
        StringBuilder sb = new();

        sb.AppendLine($"You have **{wager}** points on the line.");
        sb.Append($"**Dealer's** hand: `");
        switch (dealerHand.Count)
        {
            case 0:
                sb.Append("🎴🎴");
                break;
            case 1:
                Cards.AppendCard(sb, dealerHand[0]);
                sb.Append(" 🎴");
                break;
            default:
                Cards.AppendHand(sb, dealerHand);
                break;
        }
        sb.AppendLine("`");
        sb.AppendLine($"Dealer's hand value: **{Cards.HandValue(dealerHand)}**");

        sb.Append($"**Your** hand: `");
        switch (playerHand.Count)
        {
            case 0:
                sb.Append("🎴🎴");
                break;
            case 1:
                Cards.AppendCard(sb, playerHand[0]);
                sb.Append(" 🎴");
                break;
            default:
                Cards.AppendHand(sb, playerHand);
                break;
        }
        sb.AppendLine("`");
        sb.AppendLine($"Your hand value: **{Cards.HandValue(playerHand)}**");

        return sb.ToString();
    }

    static DiscordComponent[] GetBlackjackComponents(ulong followupMsgId, DiscordUser user, Cards.Card[] dealerHand, Cards.Card[] playerHand, int wager)
    {
        // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action}
        string str = string.Format(BLACKJACK_INTERACTION_FORMAT, followupMsgId, user.Id, Cards.ToString(dealerHand), Cards.ToString(playerHand), wager, "");

        DiscordButtonComponent hitButton = new(DiscordButtonStyle.Primary, str + "hit", "Hit!");
        DiscordButtonComponent standButton = new(DiscordButtonStyle.Secondary, str + "stand", "Stand.");
        
        return [hitButton, standButton];
    }

    [Command("checkPoints"), Description("Check your points!")]
    public static async Task CheckCasinoPoints(SlashCommandContext ctx,
        [Parameter("sendSecretly"), Description("Whether to show to only you.")] bool ephemeral = true)
    {
        await BoneBot.Bots[ctx.Client].casino.CheckPoints(ctx, ephemeral);
    }

    
    [Command("slots"), Description("GAMBLING GAMBLING GAMBLING")]
    public static async Task GambleSlots(SlashCommandContext ctx,
        [Parameter("amount"), Description("How many points to gamble.")] long amount,
        [Parameter("sendSecretly"), Description("Whether to only show to you.")] bool ephemeral = true)
    {
        await BoneBot.Bots[ctx.Client].casino.GambleSlots(ctx, (int)amount, ephemeral);
    }

    [Command("givePoints"), Description("Give a user points")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MDOERATOR_PERMS)]
    public static async Task GivePoints(SlashCommandContext ctx,
        [Parameter("amount"), Description("How many points to give.")] long amount,
        [Parameter("sendTo"), Description("Who to give points to.")] DiscordMember user)
    {
        if (await SlashCommands.ModGuard(ctx, true))
            return;

        BoneBot.Bots[ctx.Client].casino.GivePoints((DiscordMember)user, (int)amount);

        try
        {
            await ctx.RespondAsync($"Done, they now have {PersistentData.values.casinoPoints[user.Id]} points.", true);
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while giving points", ex);
        }
    }

    [Command("blackjack"), Description("Play blackjack!")]
    public static async Task StartBlackjack(SlashCommandContext ctx,
        [Parameter("amount"), Description("How many points to gamble.")] long amount,
        [Parameter("sendSecretly"), Description("Whether to only show to you.")] bool ephemeral = true)
    {
        //if (await Guard(ctx))
        //    return;

        await BoneBot.Bots[ctx.Client].casino.BeginBlackjack(ctx, (int)amount, ephemeral);
    }
}
