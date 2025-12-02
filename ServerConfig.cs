using Tomlet.Attributes;

namespace BoneBoard;

public class ServerConfig
{
    public ulong logChannel;
    public ulong[] quotingRequiredRoles = Array.Empty<ulong>();
    public ulong[] requiredEmojis = Array.Empty<ulong>();
    public int requiredReactionCount = 3;
    [TomlInlineComment("https://discord.com/channels/563139253542846474/1153819135067832370/1283700275617468416")]
    public string quoteeDeleteEmoji = "emoji id, like 1283700275617468416";
    
    public ulong frogRole = 0;
    public string frogMessageLink = "";
    
    public ulong confessionalChannel = 0;
    public ulong confessionalRole = 0;
    
    public string hangmanMessageLink = "";
    
    
    public ulong[] channelsWhereUsersAreProhibitedFromMedia = [1, 2];
    [TomlPrecedingComment("includes stickers & rxns")]
    public ulong[] channelsWhereUsersAreProhibitedFromCustomEmojis = [1, 2];
    public Dictionary<string, string[]> channelsWhereAllFlagsButListedAreProhibited = new()
    {
        {"1", [ "" ]},
    };
    public ulong[] channelsWhereNoVowelsAreAllowed = Array.Empty<ulong>();
}