using System.Text;

namespace BoneBoard;

internal static class Cards
{
    static Suit[] suits = Enum.GetValues<Suit>();
    static Rank[] ranks = Enum.GetValues<Rank>();

    internal static bool TryParse(string handStr, out List<Card> hand)
    {
        hand = new();
        if (handStr.Length % 2 != 0)
            return false;

        for (int i = 0; i < handStr.Length; i += 2)
        {
            char suit = handStr[i];
            char rank = handStr[i + 1];

            if (suits.Contains((Suit)suit) && ranks.Contains((Rank)rank))
            {
                hand.Add(new Card((Suit)suit, (Rank)rank));
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static string ToString(IEnumerable<Card> hand)
    {
        StringBuilder sb = new();
        foreach (Card card in hand)
        {
            sb.Append(card);
        }
        return sb.ToString();
    }

    public static int RankValue(Rank rank) => rank switch
    {
        Rank.Ace => 11,
        Rank.Two => 2,
        Rank.Three => 3,
        Rank.Four => 4,
        Rank.Five => 5,
        Rank.Six => 6,
        Rank.Seven => 7,
        Rank.Eight => 8,
        Rank.Nine => 9,
        Rank.Ten => 10,
        Rank.Jack => 10,
        Rank.Queen => 10,
        Rank.King => 10,
        _ => 0,
    };

    public static int HandValue(IEnumerable<Card> hand)
    {
        int value = 0;
        foreach (Card card in hand)
        {
            value += RankValue(card.Rank);
        }

        if (value > 21)
        {
            // backtrack value for aces if necessary
            foreach (Card card in hand)
            {
                if (card.Rank == Rank.Ace)
                {
                    value -= 10;
                    if (value <= 21)
                    {
                        break;
                    }
                }
            }
        }

        return value;
    }

    public static void AppendHand(StringBuilder sb, IEnumerable<Card> hand)
    {
        foreach (Card card in hand)
        {
            AppendCard(sb, card);
        }
    }

    public static void AppendCard(StringBuilder sb, Card card)
    {
        sb.Append(card.Suit switch
        {
            Cards.Suit.Clubs => "♣️",
            Cards.Suit.Diamonds => "♦️",
            Cards.Suit.Hearts => "♥️",
            Cards.Suit.Spades => "♠️",
            _ => "(err suit)"
        });
        sb.Append(card.Rank switch
        {
            Cards.Rank.Ten => "10",
            _ => ((char)card.Rank).ToString()
        });
        sb.Append(' ');
    }

    public enum Suit
    {
        Hearts = 'H',
        Diamonds = 'D',
        Clubs = 'C',
        Spades = 'S',
    }

    public enum Rank
    {
        Ace = 'A',
        Two = '2',
        Three = '3',
        Four = '4',
        Five = '5',
        Six = '6',
        Seven = '7',
        Eight = '8',
        Nine = '9',
        Ten = '0',
        Jack = 'J',
        Queen = 'Q',
        King = 'K',
    }

    public class Card
    {
        public Suit Suit { get; }
        public Rank Rank { get; }

        public Card(Suit suit, Rank rank)
        {
            Suit = suit;
            Rank = rank;
        }

        // for serialization
        public override string ToString()
        {
            return $"{(char)Suit}{(char)Rank}";
        }
    }

    public class Deck
    {
        private readonly List<Card> cards = new();
        private readonly List<Card> discarded = new();

        public Deck()
        {
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                {
                    cards.Add(new Card(suit, rank));
                }
            }
        }

        public void Shuffle(int seed = -1)
        {
            Random rng = seed == -1 ? new() : new(seed);
            int n = cards.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                (cards[n], cards[k]) = (cards[k], cards[n]);
            }
        }

        public Card Draw()
        {
            int iter = 0;
            Card card;
            do
            {
                if (cards.Count == 0)
                    return new Card(Suit.Hearts, Rank.Jack); // Default card if deck is empty

                card = cards[iter % cards.Count];
                cards.RemoveAt(iter % cards.Count);
                iter++;
            } while (discarded.Contains(card));

            return card;
        }

        public void Exclude(IEnumerable<Card> cards)
        {
            foreach (Card card in cards)
            {
                this.cards.Remove(card);
                discarded.Add(card);
            }
        }
    }
}
