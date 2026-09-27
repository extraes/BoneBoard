using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoneBoard.Modules;

// Slop-fork port of https://github.com/lyramakesmusic/jevbot/blob/master/jev_bot.py
// The vocabulary is from that project's vocab.txt.
internal sealed class Jev(BoneBot bot) : ModuleBase(bot)
{
    private const string END = "<END>";
    private const int MAX_CHOICES = 255;
    private const int QUESTIONS_PER_CALL = 10;
    private const int MAX_WORDS = 30;
    private const int MIN_WORDS = 2;
    private const int MAX_HISTORY = 5;
    private static TimeSpan JevResponseCooldown => TimeSpan.FromMinutes(Config.values.jevPerUserCooldownMins);
    private static readonly Dictionary<DiscordMember, DateTime> LastJevFullResponseTime = [];
    private static readonly HttpClient Http = new();
    private static readonly SemaphoreSlim GenerationLock = new(1, 1);
    private static readonly HashSet<string> Stopwords = new(
        ("a an the and or but if of to in on at by for with from as is are was were be been " +
         "being it its this that these those i you he she they we me him her them us my your " +
         "his their our not no so then than there here when where which who what how all any " +
         "some each into over under about above below up down out off again more most very " +
         "can will just do does did have has had would could should may might must")
        .Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<char> NoSpaceBefore = [ '.', ',', '!', '?', ';', ':', ')', '"', '\'' ];
    private static readonly Lazy<string[]> BaseVocab = new(() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Assets", "jev-vocab.txt"))
            .Where(w => w.Length > 0 && w is not "unanswered" and not "\\n")
            .ToArray());
    private sealed record EmojiChoice(string Name, DiscordEmoji Emoji);
    private static readonly Lazy<EmojiChoice[]> DiscordStandardEmojis = new(() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Assets", "jev-emojis.txt"))
            .Where(line => line.Length > 0 && !line.StartsWith("# "))
            .Select(line => line.Split('\t', 2))
            // DSharpPlus's emoji map can lag Discord's. Keep the recognized intersection.
            .Where(parts => parts.Length == 2 && DiscordEmoji.IsValidUnicode(parts[0]))
            .Select(parts => new EmojiChoice($"{parts[0]} :{parts[1]}:", DiscordEmoji.FromUnicode(parts[0])))
            .ToArray());

    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (args.Author.IsBot || string.IsNullOrWhiteSpace(Config.values.jevKey) || !Config.values.jevChannels.Contains(args.Channel.Id))
            return;

        var message = args.Message;
        if (message.Channel is null)
            return;
        var isMention = args.MentionedUsers.Any(user => user.Id == client.CurrentUser.Id);
        var isReply = message.ReferencedMessage?.Author?.Id == client.CurrentUser.Id;
        if (!isMention && !isReply)
            return;

        var prompt = Regex.Replace(message.Content ?? "", $@"<@!?{client.CurrentUser.Id}>", "").Trim();
        if (prompt.Length == 0)
            prompt = "hello";

        var hasFullResponseRole = await FullResponseCheck(args);
        if (!hasFullResponseRole)
        {
            try
            {
                var history = await FetchHistory(message);
                foreach (var emoji in await ChooseReaction(prompt, history, args.Guild, Config.values.jevKey))
                    if (await BoneBot.TryReact(message, emoji)) return;
                Logger.Warn("Jev could not add any of its selected emoji reactions.");
            }
            catch (Exception ex)
            {
                Logger.Error("Jev emoji reaction failed", ex);
            }
            return;
        }

        if (!await GenerationLock.WaitAsync(TimeSpan.Zero))
        {
            await BoneBot.TryReact(message, DiscordEmoji.FromUnicode("⏳"));
            return;
        }
        using var typingCancellation = new CancellationTokenSource();
        var typing = KeepTyping(message.Channel, typingCancellation.Token);
        try
        {
            var history = await FetchHistory(message);
            var reply = await GenerateReply(prompt, history, Config.values.jevKey);
            var builder = new DiscordMessageBuilder()
                .WithContent(reply)
                .WithReply(message.Id, false, false).WithAllowedMentions([]);
            await message.Channel.SendMessageAsync(builder);
        }
        catch (Exception ex)
        {
            Logger.Error("Jev text reply failed", ex);
            try
            {
                var robot = DiscordEmoji.FromUnicode("🤖");
                var x = DiscordEmoji.FromUnicode("❌");
                await message.CreateReactionAsync(robot);
                await message.CreateReactionAsync(x);
            }
            catch (Exception sendEx)
            {
                Logger.Error("Jev could make its error reaction", sendEx);
            }
        }
        finally
        {
            await typingCancellation.CancelAsync();
            await typing;
            GenerationLock.Release();
        }
    }

