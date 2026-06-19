using DSharpPlus.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tomlet;
using Tomlet.Attributes;

namespace BoneBoard;

internal class PersistentData
{
    public static event Action? PersistentDataChanged;
    public static PersistentData values;
    private const string PD_PATH = "./persistentData.json";
    
    public Dictionary<ulong, TimeSpan> frogRoleTimes = new(); // user id -> time
    public ulong lastFrogKing;
    public DateTime lastSwitchTime = DateTime.Now;

    public List<ulong> bufferedChannels = []; // channel id
    public Dictionary<ulong, ulong> bufferChannelMessages = new(); // channel id -> message id
    public Dictionary<ulong, string> bufferChannelMessageFormats = new(); // channel id -> message format string
    
    public Dictionary<ulong, ulong> imageRoyaleSubmissions = new(); // submitter id -> message id
    public Dictionary<ulong, ulong> videoRoyaleSubmissions = new(); // submitter id -> message id
    
    public HashSet<string> usedHaikus = [];

    public List<string> previousAiConfessions = [];
    public List<ulong> aiConfessionals = []; // message id
    public Dictionary<ulong, DateTime> confessionalRevealTime = new(); // message id -> reveal datetime
    
    public string currHangmanWord = "";
    public string currHangmanGuessed = "";
    public int currHangmanState = 0;

    public Dictionary<ulong, ulong> wikiTopicAnnounceMessages = new();
    public DateTime lastTopicSwitchTime = DateTime.Now.Subtract(TimeSpan.FromHours(24));
    public string wikiTopicMessageLink = "";
    public string currentWikiTopic = "";

    public Dictionary<ulong, int> casinoPoints = new(); // user id -> points

    public Dictionary<ulong, Dictionary<ulong, ulong>> lastReslowedMessages = new(); // channel id -> user id -> message id
    public Dictionary<ulong, Dictionary<ulong, DateTime>> ignoreReslowingUntil = new(); // channel id -> user id -> expiry time

    public string predictionBoardLink = "";
    public List<Modules.PredictionEvent> predictionEvents = [];

    public List<string> stickiedMessages = [];
    
    public Dictionary<ulong, Dictionary<ulong, DateTime>> channelTimeoutEndTimes = new(); // channel id -> user id -> expiry time

    public Dictionary<ulong, Dictionary<ulong, string>> uniqueChannelsMessages = new(); // channel id -> msg id -> cleaned content

    public Dictionary<ulong, Dictionary<ulong, DateTime>> lastUnoriginalObituaryTimes = new(); // channel id -> user id -> last time an obituary was made for someone
    
    static PersistentData()
    {
        Console.WriteLine("Initializing persistent data storage");
        if (!File.Exists(PD_PATH))
        {
            File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(new PersistentData(), Formatting.Indented)); // mmm triple parenthesis, v nice
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WritePersistentData();

        ReadPersistentData();
        WritePersistentData();
    }

    public static void OutputRawJSON()
    {
        Console.WriteLine(File.ReadAllText(PD_PATH));
    }

    [MemberNotNull(nameof(values))]
    public static void ReadPersistentData()
    {
        string configText = File.ReadAllText(PD_PATH);
        values = JsonConvert.DeserializeObject<PersistentData>(configText) ?? new PersistentData();
        Logger.Put($"Read persistent data from disk.", LogType.Debug);
    }

    private static readonly Stopwatch Timer = new();
    public static void WritePersistentData()
    {
        // Logger.Put($"Writing persistent data to disk.", LogType.Debug);
        TimeSpan serialize;
        TimeSpan write;
        
        Timer.Restart();
        string serializedString = JsonConvert.SerializeObject(values, Formatting.Indented);
        Timer.Stop();
        serialize = Timer.Elapsed;
        
        Timer.Restart();
        File.WriteAllText(PD_PATH, serializedString);
        Timer.Stop();
        write = Timer.Elapsed;
        
        PersistentDataChanged?.Invoke();
        Logger.Put($"Wrote persistent data to disk in {(serialize + write).Seconds:0.00}s " +
                   $"({(int)serialize.TotalMilliseconds}ms serializing, " +
                   $"{(int)write.TotalMilliseconds}ms writing)");
    }
}
