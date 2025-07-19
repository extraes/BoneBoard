using DSharpPlus.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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

    public List<string> previousAiConfessions = new();
    public Dictionary<ulong, TimeSpan> frogRoleTimes = new(); // user id -> time
    public DateTime lastSwitchTime = DateTime.Now;
    public List<ulong> bufferedChannels = new(); // channel id
    public Dictionary<ulong, ulong> bufferChannelMessages = new(); // channel id -> message id
    public Dictionary<ulong, string> bufferChannelMessageFormats = new(); // channel id -> message format string
    public Dictionary<ulong, ulong> imageRoyaleSubmissions = new(); // submitter id -> message id
    public Dictionary<ulong, ulong> videoRoyaleSubmissions = new(); // submitter id -> message id
    public HashSet<string> usedHaikus = new();
    public List<ulong> aiConfessionals = new(); // message id
    public Dictionary<ulong, DateTime> confessionalRevealTime = new(); // message id -> reveal datetime
    public ulong lastFrogKing;
    
    public string currHangmanWord = "";
    public string currHangmanGuessed = "";
    public int currHangmanState = 0;

    public Dictionary<ulong, ulong> wikiTopicAnnounceMessages = new();
    public DateTime lastTopicSwitchTime = DateTime.Now.Subtract(TimeSpan.FromHours(24));
    public string wikiTopicMessageLink = "";
    public string currentWikiTopic = "";

    public Dictionary<ulong, int> casinoPoints = new(); // user id -> points

    static PersistentData()
    {
        Console.WriteLine("Initializing persistent data storage");
        if (!File.Exists(PD_PATH))
        {
            File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(new PersistentData())); // mmm triple parenthesis, v nice
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

    public static void WritePersistentData()
    {
        Logger.Put($"Writing persistent data to disk.", LogType.Debug);
        File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(values));
        PersistentDataChanged?.Invoke();
    }
}
