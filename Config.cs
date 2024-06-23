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

internal class Config
{
    public static event Action? ConfigChanged;
    public static Config values;
    private const string CFG_PATH = "./config.toml";
    internal string token = "";
    public string logPath = "./logs/";

    public string quoteFont = "Comfortaa";

    public ulong[] blockedUsers = Array.Empty<ulong>();
    public ulong[] owners = Array.Empty<ulong>();
    public ulong[] requiredRoles = Array.Empty<ulong>();
    public ulong[] requiredEmojis = Array.Empty<ulong>();
    public int requiredReactionCount = 3;
    public ulong logChannel = 0;
    public ulong outputChannel = 0;
    public int maxLogFiles = 5;
    public int statusChangeDelaySec = 60 * 15; // once every 15 min
    public bool useServerProfile = true;

    public ulong frogRole = 0;
    public string frogMessageLink = "";
    public string frogMessageBase = "";
    public string frogMessageClosedBase = "";
    public string frogLeaderboardLink = "";
    public string frogLeaderboardBase = "";
    [TomlInlineComment("Options: NONE, REACTION, REPLY")]
    public FrogRoleActivation frogRoleActivation = FrogRoleActivation.REPLY;
    public FrogRoleLimitation frogRoleLimitations = FrogRoleLimitation.DAY_OF_WEEK;
    public DayOfWeek frogRoleAvailableOn = DayOfWeek.Wednesday;
    public int leaderboardUpdatePeriodMin = 5;

    public ulong confessionalChannel = 0;
    public ulong confessionalRole = 0;
    [TomlInlineComment("Options: NONE, ROLE, COOLDOWN")]
    public ConfessionalRequirements confessionalRestrictions = ConfessionalRequirements.ROLE | ConfessionalRequirements.COOLDOWN;

    public double bufferTimeMinutes = 6 * 60; // 6 hours
    public ulong[] bufferExemptRoles = Array.Empty<ulong>();

    [TomlPrecedingComment("Supplementary words to the wordsource")]
    public string hangmanMessageLink = "";
    public string hangmanMessageFormat = "";
    public string[] hangmanWords = Array.Empty<string>();
    public string hangmanWordSource = "https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt";
    public string wordSourceSeparator = "\n";

    public ulong[] casinoRoleIds = Array.Empty<ulong>();

    public Dictionary<string, ulong[]> channelsWhereUsersAreProhibitedFromMedia = new() { { "1", [2, 3] } };
    public ulong[] channelsWhereNoVowelsAreAllowed = Array.Empty<ulong>();

    static Config()
    {
        Console.WriteLine("Initializing config");
        if (!File.Exists(CFG_PATH))
        {
            File.WriteAllText(CFG_PATH, TomletMain.TomlStringFrom(new Config())); // mmm triple parenthesis, v nice
        }

        ReadConfig();
        WriteConfig();
    }

    public static void OutputRawTOML()
    {
        Console.WriteLine(File.ReadAllText(CFG_PATH));
    }

    [MemberNotNull(nameof(values))]
    public static void ReadConfig()
    {
        string configText = File.ReadAllText(CFG_PATH);
        values = TomletMain.To<Config>(configText);
    }

    public static void WriteConfig()
    {
        File.WriteAllText(CFG_PATH, TomletMain.TomlStringFrom(values));
        ConfigChanged?.Invoke();
    }
}
