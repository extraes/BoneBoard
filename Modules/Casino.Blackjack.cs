using System.ComponentModel;
using System.Reflection;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

namespace BoneBoard.Modules;

file sealed class RankComparerAceHi : IComparer<Cards.Rank>
{
    public static readonly RankComparerAceHi Instance = new RankComparerAceHi();
    
    public int Compare(Cards.Rank x, Cards.Rank y)
    {
        return Cards.RankValue(x).CompareTo(Cards.RankValue(y));
    }
}

file sealed class RankComparerAceLo : IComparer<Cards.Rank>
{
    public static readonly RankComparerAceLo Instance = new RankComparerAceLo();    
    
    public int Compare(Cards.Rank x, Cards.Rank y)
    {
        int xVal = x == Cards.Rank.Ace ? 1 : Cards.RankValue(x);
        int yVal = y == Cards.Rank.Ace ? 1 : Cards.RankValue(y);
        
        return xVal.CompareTo(yVal);
    }
}

file static class BlackjackUtils
{
    public static bool HasRank(this IEnumerable<Cards.Card> cards, Cards.Rank rank)
    {
        foreach (var card in cards)
        {
            if (card.Rank == rank) 
                return true;
        }

        return false;
    }

    private static (Casino.SideBet bet, int amount) DeserializeBet(string betString)
    {
        return
            (
                betString[0] switch
                {
                    'L' => Casino.SideBet.LuckyLucky,
                    'P' => Casino.SideBet.PerfectPair,
                    'R' => Casino.SideBet.RoyalMatch,
                    '2' => Casino.SideBet.TwentyOnePlusThree,
                    '7' => Casino.SideBet.BlazingSevens,
                    'l' => Casino.SideBet.LuckyLadies,
                    'I' => Casino.SideBet.InBetween,
                    'B' => Casino.SideBet.BustIt,
                    'M' => Casino.SideBet.MatchTheDealer,
                    _ => throw new ArgumentOutOfRangeException(nameof(betString), betString, "Invalid bet first char value")
                },
                int.Parse(betString[1..])
            );
    }
    
    public static List<(Casino.SideBet bet, int amount)> DeserializeSideBetString(string isolatedString)
    {
        return isolatedString.Split('|').Select(DeserializeBet).ToList();
    }

    private static string StringifyBet(Casino.SideBet bet, int amount)
    {
        return bet switch
        {
            Casino.SideBet.LuckyLucky => "L", 
            Casino.SideBet.PerfectPair => "P", 
            Casino.SideBet.RoyalMatch => "R", 
            Casino.SideBet.TwentyOnePlusThree => "2", 
            Casino.SideBet.BlazingSevens => "7", 
            Casino.SideBet.LuckyLadies => "l", 
            Casino.SideBet.InBetween => "I", 
            Casino.SideBet.BustIt => "B", 
            Casino.SideBet.MatchTheDealer => "M",
            _ => throw new ArgumentOutOfRangeException(nameof(bet), bet, "Invalid bet enum value")
        } + amount;
    }
    
    public static string SerializeSideBetString(List<(Casino.SideBet bet, int amount)> sideBets)
    {
        return string.Join('|', sideBets.Select(b => StringifyBet(b.bet, b.amount)));
    }
}

