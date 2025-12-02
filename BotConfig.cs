using Skeleton;
using Tomlet.Attributes;

namespace BoneBoard;

public class BotConfig : GlobalConfigBase<BotConfig>, IGlobalConfig
{
        
    public string quoteFont = "Comfortaa";

    public ulong[] blockedUsers = Array.Empty<ulong>();
    public ulong[] owners = Array.Empty<ulong>();
    public int statusChangeDelaySec = 60 * 15; // once every 15 min
    
    public bool useServerProfileForQuotes = true;
    
    public string frogMessageBase = "";
    public string frogMessageClosedBase = "";
    public string frogLeaderboardLink = "";
    public string frogLeaderboardBase = "";
    [TomlInlineComment("Options: NONE, REACTION, REPLY")]
    public FrogRoleActivation frogRoleActivation = FrogRoleActivation.REPLY;
    public FrogRoleLimitation frogRoleLimitations = FrogRoleLimitation.DAY_OF_WEEK;
    public DayOfWeek frogRoleAvailableOn = DayOfWeek.Wednesday;
    public int leaderboardUpdatePeriodMin = 5;
    
    public ConfessionalRequirements confessionalRestrictions = ConfessionalRequirements.ROLE | ConfessionalRequirements.COOLDOWN;
    
    public double bufferTimeMinutes = 6 * 60; // 6 hours
    public ulong[] bufferExemptRoles = Array.Empty<ulong>();
    
    
    public string hangmanMessageFormat = "";
    [TomlPrecedingComment("Supplementary words to the wordsource")]
    public string[] hangmanWords = Array.Empty<string>();
    public string hangmanWordSource = "https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt";
    public string wordSourceSeparator = "\n";
    
    public ulong[] casinoRoleIds = Array.Empty<ulong>();
    
    public static string LoadFromFile => "./config.toml";
}