    private static async Task<bool> FullResponseCheck(MessageCreatedEventArgs args)
    {
        var roleId = Config.values.jevFullResponseRole;
        if (roleId == 0) return true;
        try
        {
            var member = args.Author as DiscordMember ?? await args.Guild.GetMemberAsync(args.Author.Id);

            if (LastJevFullResponseTime.TryGetValue(member, out var time) && time + JevResponseCooldown > DateTime.Now)
            {
                return false;
            }

            return member.Roles.Any(role => role.Id == roleId);
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not check the user's role for jev role: " + ex.Message);
            return false;
        }
    }

    private static async Task<List<DiscordEmoji>> ChooseReaction(
        string prompt, List<string> history, DiscordGuild? guild, string key)
    {
        var choices = DiscordStandardEmojis.Value.ToList();
        if (guild is not null)
            choices.AddRange(guild.Emojis.Values.Where(emoji => emoji.IsAvailable)
                .Select(emoji => new EmojiChoice($"server emoji :{emoji.Name}:", emoji)));
        var shuffled = choices.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var state = string.Join('\n', history.Select(h => "User: " + h).Append("User: " + prompt));
        var finalists = new List<EmojiChoice>();

        foreach (var group in shuffled.Chunk(MAX_CHOICES).Chunk(QUESTIONS_PER_CALL))
        {
            var questions = group.Select((bucket, index) =>
                new KeyValuePair<string, object>($"emoji{index}", EmojiQuestion(bucket)))
                .ToDictionary();
            var answers = await Post(state, questions, key);
            foreach (var (bucket, index) in group.Select((bucket, index) => (bucket, index)))
                if (answers.TryGetValue($"emoji{index}", out var answer))
                    finalists.AddRange(Probabilities(answer).OrderByDescending(p => p.Value).Take(2)
                        .Where(p => p.Value > 0 && TryCandidateIndex(p.Key, bucket.Length, out _))
                        .Select(p => bucket[int.Parse(p.Key.AsSpan(1))]));
        }

        if (finalists.Count == 0) throw new HttpRequestException("Jev returned no emoji candidates.");
        var runoff = await Post(state, new Dictionary<string, object>
        {
            ["reaction"] = EmojiQuestion(finalists)
        }, key);
        if (!runoff.TryGetValue("reaction", out var result))
            throw new HttpRequestException("Jev returned no emoji choice.");
        return Probabilities(result).OrderByDescending(p => p.Value)
            .Where(p => TryCandidateIndex(p.Key, finalists.Count, out _))
            .Select(p => finalists[int.Parse(p.Key.AsSpan(1))].Emoji).ToList();
    }

    private static bool TryCandidateIndex(string key, int count, out int index)
    {
        index = -1;
        return key.StartsWith('e') && int.TryParse(key.AsSpan(1), out index) && index >= 0 && index < count;
    }

    private static object EmojiQuestion(IEnumerable<EmojiChoice> candidates) => new
    {
        type = "choice",
        instructions = "Which emoji is the best reaction to the user's message?",
        criteria = candidates.Select((candidate, index) => (candidate, index))
            .ToDictionary(x => $"e{x.index}", x => x.candidate.Name)
    };

