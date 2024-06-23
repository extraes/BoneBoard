using DSharpPlus;
using DSharpPlus.Entities;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BoneBoard;

internal static partial class Quoter
{
    internal static readonly Regex UserMention = BakedRegex_UserMention();
    [GeneratedRegex(@"<@!?(\d+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex BakedRegex_UserMention();

    internal static readonly Regex RoleMention = BakedRegex_RoleMention();
    [GeneratedRegex(@"<@&(\d+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex BakedRegex_RoleMention();

    internal static readonly Regex ChannelMention = BakedRegex_ChannelMention();
    [GeneratedRegex(@"<#(\d+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex BakedRegex_ChannelMention();

    internal static readonly Regex Link = BakedRegex_Link();
    [GeneratedRegex(@"\w+://\S+", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex BakedRegex_Link();

    internal static readonly Regex CustomEmoji = BakedRegex_CustomEmoji();
    [GeneratedRegex(@"<a?:([\w0-9]+):[0-9]+>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex BakedRegex_CustomEmoji();

    static HttpClient pfpGetter = new();
    static HttpClient mediaGetter = new();

    public static async Task<Image?> GenerateImageFrom(DiscordMessage msg, DiscordClient clint)
    {
        if (msg.Author is null || msg.Channel is null)
            return null;

        bool global = !Config.values.useServerProfile;
        //string extraText; might re-add later?
        string pfpUrl;
        string name;
        string cleanContent;
        string subtext = "";
        string? mediaThumb = null;

        pfpUrl = msg.Author.AvatarUrl ?? msg.Author.DefaultAvatarUrl;
        name = msg.Author.GlobalName ?? msg.Author.Username;
        if (!global)
        {
            DiscordMember? memby = null;
            try
            {
                memby = await msg.Channel.Guild.GetMemberAsync(msg.Author.Id);
            }
            catch (Exception ex)
            {
                Logger.Warn("Exception while trying to fetch member data. User likely was banned/kicked/left.", ex);
            }

            if (memby is not null)
            {
                pfpUrl = memby.GuildAvatarUrl ?? memby.AvatarUrl ?? memby.DefaultAvatarUrl;
                name = memby.Nickname ?? msg.Author.GlobalName ?? msg.Author.Username;
            }
        }

        MatchEvaluator userMentionEvaluator = new(match => ReplaceIdWithUser(match, clint, global ? msg.Channel.Guild : null));
        cleanContent = UserMention.Replace(msg.Content, userMentionEvaluator);

        MatchEvaluator channelMentionEvaluator = new(match => ReplaceIdWithChannel(match, clint, msg.Channel?.Guild));
        cleanContent = ChannelMention.Replace(cleanContent, channelMentionEvaluator);

        if (msg.Attachments.Any()) mediaThumb = msg.Attachments[0].Url;

        if (mediaThumb is null)
        {
            foreach (var embed in msg.Embeds)
            {
                switch (embed.Type)
                {
                    case "gifv":
                    case "image":
                    case "video":
                        mediaThumb = embed?.Thumbnail?.ProxyUrl.ToString();
                        break;
                }
            }
        }

        if (mediaThumb is null)
        {
            var sticker = msg.Stickers?.FirstOrDefault();
            if (sticker is not null && sticker.FormatType != DiscordStickerFormat.LOTTIE)
                mediaThumb = sticker.StickerUrl;
        }

        // if it doesnt embed then i dont want it 😤
        //if (mediaThumb is null)
        //{
        //    int idx = msg.Content.IndexOf("http");
        //    if (idx is not -1)
        //    {
        //        int idxEnd = msg.Content.IndexOf(' ', idx);
        //        if (idxEnd is not -1) mediaThumb = msg.Content.Substring(idx, idxEnd - idx);
        //        else mediaThumb = msg.Content.Substring(idx);
        //    }

        //    //bool isImageFile = 
        //}
        

        using Stream stream = await pfpGetter.GetStreamAsync(pfpUrl);
        using Image img = await Image.LoadAsync(stream);
        Image? media = null;
        if (mediaThumb is not null || mediaThumb?.Length < 5)
        {
            try
            {
                cleanContent = Link.Replace(cleanContent.Replace(mediaThumb, ""), "<Link>");

                if (cleanContent == "<Link>")
                    cleanContent = "";

                using Stream mediaStream = await mediaGetter.GetStreamAsync(mediaThumb);
                media = await Image.LoadAsync(mediaStream);
            }
            catch (Exception ex)
            {
                // dont bailout if media fails to get assigned. its nonessential
                Logger.Warn($"Failed to load media. Continuing because media isn't absolutely necessary. URL: {mediaThumb}", ex);
            }
        }

        cleanContent = CustomEmoji.Replace(cleanContent, @":$1:");

        if (string.IsNullOrWhiteSpace(cleanContent) && media is null)
            subtext = "So true...";
        else if (msg.ReferencedMessage is not null)
            subtext = GetReplyString(msg.ReferencedMessage, clint, global ? msg.Channel.Guild : null);
        else if (msg.Reference is not null)
            subtext = GetReplyString(await msg.Channel.GetMessageAsync(msg.Reference.Message.Id), clint, global ? msg.Channel.Guild : null);

        if (cleanContent == "<Link>")
            cleanContent = "";

        return Quotify(img, name, cleanContent, msg.Timestamp.Year, subtext, media);
    }

    static string CleanContent(DiscordMessage msg, DiscordClient clint, DiscordGuild? guild)
    {
        string cleanContent = msg.Content;
        MatchEvaluator userMentionEvaluator = new(match => ReplaceIdWithUser(match, clint, guild));
        cleanContent = UserMention.Replace(cleanContent, userMentionEvaluator);

        MatchEvaluator channelMentionEvaluator = new(match => ReplaceIdWithChannel(match, clint, msg.Channel?.Guild));
        cleanContent = ChannelMention.Replace(cleanContent, channelMentionEvaluator);

        cleanContent = CustomEmoji.Replace(cleanContent, @":$1:");


        cleanContent = Link.Replace(cleanContent, "<Link>");

        if (cleanContent == "<Link>")
            cleanContent = "";

        return cleanContent;
    }

    static string ReplaceIdWithUser(Match match, DiscordClient clint, DiscordGuild? guild)
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

    static string ReplaceIdWithRole(Match match, DiscordClient clint, DiscordGuild? guild)
    {
        if (guild is null)
            return "@Role";

        ulong id = ulong.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string? name = guild.GetRole(id)?.Name;

        // to date i dont think name has been null, this should be fine
        if (name is null)
            return "@Role";
        else return $"@{name}";
    }

    static string ReplaceIdWithChannel(Match match, DiscordClient clint, DiscordGuild? guild)
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

        // to date i dont think name has been null, this should be fine
        if (name is null)
            return "#channel";
        else return $"#{name}";
    }

    public static Image Quotify(Image pfp, string name, string quote, int year, string extraText, Image? media = null, int width = 1280, int height = 720, int pfpSize = 512)
    {
        name = name.Replace("\n", " ").Trim();
        quote = "\"" + quote + "\"";
        int pfpDistToTop = (height - pfpSize) / 2;
        float marginY = height * 0.25f, marginX = 50, bottomToPfpMargin = (height - pfpSize) / 2;
        const float NAME_FONT_SIZE = 36;


        Image<Rgba32> quoteBaseplate = new(width, height);
        var config = quoteBaseplate.Configuration;
        GraphicsOptions opt = new();
        quoteBaseplate.Mutate(x => x.BackgroundColor(Color.Black));

        var pfpRect = new Rectangle(width - pfpSize, pfpDistToTop, pfpSize, pfpSize);
        var pfpPt = new Point(width - pfpSize, pfpDistToTop);

        pfp = pfp.Clone(x => x.Resize(pfpSize, pfpSize));
        quoteBaseplate.Mutate(x => x.DrawImage(pfp, pfpPt, .9f));

        Color halfBlack = Color.FromRgba(0, 0, 0, 128);
        Color transparent = Color.FromRgba(0, 0, 0, 0);

        ColorStop blackStart = new(0, Color.Black);
        ColorStop halfBlackStart = new(0, halfBlack);
        ColorStop transparentEnd = new(1, transparent);

        LinearGradientBrush lgbt = new(new Point(width - pfpSize, 0), new Point(width, 0), GradientRepetitionMode.None, blackStart, transparentEnd);
        LinearGradientBrush lgbtq = new(new Point(width - pfpSize, height), new Point(width, 0), GradientRepetitionMode.None, blackStart, transparentEnd);
        LinearGradientBrush lgbtqia = new(new Point(width - pfpSize, 0), new Point(width, height), GradientRepetitionMode.None, blackStart, transparentEnd);

        quoteBaseplate.Mutate(x => x.Fill(lgbt, pfpRect));
        quoteBaseplate.Mutate(x => x.Fill(lgbtq, pfpRect));
        quoteBaseplate.Mutate(x => x.Fill(lgbtqia, pfpRect));

        FontFamily ff = SystemFonts.Get(Config.values.quoteFont);
        FontFamily ffEmoji = SystemFonts.Get("Twemoji Mozilla");
        Font usernameFont = new(ff, NAME_FONT_SIZE);

        bool offsetMediaAndQuote = media is not null && (float)media.Width / media.Height > 2;
        const float THIN_IMG_OFFSET = 0.25f;

        if (media is not null)
        {
            media.Mutate(x => x.EntropyCrop(0.1f)); // may need to try adding extra padding
            float mediaAreaWidth = pfpPt.X;
            float mediaAreaHeight = offsetMediaAndQuote ? height * (1 - THIN_IMG_OFFSET) : height;
            float mediaAreaMiddleX = mediaAreaWidth / 2;
            float mediaAreaMiddleY = offsetMediaAndQuote ? (height * THIN_IMG_OFFSET) + (mediaAreaHeight / 2) : height / 2;

            if (media.Height + media.Width == 0)
                throw new InvalidOperationException($"Media image is zero-size! How even? Dimensions: {media.Width} x {media.Height}");
            float resizeX = mediaAreaWidth / media.Width;
            float resizeY = mediaAreaHeight / media.Height;
            float resizeRatio = Math.Min(resizeX, resizeY); // shrink media image to fit bounds
            Size newSize = (Size)(media.Size * resizeRatio);
            media.Mutate(x => x.Resize(newSize));

            Point mediaTopLeft = new(0, (int)mediaAreaMiddleY - media.Height / 2);
            Point mediaBottomRight = new(media.Width + mediaTopLeft.X, media.Height + mediaTopLeft.Y);

            quoteBaseplate.Mutate(x => x.DrawImage(media, mediaTopLeft, 1));

            Rectangle mediaRect = new(mediaTopLeft, media.Size);
            if (quote.Length != 2)
            {
                lgbt = new(new Point(mediaBottomRight.X, mediaTopLeft.Y), new Point(mediaTopLeft.X, mediaTopLeft.Y), GradientRepetitionMode.None, blackStart, transparentEnd);
                lgbtq = new(new Point(mediaBottomRight.X, mediaBottomRight.Y), new Point(mediaTopLeft.X, mediaTopLeft.Y), GradientRepetitionMode.None, blackStart, transparentEnd);
                lgbtqia = new(new Point(mediaBottomRight.X, mediaTopLeft.Y), new Point(mediaTopLeft.X, mediaBottomRight.Y), GradientRepetitionMode.None, blackStart, transparentEnd);


                quoteBaseplate.Mutate(x => x.Fill(lgbt, mediaRect));
                quoteBaseplate.Mutate(x => x.Fill(lgbtq, mediaRect));
                quoteBaseplate.Mutate(x => x.Fill(lgbtqia, mediaRect));
            }
            else
            {
                lgbt = new(new Point(mediaBottomRight.X, mediaTopLeft.Y), new Point(mediaTopLeft.X, mediaTopLeft.Y), GradientRepetitionMode.None, halfBlackStart, transparentEnd);
                quoteBaseplate.Mutate(x => x.Fill(lgbt, mediaRect));

                if (!string.IsNullOrEmpty(extraText))
                {
                    lgbtq = new(new Point(mediaBottomRight.X, mediaBottomRight.Y), new Point(mediaTopLeft.X, mediaTopLeft.Y), GradientRepetitionMode.None, blackStart, transparentEnd);
                    //lgbtqia = new(new Point(mediaBottomRight.X, mediaTopLeft.Y), new Point(mediaTopLeft.X, mediaBottomRight.Y), GradientRepetitionMode.None, blackStart, transparentEnd);
                    quoteBaseplate.Mutate(x => x.Fill(lgbtq, mediaRect));
                }
            }
        }

        // don't draw the quote if there was no text content in the first place
        if (quote.Length != 2)
        {
            float quoteSize = 512 / MathF.Sqrt(quote.Length);
            float quoteSizeSmaller = 384 / MathF.Sqrt(quote.Length);
            Font quoteFont = ff.CreateFont(quoteSize, FontStyle.Bold);
            Font quoteFontSmaller = ff.CreateFont(quoteSizeSmaller, FontStyle.Bold);
            float textWidth = (width - pfpSize);
            float textHeight = (height - 2 * marginY);
            float originY = offsetMediaAndQuote ? (height * (1 - THIN_IMG_OFFSET)) / 2 : height / 2;
            RichTextOptions quoteOpt = new(quoteFont)
            {
                Origin = new(marginX + textWidth / 2, originY),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                WrappingLength = textWidth,
                FallbackFontFamilies = new List<FontFamily>()
                    {
                        ffEmoji
                    }.AsReadOnly(),
                WordBreaking = WordBreaking.BreakWord,
            };
            RichTextOptions quoteOptSmaller = new(quoteOpt)
            {
                Font = quoteFontSmaller
            };
            bool sameLineCount = TextMeasurer.CountLines(quote, quoteOpt) == TextMeasurer.CountLines(quote, quoteOptSmaller);
            if (sameLineCount)
                quoteBaseplate.Mutate(x => x.DrawText(quoteOpt, quote, Color.White));
            else
                quoteBaseplate.Mutate(x => x.DrawText(quoteOptSmaller, quote, Color.White));
        }

        if (!string.IsNullOrWhiteSpace(extraText))
        {
            Font extraTextFont = ff.CreateFont(24, FontStyle.Italic);
            RichTextOptions extraOpt = new(extraTextFont)
            {
                Origin = new(marginX, height - 0.5f * marginY),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                FallbackFontFamilies = new List<FontFamily>()
                    {
                        ffEmoji
                    }.AsReadOnly(),
            };
            quoteBaseplate.Mutate(x => x.DrawText(extraOpt, extraText, Color.White));
        }

        PointF namePos = new(width - pfpSize + (marginX / 2), height - bottomToPfpMargin - (NAME_FONT_SIZE * 1.25f));
        string nameTxt = "- " + name + ", " + year;
        RichTextOptions usernameOptions = new(usernameFont)
        {
            Origin = namePos,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        while (TextMeasurer.MeasureBounds(nameTxt, usernameOptions).Width > pfpSize - marginX)
        {
            usernameFont = new(usernameFont, usernameFont.Size * 0.9f);
            usernameOptions.Font = usernameFont;
        }

        quoteBaseplate.Mutate(x => x.DrawText(usernameOptions, nameTxt, Color.White));


        return quoteBaseplate;
    }

    private static string GetReplyString(DiscordMessage refMsg, DiscordClient clint, DiscordGuild? guild)
    {
        if (refMsg is null || refMsg.Author is null) return "";
        if (refMsg.MessageType != DiscordMessageType.Reply && refMsg.MessageType != DiscordMessageType.Default) return "";
        
        if (string.IsNullOrEmpty(refMsg.Content))
        {
            if (refMsg.Stickers is not null && refMsg.Stickers.Count != 0)
                return $"A \"{refMsg.Stickers[0].Name}\" sticker from {GetAuthorString(refMsg)}";
            if (refMsg.Attachments.Count != 0)
            {
                string mimeType = refMsg.Attachments[0].MediaType?.Split("/")[0] ?? "";
                string attachmentFile;
                string anA = mimeType[0] switch
                {
                    var x when
                    x == 'a' ||
                    x == 'e' ||
                    x == 'i' ||
                    x == 'o' ||
                    x == 'u' => "an",
                    _ => "a"
                };

                if (mimeType == "image" || mimeType == "video" || mimeType == "audio")
                    attachmentFile = mimeType;
                else attachmentFile = $"file named \"{refMsg.Attachments[0].FileName}\"";

                return $"Replying to {anA} {attachmentFile} from {GetAuthorString(refMsg)}";
            }
        }

        return $"Replying to \"{Shorten(CleanContent(refMsg, clint, guild), 30).Replace("\n", "")}\" from {GetAuthorString(refMsg)}";
    }

    private static string GetAuthorString(DiscordMessage msg)
    {
        if (msg.Author is null) return "someone";

        if (Config.values.useServerProfile && msg.Channel is not null && msg.Channel.Type != DiscordChannelType.Private)
        {
            try
            {
                DiscordMember memby = msg.Author as DiscordMember ?? msg.Channel.Guild.GetMemberAsync(msg.Author.Id).GetAwaiter().GetResult(); // yaaay more synchony!
                return memby.DisplayName;
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to get author name string. Falling back to global nickname/username.", ex);
            }
        }

        return msg.Author.GlobalName ?? msg.Author.Username;
    }

    static string Shorten(string str, int maxLength = 150)
    {
        str = str.Trim();
        if (str.Length > maxLength) return str[..(maxLength - 3)] + "...";
        else return str;
    }
}
