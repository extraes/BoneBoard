using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BoneBoard;

public static partial class TextThings
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
    
    [SuppressMessage("ReSharper", "ConvertToLocalFunction")]
    public static string QuickCleanContent(DiscordMessage msg, DiscordClient clint, DiscordGuild? guild)
    {
        string cleanContent = msg.Content;
        MatchEvaluator userMentionEvaluator = match => ReplaceIdWithUser(match, clint, guild);
        cleanContent = UserMention.Replace(cleanContent, userMentionEvaluator);

        MatchEvaluator channelMentionEvaluator = match => ReplaceIdWithChannel(match, clint, msg.Channel?.Guild);
        cleanContent = ChannelMention.Replace(cleanContent, channelMentionEvaluator);
        
        MatchEvaluator roleMentionEvaluator = match => ReplaceIdWithRole(match, clint, guild);
        cleanContent = RoleMention.Replace(cleanContent, roleMentionEvaluator);

        cleanContent = CustomEmoji.Replace(cleanContent, ":$1:");

        cleanContent = Link.Replace(cleanContent, "<Link>");

        if (cleanContent == "<Link>")
            cleanContent = "";

        return cleanContent;
    }

    public static string ReplaceIdWithUser(Match match, DiscordClient clint, DiscordGuild? guild)
    {
        ulong id = ulong.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string? name;

        // not that good to synchronize async like this, but its fine enough cuz its only called from an async method lol
        if (guild is not null)
        {
            try
            {
                name = guild.GetMemberAsync(id).GetAwaiter().GetResult().DisplayName;
            }
            catch(Exception ex)
            {
                Logger.Warn($"Failed to fetch guild member from ID {id}, they probably left or were kicked/banned.", ex);
                DiscordUser? user = clint.GetUserAsync(id, true).GetAwaiter().GetResult();
                name = user?.GlobalName ?? user?.Username;
            }
        }
        else
        {
            DiscordUser? user = clint.GetUserAsync(id, true).GetAwaiter().GetResult();
            name = user?.GlobalName ?? user?.Username;
        }

        // to date i dont think name has been null, this should be fine
        if (name is null)
            return "@Person";
        else return $"@{name}";
    }

    public static string ReplaceIdWithRole(Match match, DiscordClient clint, DiscordGuild? guild)
    {
        if (guild is null)
            return "@Role";

        ulong id = ulong.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string? name = null;
        try
        {
            name = guild.GetRoleAsync(id).GetAwaiter().GetResult().Name;
        }
        catch
        {
            // ignored
        }

        // to date i don't think name has been null, this should be fine
        return name is null ? "@Role" : $"@{name}";
    }

    public static string ReplaceIdWithChannel(Match match, DiscordClient clint, DiscordGuild? guild)
    {
        if (guild is null)
            return "#channel";

        ulong id = ulong.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string? name;

        // not that good to synchronize async like this, but its fine enough cuz its only called from an async method lol
        try
        {
            name = guild.GetChannelAsync(id).GetAwaiter().GetResult()?.Name ?? BoneBot.Bots[clint].allChannels[guild].First(ch => ch.Id == id).Name;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to fetch guild channel from ID {id}. Why?", ex);
            name = null;
            //DiscordUser? user = clint.GetUserAsync(id, true).GetAwaiter().GetResult();
            //name = user?.GlobalName ?? user?.Username;
        }

        // to date i don't think name has been null, this should be fine
        return name is null ? "#channel" : $"#{name}";
    }
}