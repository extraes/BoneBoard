using System.Text.RegularExpressions;

namespace BoneBoard;

public static partial class RegularExpressions
{
    [GeneratedRegex(@"[\s_-]+", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    public static partial Regex WhitespaceIsh { get; }
    
    [GeneratedRegex(@"["",.']")]
    public static partial Regex SymbolRegex { get; }
    
    [GeneratedRegex(@"<@!?(\d+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    public static partial Regex UserMention { get; }

    [GeneratedRegex(@"<@&(\d+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    public static partial Regex RoleMention { get; }

    [GeneratedRegex(@"<#(\d+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    public static partial Regex ChannelMention { get; }

    [GeneratedRegex(@"\w+://\S+", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    public static partial Regex Link { get; }

    [GeneratedRegex(@"<a?:([\w0-9]+):([0-9]+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    public static partial Regex CustomEmoji { get; }
}