    private static async Task KeepTyping(DiscordChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await channel.TriggerTypingAsync();
                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Warn("Jev could not show typing: " + ex.Message);
        }
    }

    private static async Task<List<string>> FetchHistory(DiscordMessage before)
    {
        var history = new List<string>();
        var channel = before.Channel;
        if (channel is null) return history;
        try
        {
            await foreach (var message in channel.GetMessagesBeforeAsync(before.Id, 30))
            {
                if (message.Author?.IsBot != false || string.IsNullOrWhiteSpace(message.Content) ||
                    message.Content.StartsWith('.') || message.Content.StartsWith('/'))
                    continue;
                history.Add(message.Content);
                if (history.Count == MAX_HISTORY)
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Jev could not fetch channel history: " + ex.Message);
        }
        history.Reverse();
        return history;
    }

    private static async Task<string> GenerateReply(string prompt, List<string> history, string key)
    {
        var vocab = BaseVocab.Value.ToList();
        var seen = new HashSet<string>(vocab, StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(prompt.ToLowerInvariant(), "[a-z']+"))
            if (seen.Add(match.Value)) vocab.Add(match.Value);
        vocab.Add(END);

        var words = new List<string>();
        for (var step = 0; step < MAX_WORDS; step++)
        {
            var state = string.Join('\n', history.Select(h => "User: " + h)
                .Append("User: " + prompt).Append("Jev: " + Render(words)));
            var (probabilities, completeness) = await NextWord(state, vocab, Render(words), key);
            if (probabilities.Count == 0)
            {
                if (words.Count == 0) throw new HttpRequestException("Jev returned no word probabilities.");
                break;
            }
            var canStop = words.Count(w => w.Any(char.IsLetterOrDigit)) >= MIN_WORDS;
            if (canStop && completeness >= 0.5) break;

            var ranked = probabilities
                .Where(kv => kv.Value > 0 && (kv.Key != END || canStop) &&
                    !(kv.Key.Length == 1 && NoSpaceBefore.Contains(kv.Key[0]) && words.LastOrDefault() == kv.Key))
                .Select(kv => (kv.Key, Score: kv.Value / Penalty(words, kv.Key)))
                .OrderByDescending(kv => kv.Score).ToArray();
            if (ranked.Length == 0 || ranked[0].Key == END)
                break;
            words.Add(ranked[0].Key);
        }
        return words.Count == 0 ? "..." : Render(words);
    }

    private static async Task<(Dictionary<string, double>, double)> NextWord(string state, List<string> vocab, string reply, string key)
    {
        var shuffled = vocab.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var buckets = shuffled.Chunk(MAX_CHOICES).ToArray();
        var tasks = buckets.Chunk(QUESTIONS_PER_CALL).Select((group, groupIndex) =>
        {
            var questions = group.Select((bucket, index) =>
                new KeyValuePair<string, object>($"b{groupIndex * QUESTIONS_PER_CALL + index}", ChoiceQuestion(bucket, reply)))
                .ToDictionary();
            return Post(state, questions, key);
        }).ToList();
        tasks.Add(Post(state, new Dictionary<string, object>
        {
            ["complete"] = new { type = "noul", instructions = "Is the reply complete?" }
        }, key));

        var answers = await Task.WhenAll(tasks);
        var complete = answers[^1].TryGetValue("complete", out var completion) &&
                       completion.TryGetProperty("noul", out var noul) && noul.TryGetDouble(out var value)
            ? value : 0;
        var finalists = new List<string>();
        foreach (var group in answers.SkipLast(1))
        foreach (var answer in group.Values)
        foreach (var (word, probability) in Probabilities(answer)
                     .OrderByDescending(kv => kv.Value).Take(2))
            if (probability > 0) finalists.Add(word);
        if (!finalists.Contains(END)) finalists.Add(END);
        var runoff = await Post(state, new Dictionary<string, object>
        {
            ["final"] = ChoiceQuestion(finalists.Take(MAX_CHOICES), reply)
        }, key);
        return (runoff.TryGetValue("final", out var final) ? Probabilities(final) : [], complete);
    }

    private static object ChoiceQuestion(IEnumerable<string> words, string reply)
    {
        // The upstream bot mixes bare and contextual criteria on each question.
        var last = reply.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var contextual = Random.Shared.Next(2) == 1;
        return new
        {
            type = "choice", instructions = "Next word?",
            criteria = words.Distinct().ToDictionary(w => w,
                w => contextual ? last is null ? w : $"...{last} {w}" : "")
        };
    }

    private static async Task<Dictionary<string, JsonElement>> Post(string state, Dictionary<string, object> questions, string key)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Config.values.jevEndpoint);
                request.Content = JsonContent.Create(new { model = Config.values.jevModel, state, questions });
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var response = await Http.SendAsync(request, timeout.Token);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (document.RootElement.TryGetProperty("answers", out var answers) &&
                    answers.ValueKind == JsonValueKind.Object)
                    return answers.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Logger.Warn($"Jev API request failed (attempt {attempt + 1}): {ex.Message}");
            }
            if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(1 + 2 * attempt));
        }
        return [];
    }

    private static Dictionary<string, double> Probabilities(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object ||
            !answer.TryGetProperty("probabilities", out var probabilities) ||
            probabilities.ValueKind != JsonValueKind.Object) return [];
        return probabilities.EnumerateObject()
            .Where(p => p.Value.TryGetDouble(out _))
            .ToDictionary(p => p.Name, p => p.Value.GetDouble());
    }

    private static double Penalty(List<string> reply, string word)
    {
        var local = reply.TakeLast(8).Count(w => w == word) +
                    (reply.LastOrDefault() == word ? 2 : 0);
        var penalty = Math.Pow(1.5, local);
        if (!word.All(char.IsLetter))
            return penalty;
        
        var seen = reply.Count(w => w == word);
        penalty *= Stopwords.Contains(word)
            ? Math.Pow(1.6, Math.Min(seen, 6))
            : Math.Pow(2.5, Math.Min(seen, 4));
        
        return penalty;
    }

    private static string Render(IEnumerable<string> tokens)
    {
        var text = "";
        foreach (var token in tokens)
        {
            if (token is "\\n" or "\n") continue;
            text += text.Length == 0 || token.Length == 1 && NoSpaceBefore.Contains(token[0])
                ? token : " " + token;
        }
        return Regex.Replace(text.Trim(), @"(^|[.!?]\s+|\n)([a-z])",
            m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
    }
}