internal partial class Casino
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    private sealed class SideBetMetadata(string name, string payoutInfo) : Attribute
    {
        public readonly string Name = name;
        public readonly string Description = payoutInfo;
    }
    
    private const string BLACKJACK_NO_OP = "bb.bj.noop";
    private const string BLACKJACK_INTERACTION_FORMAT = "bb.bj.{0}.{1}.{2}.{3}.{4}.{5}.{6}"; // .Format idx's
    private const string BLACKJACK_ACTION_HIT = "h";
    private const string BLACKJACK_ACTION_STAND = "s";

    private const double SIDE_BET_INDETERMINATE = -1; // use BEFORE requirements to judge met
    private const double SIDE_BET_FAILED = -2;
    private const double SIDE_BET_INAPPLICABLE = -3; // use AFTER requirements to judge met

    private static readonly Cards.Rank[] Ranks = Enum.GetValues<Cards.Rank>();

    internal enum SideBet
    {
        [Description("Player's initial 2 + dealer's first card sum to 19-21.")]
        [SideBetMetadata("Lucky Lucky", "2x on 19 or 20; 3x on 21; 10x on suited 21; 30x on 6-7-8; 50x on 7-7-7; 100x on suited 6-7-8")]
        LuckyLucky,
        [Description("Player/dealer's initial hand has a pair")]
        [SideBetMetadata("Perfect Pair", "6x on red/black pair; 10x on colored pair; 50x if both have a pair; 250x if both pairs are colored; 1000x if all four cards match")]
        PerfectPair,
        [Description("Player's initial hand is suited. Bonus for K&Q")]
        [SideBetMetadata("Royal Match", "2.5x for suited; 25x for K&Q")]
        RoyalMatch,
        [Description("Player's initial 2 + dealer's first card make a poker hand.")]
        [SideBetMetadata("21+3", "3x for flush; 3x for straight; 3x for 3 of a kind; 25x for straight flush; nothing for a regular pair, lol")]
        TwentyOnePlusThree,
        [Description("Player's initial 2 + dealer's first card contain 7s")]
        [SideBetMetadata("Blazing Sevens", "2x for one 7; 25x for two 7s; 200x for three 7s")]
        BlazingSevens,
        [Description("Player's initial hand sums to 20")]
        [SideBetMetadata("Lucky Ladies", "4x on unsuited; 9x on suited; 20x on matched; 100x on queens; 1500x on red queens")]
        LuckyLadies,
        [Description("Dealer's first card is between the player's first two")]
        [SideBetMetadata("In Between", "Higher payouts for tighter windows: 6x for a two-rank window; 10x for a one-rank window; 30x for a three of a kind; 4x for anything else")]
        InBetween,
        [Description("First card drawn to the dealer results in a bust")]
        [SideBetMetadata("Bust It", "2.5x if it happens; 999999x if it doesn't. sike.")]
        BustIt,
        [Description("One or both of the player's first two matches the dealer's first card.")]
        [SideBetMetadata("Match The Dealer", "4x if one card matches; 10x if one card matches & matches color; 50x if both cards match")]
        MatchTheDealer
    }
    
    private static readonly SideBet[] allSideBets = Enum.GetValues<SideBet>();

    private static Dictionary<SideBet, DescriptionAttribute> SideBetDescriptions = typeof(SideBet).GetFields().Where(fi => fi.IsStatic)
        .ToDictionary(fi => (SideBet)fi.GetValue(null)!, fi => fi.GetCustomAttributes<DescriptionAttribute>().First());
    private static Dictionary<SideBet, SideBetMetadata> SideBetMetadatas = typeof(SideBet).GetFields().Where(fi => fi.IsStatic)
        .ToDictionary(fi => (SideBet)fi.GetValue(null)!, fi => fi.GetCustomAttributes<SideBetMetadata>().First());
    
    private delegate double SideBetEvaluator(List<Cards.Card> dealer, List<Cards.Card> player);

    private static bool CardCountIsNot(int targetHandSize, List<Cards.Card> dealer, List<Cards.Card> player,
        out double result)
    {
        int cardCount = dealer.Count + player.Count;
        if (cardCount < targetHandSize)
        {
            result = SIDE_BET_INDETERMINATE;
            return true;
        }

        if (cardCount > targetHandSize)
        {
            result = SIDE_BET_INAPPLICABLE;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool RanksMatch(List<Cards.Card> cards)
    {
        if (cards.Count == 0)
            return false;
        
        Cards.Rank rank = cards[0].Rank;
        foreach (Cards.Card card in cards)
        {
            if (card.Rank != rank)
                return false;
        }

        return true;
    }
    
    private static bool SuitsMatch(List<Cards.Card> cards)
    {
        if (cards.Count == 0)
            return false;
        
        Cards.Suit suit = cards[0].Suit;
        foreach (Cards.Card card in cards)
        {
            if (card.Suit != suit)
                return false;
        }

        return true;
    }

    private static bool ColorsMatch(List<Cards.Card> cards, Cards.Suit? suitToColorMatchTo = null)
    {
        if (cards.Count == 0)
            return false;

        var suit = suitToColorMatchTo ?? cards[0].Suit;
        
        bool isBlackSuit = suit == Cards.Suit.Clubs || suit == Cards.Suit.Spades;

        if (isBlackSuit)
        {
            foreach (Cards.Card card in cards)
            {
                if (card.Suit == Cards.Suit.Diamonds || card.Suit == Cards.Suit.Hearts)
                    return false;
            }
        }
        else
        {
            foreach (Cards.Card card in cards)
            {
                if (card.Suit == Cards.Suit.Spades || card.Suit == Cards.Suit.Clubs)
                    return false;
            }
        }

        return true;
    }

    private static readonly Dictionary<SideBet, SideBetEvaluator> SideBetEvaluators = new()
    {
        {
            SideBet.LuckyLucky,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;

                List<Cards.Card> allCards =
                    dealer.Concat(player).OrderBy(c => c.Rank, RankComparerAceLo.Instance).ToList();
                int sum = Cards.HandValue(allCards);
                if (sum < 19 || sum > 21)
                    return SIDE_BET_FAILED;

                if (sum != 21)
                    return 2;

                bool matching = RanksMatch(allCards);
                bool suited = SuitsMatch(allCards);
                bool countingUp = allCards[0].Rank == Cards.Rank.Six &&
                                  allCards[1].Rank == Cards.Rank.Seven &&
                                  allCards[2].Rank == Cards.Rank.Eight;

                if (matching) // must be all sevens 
                    return 50;

                if (suited)
                    return countingUp ? 100 : 10;

                if (countingUp)
                    return 30;

                return 3;
            }
        },
        {
            SideBet.PerfectPair,
            (dealer, player) =>
            {
                if (CardCountIsNot(4, dealer, player, out double result))
                    return result;

                bool dealerPair = RanksMatch(dealer);
                bool playerPair = RanksMatch(player);
                if (!dealerPair && !playerPair)
                    return SIDE_BET_FAILED;

                bool dealerColor = ColorsMatch(dealer);
                bool playerColor = ColorsMatch(dealer);

                List<Cards.Card> allCards = dealer.Concat(player).OrderBy(c => c.Rank).ToList();
                if (RanksMatch(allCards))
                    return 1000;

                if (dealerPair && playerPair)
                {
                    if (dealerColor && playerColor)
                        return 250;

                    return 50;
                }

                if (dealerPair && dealerColor || playerPair && playerColor)
                {
                    return 10;
                }

                return 6;
            }
        },
        {
            SideBet.RoyalMatch,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;

                if (!SuitsMatch(player) || player.Count != 2)
                    return SIDE_BET_FAILED;


                if (player.HasRank(Cards.Rank.King) && player.HasRank(Cards.Rank.Queen))
                    return 25;

                return 2.5;
            }
        },
        {
            SideBet.TwentyOnePlusThree,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;

                List<Cards.Card> allCards =
                    dealer.Concat(player).OrderBy(c => c.Rank, RankComparerAceLo.Instance).ToList();
                bool flush = RanksMatch(allCards);

                int firstRankIdx = Array.IndexOf(Ranks, allCards[0].Rank);
                int secondRankIdx = Array.IndexOf(Ranks, allCards[1].Rank);
                int thirdRankIdx = Array.IndexOf(Ranks, allCards[2].Rank);
                bool straight = secondRankIdx == (firstRankIdx + 1) % Ranks.Length &&
                                thirdRankIdx == (secondRankIdx + 1) % Ranks.Length;

                if (flush)
                {
                    return straight ? 25 : 3;
                }

                if (straight)
                    return 3;

                if (RanksMatch(allCards)) // three of a kind
                    return 3;

                return SIDE_BET_FAILED;
            }
        },
        {
            SideBet.BlazingSevens,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;

                int sevenCount = dealer.Concat(player).Count(c => c.Rank == Cards.Rank.Seven);

                return sevenCount switch
                {
                    1 => 2,
                    2 => 25,
                    3 => 200,
                    _ => SIDE_BET_FAILED // lol
                };
            }
        },
        {
            SideBet.LuckyLadies,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;

                if (Cards.HandValue(player) != 20)
                    return SIDE_BET_FAILED;

                bool matching = RanksMatch(player);
                bool suited = SuitsMatch(player);

                if (matching)
                {
                    if (player[0].Rank == Cards.Rank.Queen) // matching queens
                    {
                        if (ColorsMatch(player, Cards.Suit.Hearts))
                            return 1500;
                        return 100;
                    }

                    return 20;
                }

                return 4;
            }
        },
        {
            SideBet.InBetween,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;


                int higher = Array.IndexOf(Ranks, player[0].Rank);
                int lower = Array.IndexOf(Ranks, player[1].Rank);
                if (lower > higher)
                    (higher, lower) = (lower, higher);

                int subject = Array.IndexOf(Ranks, dealer[0].Rank);
                if (subject > higher || subject < lower)
                    return SIDE_BET_FAILED;

                int rankWindowSize = higher - lower;
                return rankWindowSize switch
                {
                    0 => 30,
                    1 => 10,
                    2 => 6,
                    _ => 4,
                };
            }
        },
        {
            SideBet.BustIt,
            (dealer, player) =>
            {
                return Cards.HandValue(dealer) > 21 ? 2.5 : SIDE_BET_INDETERMINATE;
            }
        },
        {
            SideBet.MatchTheDealer,
            (dealer, player) =>
            {
                if (CardCountIsNot(3, dealer, player, out double result))
                    return result;

                var dealerCard = dealer[0];
                var matchedCards = player.Where(c => c.Rank == dealerCard.Rank).ToList();
                if (matchedCards.Count == 0)
                    return SIDE_BET_FAILED;

                if (matchedCards.Count > 1)
                    return 50;

                // only one matches, we now have to see if it matches the dealer card's color
                matchedCards.Add(dealerCard);

                return ColorsMatch(matchedCards) ? 10 : 4;
            }
        },
    };

    public static Dictionary<ulong, Dictionary<Casino.SideBet, double>> HistoricalBetEvaluations = new();

    private static void InitializeHistoricalEvaluationsFor(List<(SideBet, int)> bets, ulong followupId)
    {
        var dict = HistoricalBetEvaluations[followupId] = new Dictionary<SideBet, double>();
        foreach (var bet in bets)
            dict[bet.Item1] = SIDE_BET_INDETERMINATE;
    }
    
    public static void UpdateBetEvaluationsFor(List<Cards.Card> dealer,
        List<Cards.Card> player, ulong followupId)
    {
        if (!HistoricalBetEvaluations.TryGetValue(followupId, out Dictionary<Casino.SideBet, double>? betEvaluations))
            betEvaluations = new();

        // copy to list to avoid InvalidOperationException
        foreach (var betType in betEvaluations.Keys.ToList())
        {
            
            double currEval =  betEvaluations[betType];
            if (currEval == SIDE_BET_FAILED)
            {
                Logger.Put($"Skipping side bet {betType} evaluation bc it failed");
                continue;
            }
            
            double nextEval = SideBetEvaluators[betType].Invoke(dealer, player);
            
            Logger.Put($"Side bet {betType} evaluated to {nextEval}. Current = {currEval}");
            
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (currEval == SIDE_BET_INDETERMINATE || nextEval > 0)
                betEvaluations[betType] = nextEval;
            
            Logger.Put($"Side bet {betType} now at {betEvaluations[betType]}");
        }
    }
    
    internal async Task BeginBlackjack(SlashCommandContext ctx, int wager, List<(SideBet bet, int amount)> sideBets,
        bool ephemeral)
    {
        int totalWager = wager + sideBets.Sum(b => b.amount);
        
        if (!PersistentData.values.casinoPoints.TryGetValue(ctx.User.Id, out int hasPoints) || hasPoints < totalWager)
        {
            await ctx.RespondAsync($"You don't have {totalWager} points to gamble! You only have {hasPoints} points!",
                true);
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
        else if (sideBets.Any(t => t.amount < 350))
        {
            await ctx.RespondAsync("those weak ass side bets... go back to slots lil bro. gimme bout like... Tree Fiddy.", true);
            return;
        }

        GivePoints(ctx.User, -totalWager);

        var deck = new Cards.Deck();
        deck.Shuffle();

        await ctx.DeferResponseAsync(ephemeral);

        var dfmb = new DiscordFollowupMessageBuilder()
            .AsEphemeral(ephemeral)
            .WithContent("Starting...");

        DiscordMessage followup = await ctx.FollowupAsync(dfmb);

        Logger.Put("Followup ID: " + followup.Id);

        InitializeHistoricalEvaluationsFor(sideBets, followup.Id);

        // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action}.{8: sidebets}
        // const string BLACKJACK_INTERACTION_FORMAT = "bb.bj.{0}.{1}.{2}.{3}.{4}.{5}";

        string buttonIdBase = string.Format(BLACKJACK_INTERACTION_FORMAT, followup.Id, ctx.User.Id, "{0}", "{1}",
            wager, "{2}", BlackjackUtils.SerializeSideBetString(sideBets));

        var hitButtonGrayed =
            new DiscordButtonComponent(DiscordButtonStyle.Danger, BLACKJACK_NO_OP + ".a", "Hit", true);
        var stayButtonGrayed =
            new DiscordButtonComponent(DiscordButtonStyle.Primary, BLACKJACK_NO_OP + ".b", "Stay", true);

        List<Cards.Card> dealerHand = new();
        List<Cards.Card> playerHand = new();
        string display = GenerateBlackjackString(wager, dealerHand, playerHand, sideBets, new Dictionary<SideBet, double>());
        var builder = new DiscordWebhookBuilder()
            .AddActionRowComponent(hitButtonGrayed, stayButtonGrayed);

        // yes i know local functions are bad. i dont care.
        async Task UpdateDisplay()
        {
            display = GenerateBlackjackString(wager, dealerHand, playerHand, sideBets, HistoricalBetEvaluations[followup.Id]);
            builder.WithContent(display);
            await ctx.EditFollowupAsync(followup.Id, builder);
            UpdateBetEvaluationsFor(dealerHand, playerHand, followup.Id);
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
        string hitId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand),
            BLACKJACK_ACTION_HIT);
        string standId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand),
            BLACKJACK_ACTION_STAND);

        var hitButton = new DiscordButtonComponent(DiscordButtonStyle.Danger, hitId, "Hit", false);
        var stayButton = new DiscordButtonComponent(DiscordButtonStyle.Primary, standId, "Stay", false);

        builder.ClearComponents();
        builder.AddActionRowComponent(hitButton, stayButton);
        await UpdateDisplay();
    }

    internal async Task HandleBlackjackInteraction(DiscordInteraction interaction, DiscordMessage msg,
        string[] strings)
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
                    await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("👍").AsEphemeral(true));
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
            await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("You can't play blackjack for someone else!")
                    .AsEphemeral(true));
            return;
        }

        await interaction.DeferAsync(true);

        if (!int.TryParse(strings[6], out int wager))
        {
            await Respond(builder.WithContent("Failed to parse a wager from " + interaction.Data.CustomId));
        }

        var sideBets = BlackjackUtils.DeserializeSideBetString(strings[8]);
        var historicalEvals = HistoricalBetEvaluations[followupId];

        // it now deducts points on interaction start
        //if (PersistentData.values.casinoPoints.TryGetValue(interaction.User.Id, out int points) && points < wager)
        //{
        //    await Respond(builder.WithContent("You don't have enough points to wager!"));
        //    return;
        //}

        if (!Cards.TryParse(strings[4], out var dealerHand))
        {
            await Respond(
                builder.WithContent("Failed to parse the dealer's hand from " + interaction.Data.CustomId));
            return;
        }

        if (!Cards.TryParse(strings[5], out var playerHand))
        {
            await Respond(builder.WithContent("Failed to parse your hand from " + interaction.Data.CustomId));
            return;
        }

        if (isEphemeral)
        {
            createdFollowupId =
                (await interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder().WithContent("Continuing blackjack"))).Id;
        }

        Logger.Put("Followup ID: " + followupId + ", created followup ID: " + createdFollowupId);
        //var ms = await interaction.Channel.GetMessageAsync(followupId);
        //var ms = await interaction.GetOriginalResponseAsync();
        //await interaction.EditOriginalResponseAsync(builder.WithContent("SUCK ME"));
        //await interaction.EditFollowupMessageAsync(followupId, builder.WithContent("SUCK ME"));

        // yes i still know local funcs are shit. i still dont care.
        async Task UpdateDisplay()
        {
            string display = GenerateBlackjackString(wager, dealerHand, playerHand, sideBets, historicalEvals);
            builder.WithContent(display);
            //await interaction.EditFollowupMessageAsync(followupId, builder);
            await Respond(builder);
            UpdateBetEvaluationsFor(dealerHand, playerHand, followupId);
        }

        // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action}
        // const string BLACKJACK_INTERACTION_FORMAT = "bb.bj.{0}.{1}.{2}.{3}.{4}.{5}";
        string buttonIdBase = string.Format(BLACKJACK_INTERACTION_FORMAT, followupId, userId, "{0}", "{1}", wager,
            "{2}", BlackjackUtils.SerializeSideBetString(sideBets));

        List<DiscordComponent> disabledButtons = new()
        {
            new DiscordButtonComponent(DiscordButtonStyle.Danger, BLACKJACK_NO_OP + ".a", "Hit", true),
            new DiscordButtonComponent(DiscordButtonStyle.Primary, BLACKJACK_NO_OP + ".b", "Stay", true),
        };
        DiscordActionRowComponent disabledDarc = new(disabledButtons);

        Cards.Deck deck = new();
        deck.Exclude(dealerHand);
        deck.Exclude(playerHand);
        deck.Shuffle();
        //deck.Shuffle();

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
                    PersistentData.WritePersistentData();

                    builder.AddActionRowComponent(disabledDarc);
                    await Respond(builder.WithContent(builder.Content +
                                                      $"\nYou lost your {wager} points! You now have {PersistentData.values.casinoPoints[interaction.User.Id]} points!"));
                    return;
                }
                //else if (playerValHit == dealerValHit)
                //{
                //    // tie
                //    builder.AddComponents(disabledButtons);
                //    await Respond(builder.WithContent(builder.Content + "\nIt's a tie! You get your points back."));
                //    return;
                //}


                string hitId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand),
                    BLACKJACK_ACTION_HIT);
                string standId = string.Format(buttonIdBase, Cards.ToString(dealerHand), Cards.ToString(playerHand),
                    BLACKJACK_ACTION_STAND);

                var hitButton = new DiscordButtonComponent(DiscordButtonStyle.Danger, hitId, "Hit", false);
                var stayButton = new DiscordButtonComponent(DiscordButtonStyle.Primary, standId, "Stay", false);

                builder.AddActionRowComponent(hitButton, stayButton);

                await UpdateDisplay();
                break;
            case BLACKJACK_ACTION_STAND:

                while (Cards.HandValue(dealerHand) < 17 ||
                       Cards.HandValue(dealerHand) < Cards.HandValue(playerHand))
                {
                    dealerHand.Add(deck.Draw());

                    await Task.Delay(1000);

                    await UpdateDisplay();
                }


                builder.ClearComponents();
                builder.AddActionRowComponent(disabledDarc);

                int playerValStand = Cards.HandValue(playerHand);
                int dealerValStand = Cards.HandValue(dealerHand);
                if (playerValStand < dealerValStand && dealerValStand < 22)
                {
                    // lose
                    // points are deducted on interaction start. dont double-deduct
                    builder.WithContent(builder.Content +
                                        $"\nYou lost your {wager} points! You now have {PersistentData.values.casinoPoints[interaction.User.Id]} points!");
                }
                else if (playerValStand == dealerValStand)
                {
                    // tie
                    // give back points deducted on start
                    GivePoints(userId, wager);
                    builder.WithContent(builder.Content + "\nIt's a tie! You get your points back.");
                }
                else
                {
                    // win
                    // give back points deducted on start, and then the extra points won
                    GivePoints(userId, wager * 2);
                    builder.WithContent(builder.Content +
                                        $"\nYou won your {wager} point wager! You now have {PersistentData.values.casinoPoints[interaction.User.Id]} points!");
                }

                await Respond(builder);
                break;
            default:
                await Respond(builder.WithContent("Failed to retrieve a valid action from " +
                                                  interaction.Data.CustomId));
                //await interaction.EditFollowupMessageAsync(followupId, builder.WithContent("Failed to retrieve a valid action from " + interaction.Data.CustomId));
                return;
        }
    }

    private static string GenerateBlackjackString(int wager, List<Cards.Card> dealerHand,
        List<Cards.Card> playerHand, List<(SideBet bet, int amount)> zippedBets, Dictionary<SideBet, double> historicalBetEvals)
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

        if (zippedBets.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Side Bets**");
            HashSet<SideBet> bets = [];
            
            foreach ((SideBet bet, int amount) in zippedBets)
            {
                string name = bets.Contains(bet)
                    ? $"It's so nice, you did it twice: '{SideBetMetadatas[bet].Name}' edition"
                    : SideBetMetadatas[bet].Name;

                double pastEval = historicalBetEvals.GetValueOrDefault(bet, SIDE_BET_INDETERMINATE);
                double currentEval = SideBetEvaluators[bet].Invoke(dealerHand, playerHand);
                
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                double displayEval = pastEval == SIDE_BET_INDETERMINATE ? currentEval : pastEval;

                string evalString = displayEval switch
                {
                    SIDE_BET_INDETERMINATE => $"*{amount} on the line...*",
                    SIDE_BET_FAILED => $"Lost {amount} points!!",
                    SIDE_BET_INAPPLICABLE => $"this text shouldnt appearrrrrrr!!! (past evals {pastEval}, curr eval {currentEval})",
                    _ => $"Won {(int)Math.Round(displayEval*amount)} points!"
                };
                sb.AppendLine($"-# **{name}**: {evalString}");
            }
        }

        return sb.ToString();
    }

    // [Command("listsidebets"), Description("Lists side bets and how they pay out")]
    public static async Task ListSideBets(SlashCommandContext ctx)
    {
        
    }
    
    
    [Command("blackjack"), Description("Play blackjack! (Shown to everyone)")]
    public static async Task StartBlackjack(SlashCommandContext ctx,
            [Parameter("amount"), Description("How many points to gamble.")]
            int amount,
            SideBet? primarySideBet = null,
            SideBet? secondarySideBet = null,
            int primarySideBetAmount = 0,
            int secondarySideBetAmount = 0)
        //[Parameter("sendSecretly"), Description("Whether to only show to you.")] bool ephemeral = true)
    {
        // List<SideBet> sideBets = [];
        // List<int> sideBetPointAmounts = [];

        
        List<(SideBet bet, int amount)> zippedBets = [];
        // var pointAmounts = sideBetPointAmounts.ToList();

        if (primarySideBet.HasValue)
            zippedBets.Add((primarySideBet.Value, primarySideBetAmount));
        if (secondarySideBet.HasValue)
            zippedBets.Add((secondarySideBet.Value, secondarySideBetAmount));
        // if (pointAmounts.Count == 1)
        // {
        //     for (int i = 0; i < zippedBets.Count; i++)
        //     {
        //         // var 
        //         var tuple = zippedBets[i];
        //         tuple.amount = pointAmounts[0];
        //         zippedBets[i] =  tuple;
        //     }
        // }
        // if (zippedBets.Count() != pointAmounts.Count)
        // {
        //     await ctx.RespondAsync("Hey... you kind of need to... include amounts for your side bets... for all of them...", true);
        //     return;
        // }

        await BoneBot.Bots[ctx.Client].casino.BeginBlackjack(ctx, amount, zippedBets, false);
    }
}