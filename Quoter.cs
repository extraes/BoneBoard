using DSharpPlus;
using DSharpPlus.Entities;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp.Formats.Gif;
using StableCube.Media.Gifsicle;

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
    [GeneratedRegex(@"<a?:([\w0-9]+):([0-9]+)>", RegexOptions.IgnoreCase | RegexOptions.ECMAScript, "en-US")]
    private static partial Regex BakedRegex_CustomEmoji();

    static readonly HttpClient pfpGetter = new();
    static readonly HttpClient mediaGetter = new();
    static readonly HttpClient emojiGetter = new();
    const string CUSTOM_EMOJI_SUBSTITUTE = "🔲";
    static readonly List<Match> matchedCustomEmojis = new();
    static readonly ConditionalWeakTable<string, Image> emojiImageCache = new();

    public static async Task<Image?> GenerateImageFrom(DiscordMessage msg, DiscordClient clint)
    {
        if (msg.Author is null || msg.Channel is null)
            return null;

        bool isConfession = msg.Channel.Id == Config.values.confessionalChannel
                         && msg.Content.Length == 0
                         && msg.Author == clint.CurrentUser
                         && msg.Embeds.Count > 0;
        DiscordEmbed? confessionEmbed =  isConfession ? msg.Embeds[0] : null;
        bool global = !Config.values.useServerProfile;
        //string extraText; might re-add later?
        string startingContent = isConfession ? confessionEmbed?.Description ?? msg.Content : msg.Content;
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

        if (isConfession)
        {
            DiscordEmbedFooter? footer = confessionEmbed!.Footer;
            if (footer?.Text?.Contains("AI") ?? false)
                name = "OpenAI's finest finetune";
            else
                name = "Anonymous confessor";
        }

        MatchEvaluator userMentionEvaluator = new(match => ReplaceIdWithUser(match, clint, global ? msg.Channel.Guild : null));
        cleanContent = UserMention.Replace(startingContent, userMentionEvaluator);

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

        List<Match> customEmojis = matchedCustomEmojis;
        customEmojis.Clear();
        MatchEvaluator emojiMentionEvaluator = match => { customEmojis.Add(match); return CUSTOM_EMOJI_SUBSTITUTE; };
        cleanContent = CustomEmoji.Replace(cleanContent, emojiMentionEvaluator);

        if (string.IsNullOrWhiteSpace(cleanContent) && media is null)
            subtext = "So true...";
        else if (msg.ReferencedMessage is not null)
            subtext = GetReplyString(msg.ReferencedMessage, clint, global ? msg.Channel.Guild : null);
        else if (msg.Reference is not null)
            subtext = GetReplyString(await msg.Channel.GetMessageAsync(msg.Reference.Message.Id), clint, global ? msg.Channel.Guild : null);

        if (cleanContent == "<Link>")
            cleanContent = "";

        GlyphReplacer? emojiDrawer = null;
        List<int> indices = GetIndicesOf(cleanContent, CUSTOM_EMOJI_SUBSTITUTE).ToList(); // the beginning quote is added to the string lol
        if (customEmojis.Count == 0)
            emojiDrawer = null;
        else if (customEmojis.Count == indices.Count)
        {
            List<Image?> emojis = await FetchCustomEmojis(customEmojis);

            emojiDrawer = (quoteImages, allGlyphs, blockBounds, lineCount) =>
            {
                (Image<Rgba32> quoteBeforeText, Image<Rgba32> quoteAfterText) = quoteImages;
                //float lineHeight = blockBounds.Height / lineCount;
                //float currX = 0;
                //float currY = 0;

                ////var indicesEnumerator = indices.GetEnumerator();
                //int currEmojiIdx = 0;

                //for(int i = 0; i < allGlyphs.Length; i++)
                //{
                //    GlyphBounds bounds = allGlyphs[i];
                //    currX += bounds.Bounds.Width;
                //    if (currX > blockBounds.Width)
                //    {
                //        currX = 0;
                //        currY += lineHeight;
                //        //continue;
                //    }

                //    if (currEmojiIdx >= indices.Count)
                //        break;

                //    if (i >= indices[currEmojiIdx])
                //    {
                //        var emoji = emojis[currEmojiIdx];
                //        currEmojiIdx++;
                //        if (emoji is not null)
                //        {
                //            var emojiRect = new Rectangle((int)(currX + blockBounds.X), (int)(currY + blockBounds.Y), (int)bounds.Bounds.Width, (int)bounds.Bounds.Width);
                //            quoteBaseplate.Mutate(x => x.DrawImage(emoji, emojiRect.Location, 1));
                //        }
                //    }

                //    i += bounds.Codepoint.Utf16SequenceLength - 1;
                //}

                //int remap = 0; // so it can be reused, otherwise indices[i] without remap would throw an exception when slicing
                //for (int i = 0; i < indices.Count; i++)
                //{
                //    if (emojis[i] is null)
                //        continue;

                //    int sliceTo = Math.Clamp(indices[i] + remap - 1, 0, int.MaxValue);
                //    remap = 0;
                //    foreach (GlyphBounds pastBound in allGlyphs.Slice(0, sliceTo))
                //    {
                //        remap -= pastBound.Codepoint.Utf16SequenceLength - 1;
                //    }
                
                //    var bounds = allGlyphs[indices[i] + remap];
                //    var emoji = emojis[i]!;
                //    var emojiRect = new Rectangle((int)bounds.Bounds.X, (int)bounds.Bounds.Y, (int)bounds.Bounds.Width, (int)bounds.Bounds.Width);
                //    emoji.Mutate(x => x.Resize(new ResizeOptions() { Mode = ResizeMode.Max, Size = new Size(emojiRect.Width, emojiRect.Width) }));
                //    quoteBaseplate.Mutate(x => x.DrawImage(emoji, emojiRect.Location, 1));
                //}

                //for (int i = 0; i < cleanContent.Length; i++)
                //{
                //    if (i >= indices.Count)
                //        break;

                //    var emoji = emojis[i];
                //    if (emoji is null)
                //        continue;

                //    var bounds = allGlyphs[i];
                //    var emojiRect = new Rectangle((int)bounds.Bounds.X, (int)bounds.Bounds.Y, (int)bounds.Bounds.Width, (int)bounds.Bounds.Width);
                //    emoji.Mutate(x => x.Resize(new ResizeOptions() { Mode = ResizeMode.Max, Size = new Size(emojiRect.Width, emojiRect.Width) }));
                //    quoteBaseplate.Mutate(x => x.DrawImage(emoji, emojiRect.Location, 1));
                //}

                //int stringToGlyphOffset = 0;
                var emojiEnumerator = emojis.GetEnumerator();
                while (emojiEnumerator.Current is null)
                    emojiEnumerator.MoveNext();

                GlyphBounds bounds;
                for (int i = 0; i < allGlyphs.Length; i+= bounds.Codepoint.Utf16SequenceLength)
                {
                    bounds = allGlyphs[i];
                    if (CUSTOM_EMOJI_SUBSTITUTE != bounds.Codepoint.ToString())
                    {
                        continue;
                    }

                    var emoji = emojiEnumerator.Current;
                    if (emoji is null)
                    {
                        emojiEnumerator.MoveNext();
                        continue;
                    }

                    var brush = new SolidBrush(Color.White);
                    //quoteBeforeText.Mutate(x => x.Skew(20, 10));
                    var emojiRect = new Rectangle((int)bounds.Bounds.X, (int)bounds.Bounds.Y, (int)bounds.Bounds.Width, (int)bounds.Bounds.Width);
                    var rebackRect = new RectangleF((int)Math.Floor(bounds.Bounds.X), (int)Math.Floor(bounds.Bounds.Y), (int)Math.Ceiling(bounds.Bounds.Width) + 1, (int)Math.Ceiling(bounds.Bounds.Width) + 1);
                    ImageBrush brushImage = new(quoteBeforeText, rebackRect);
                    quoteAfterText.Mutate(x => x.Fill(brushImage, rebackRect));

                    if (emoji.Height < emoji.Width)
                    {
                        emoji = emoji.Clone(x => x.Pad(emoji.Width, emoji.Width));
                    }
                    else if (emoji.Width < emoji.Height)
                    {
                        emoji = emoji.Clone(x => x.Pad(emoji.Height, emoji.Height));
                    }
                    emoji.Mutate(x => x.Resize(new ResizeOptions() { Mode = ResizeMode.Max, Size = new Size(emojiRect.Width, emojiRect.Width), Position = AnchorPositionMode.Center }));

                    quoteAfterText.Mutate(x => x.DrawImage(emoji, emojiRect.Location, 1));

                    emojiEnumerator.MoveNext();
                    continue;
                }
            };
        }
        else
        {
            //int idx = 0;
            //foreach (Match match in customEmojis)
            //{

            //}
        }

        return Quotify(img, name, cleanContent, msg.Timestamp.Year, subtext, media, glyphReplacer: emojiDrawer);
    }

    static async Task<List<Image?>> FetchCustomEmojis(List<Match> matches)
    {
        List<Image?> images = new(matches.Count);
        foreach (Match emojiMatch in matches)
        {
            var id = emojiMatch.Groups[2].Value;
            
            if (emojiImageCache.TryGetValue(id, out var img))
            {
                images.Add(img);
                continue;
            }

            string url = $"https://cdn.discordapp.com/emojis/{id}.png?size=320&quality=lossless";
            try
            {
                using Stream stream = await emojiGetter.GetStreamAsync(url);
                Image<Rgba32> emoji = await Image.LoadAsync<Rgba32>(stream);
                emojiImageCache.Add(id, emoji);
                images.Add(emoji);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to fetch emoji image from URL {url}.", ex);
                images.Add(null);
                continue;
            }
        }

        return images;
    }

    static IEnumerable<int> GetIndicesOf(string str, string substr)
    {
        int idx = 0;
        while (true)
        {
            idx = str.IndexOf(substr, idx);
            if (idx == -1)
                yield break;
            yield return idx;
            idx++;
        }
    }

    static string QuickCleanContent(DiscordMessage msg, DiscordClient clint, DiscordGuild? guild)
    {
        string cleanContent = msg.Content;
        MatchEvaluator userMentionEvaluator = match => ReplaceIdWithUser(match, clint, guild);
        cleanContent = UserMention.Replace(cleanContent, userMentionEvaluator);

        MatchEvaluator channelMentionEvaluator = match => ReplaceIdWithChannel(match, clint, msg.Channel?.Guild);
        cleanContent = ChannelMention.Replace(cleanContent, channelMentionEvaluator);

        cleanContent = CustomEmoji.Replace(cleanContent, ":$1:");

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
        string? name = null;
        try
        {
            name = guild.GetRoleAsync(id).GetAwaiter().GetResult().Name;
        }
        catch { }

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

    public delegate void GlyphReplacer((Image<Rgba32> quoteBeforeText, Image<Rgba32> quoteAfterText) quoteImages, ReadOnlySpan<GlyphBounds> allGlyphs, FontRectangle textblockBounds, int lineCount);

    public static Image Quotify(Image pfp, string name, string quote, int year, string extraText, Image? media = null,
        int width = 1280, int height = 720, int pfpSize = 512, GlyphReplacer? glyphReplacer = null)
    {
        if (media is null || media.Frames.Count == 1)
        {
            return QuotifyFrame(pfp, name, quote, year, extraText, media, width, height,
                pfpSize, glyphReplacer);
        }
        else
        {
            return QuotifyAnimated(pfp, name, quote, year, extraText, media, width, height,
                pfpSize, glyphReplacer);
        }
    }
    
    public static Image QuotifyAnimated(Image pfp, string name, string quote, int year, string extraText,
        Image media, int width = 1280, int height = 720, int pfpSize = 512, GlyphReplacer? glyphReplacer = null)
    {
        Image outputGif = QuotifyFrame(pfp, name, quote, year, extraText, media, width, height, pfpSize,
            glyphReplacer);
        outputGif.Metadata.GetGifMetadata().RepeatCount = 0;
        outputGif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay
            = media.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay;
        
#if false
        ImageFrame[] frames = new ImageFrame[media.Frames.Count];
        var loopRes = Parallel.For(0, media.Frames.Count, (i, state) =>
        {
            Logger.Put($"Working on frame idx {i} (of {media.Frames.Count})");
            // this is inefficient as FUCK but i DONT CARE!!!!!
            Image frameColor = QuotifyFrame(pfp, name, quote, year, extraText, media.Frames.ExportFrame(i), width,
                height,
                pfpSize, glyphReplacer);

            var delayTime = media.Frames[i].Metadata.GetGifMetadata().FrameDelay;
            var newFrameMetadata = frameColor.Frames.RootFrame.Metadata.GetGifMetadata();
            newFrameMetadata.FrameDelay = delayTime;

            // outputGif.Frames.AddFrame(frameColor.Frames.RootFrame);
            frames[i] = frameColor.Frames.RootFrame;
        });

        while (!loopRes.IsCompleted)
        {
        }

        foreach (var frame in frames)
        {
            outputGif.Frames.AddFrame(frame);
        }
#else
        
        // for (int i = media.Frames.Count - 1; i >= 0; i--)
        // for (int i = 0; i < media.Frames.Count; i++)
        while (media.Frames.Count > 1)
        {
            int i = 0;
            // this is inefficient as FUCK but i DONT CARE!!!!!
            Image currFrame = media.Frames.ExportFrame(i);
            Image frameColor = QuotifyFrame(pfp, name, quote, year, extraText, currFrame, width, height, pfpSize,
                glyphReplacer);

             
            var delayTime = currFrame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay;
            
            // first frame is a bitch and makes me want to kill my self.
            // if (outputGif.Frames.Count == 1)
            // {
            //     outputGif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = delayTime;
            //     // outputGif.Frames.RootFrame.Metadata.GetGifMetadata().DisposalMethod =
            //     //     GifDisposalMethod.RestoreToBackground;
            //     // ImageBrush brush = new ImageBrush(frameColor);
            //     outputGif.Mutate(x => x.DrawImage(frameColor, 1));
            //     // outputGif.
            //     continue;
            // }

            var newFrameMetadata = frameColor.Frames.RootFrame.Metadata.GetGifMetadata();
            // newFrameMetadata.DisposalMethod = GifDisposalMethod.NotDispose;
            newFrameMetadata.FrameDelay = delayTime;

            // outputGif.Frames.InsertFrame(0, frameColor.Frames.RootFrame);
            outputGif.Frames.AddFrame(frameColor.Frames.RootFrame);
            // if (i == 0)
            //     outputGif.Frames.RemoveFrame(0);
        }
#endif

        // first frame is blank from creation. remove it.
        // outputGif.Frames.RemoveFrame(0);
        return outputGif;
    }

    public static Image QuotifyFrame(Image pfp, string name, string quote, int year, string extraText, Image? media = null,
        int width = 1280, int height = 720, int pfpSize = 512, GlyphReplacer? glyphReplacer = null)
    {
        name = name.Replace("\n", " ").Trim();
        quote = "\"" + quote + "\"";
        int pfpDistToTop = (height - pfpSize) / 2;
        float marginY = height * 0.25f, marginX = 50, bottomToPfpMargin = (height - pfpSize) / 2f;
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
                static float Lerp(float a, float b, float t) // unclamped
                {
                    return a + (b - a) * t;
                }
                int yTenPerc = (int)Lerp(mediaBottomRight.Y, mediaTopLeft.Y, 0.1f);
                int yNinetyPerc = (int)Lerp(mediaBottomRight.Y, mediaTopLeft.Y, 0.9f);
                int xTenPerc = (int)Lerp(mediaBottomRight.X, mediaTopLeft.X, 0.1f);
                int xNinetyPerc = (int)Lerp(mediaBottomRight.X, mediaTopLeft.X, 0.9f);
                lgbt = new(new Point(mediaBottomRight.X, mediaTopLeft.Y), new Point(mediaTopLeft.X, mediaTopLeft.Y), GradientRepetitionMode.None, halfBlackStart, transparentEnd);
                lgbtq = new(mediaBottomRight, new Point(mediaBottomRight.X, yTenPerc), GradientRepetitionMode.None, blackStart, transparentEnd);
                lgbtqia = new(mediaBottomRight, new Point(xTenPerc, mediaBottomRight.Y), GradientRepetitionMode.None, blackStart, transparentEnd);
                LinearGradientBrush lgbtqiaa = new(new Point(mediaBottomRight.X, mediaTopLeft.Y), new Point(mediaBottomRight.X, yNinetyPerc), GradientRepetitionMode.None, blackStart, transparentEnd);

                quoteBaseplate.Mutate(x => x.Fill(lgbt, mediaRect));
                quoteBaseplate.Mutate(x => x.Fill(lgbtq, mediaRect));
                quoteBaseplate.Mutate(x => x.Fill(lgbtqia, mediaRect));
                quoteBaseplate.Mutate(x => x.Fill(lgbtqiaa, mediaRect));

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
            int normalLineCount = TextMeasurer.CountLines(quote, quoteOpt);
            int smallerLineCount = TextMeasurer.CountLines(quote, quoteOptSmaller);
            bool sameLineCount = normalLineCount == smallerLineCount;
            
            RichTextOptions quoteOptFinal = sameLineCount ? quoteOpt : quoteOptSmaller;
            int lineCount = sameLineCount ? normalLineCount : smallerLineCount;
            
            //quoteBaseplate.Mutate(x => x.DrawText(quoteOptFinal, quote, Color.White)); moved into if blocks
            
            if (glyphReplacer is not null && TextMeasurer.TryMeasureCharacterBounds(quote, quoteOptFinal, out var charBounds))
            {
                Image<Rgba32> pretextBaseplate = quoteBaseplate.CloneAs<Rgba32>();
                quoteBaseplate.Mutate(x => x.DrawText(quoteOptFinal, quote, Color.White));

                ReadOnlySpan<GlyphBounds> boundsWithoutQuotes = charBounds[1..^1];
                var textBounds = TextMeasurer.MeasureBounds(quote, quoteOptFinal);
                glyphReplacer((pretextBaseplate, quoteBaseplate), boundsWithoutQuotes, textBounds, lineCount);
            }
            else
            {
                quoteBaseplate.Mutate(x => x.DrawText(quoteOptFinal, quote, Color.White));
            }
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
        if (Config.values.blockedUsers.Contains(refMsg.Author.Id)) return "";
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

        return $"Replying to \"{Shorten(QuickCleanContent(refMsg, clint, guild), 30).Replace("\n", "")}\" from {GetAuthorString(refMsg)}";
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
