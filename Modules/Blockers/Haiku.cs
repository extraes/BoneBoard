using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("haiku")]
internal class Haiku : ModuleBase
{
    public Haiku(BoneBot bot) : base(bot) { }
    
    static Dictionary<ulong, string> reasoningTraces = new Dictionary<ulong, string>();

    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return MessageCheck(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return MessageCheck(msgUpdatedArgs.Message);
        }

        return false;
    }

    private bool MessageCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;
        
        if (!Config.values.channelsWhereMessagesMustBeHaikus.Contains(msg.ChannelId))
            return false;
        
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant

        string content = msg.Content;

        switch (msg.MessageType)
        {
            case DiscordMessageType.Default:
            case DiscordMessageType.Reply:
                break;
            default:
                // Don't care about things like ThreadCreated.
                return false;
        }
        
        if (msg.Reference?.Type == DiscordMessageReferenceType.Forward && (msg.MessageSnapshots?.Count ?? 0) > 0)
            content = string.Join('\n', msg.MessageSnapshots!.Select(m => m.Message.Content));

        if (content.Length == 0) // empty message/only attachments
            return false;

        

        // do quick check for haiku
        string[] lines = content.Split('\n');
        if (lines.Length < 3)
        {
            TryDeleteDontCare(msg, "message not haiku | ancient japan memory | experience woe.");
            reasoningTraces[msg.Id] = "Less than 3 lines";
            Logger.Put($"not 3 lines '{content}' -- lines length = {lines.Length}");
            return true;
        }

        // determine if the haiku is reused
        string line1 = CleanString(lines[0]);
        string line2 = CleanString(lines[1]);
        string line3 = CleanString(lines[2]);

        string haikuSerialize = line1 + line2 + line3;
        ulong authorId = msg.Author?.Id ?? 0;
        // exempt owners bc it could contain a dev who's testing shit
        bool isOwner = bot.client.CurrentApplication.Owners?.Any(u => u.Id == authorId) ?? false;
        if (PersistentData.values.usedHaikus.Contains(haikuSerialize) && !isOwner &&
            !msg.IsEdited /* editing a valid message shouldn't result in deletion */)
        {
            TryDeleteDontCare(msg,
                "the works of others | the blood, sweat, tears poured in them | are not yours to take");
            reasoningTraces[msg.Id] = "haiku already posted";
            return true;
        }

        // wasn't reused -- now mark it down as used
        PersistentData.values.usedHaikus.Add(haikuSerialize);
        PersistentData.WritePersistentData();

        _ = Task.Run(async () =>
        {
            try
            {
                await DetermineHaikuAsync(msg, content);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while determining/handling Haiku-ness of message {content}", ex);
            }
            
            
        });
        return false;
    }
    
    async Task DetermineHaikuAsync(DiscordMessage msg, string content)
    {
        string[] lines = content.Split('\n');
        
        int[] syllableCounts = [0, 0, 0];
        for (var i = 0; i < syllableCounts.Length; i++)
        {
            var line = lines[i];
            var stats = TextStatistics.TextStatistics.Parse(line);
            syllableCounts[i] = (int)Math.Round(stats.WordCount * stats.AverageSyllablesPerWord);
        }

        reasoningTraces[msg.Id] = $"Calculated text stats shows {string.Join(" / ", lines)} has {string.Join(", ", syllableCounts)} syllables.";

        if (!syllableCounts.SequenceEqual([5, 7, 5]))
        {
            TryDeleteDontCare(msg, $"message not haiku | lines do not make 5-7-5 | experience woe.");
            return;
        }
        
        var openAiClient = bot.OpenAI.Value;
        if (openAiClient is null)
        {
            Logger.Put("Can't determine haiku without OpenAI client!");
            return;
        }
        var effortClint = openAiClient.GetChatClient(Config.values.haikuEffortModel);
        // determine effort last because I got that shit on fast mode
        ChatMessage[] messages =
        [
            ChatMessage.CreateSystemMessage(Config.values.haikuEffortPrompt),
            ChatMessage.CreateUserMessage(content)
        ];
        
#pragma warning disable OPENAI001
        ChatServiceTier? serviceTier = Config.values.useFastProcessingForEffort ? new ChatServiceTier("fast") : null;
        var effortRes = await effortClint.CompleteChatAsync(messages, new ChatCompletionOptions()
        {
            ServiceTier = serviceTier
        });
#pragma warning restore OPENAI001
        
        if (effortRes.Value.Role != ChatMessageRole.Assistant)
        {
            Logger.Put($"Why is the LLM giving me a non-assistant response? Why is the {effortRes.Value.Role} yapping?? Why's it saying {effortRes.Value.Content.SelectMany(c => c.Text)}");
        }

        if (effortRes.Value.Content.Any(c => c.Text.Contains("no", StringComparison.InvariantCultureIgnoreCase)))
        {
            TryDeleteDontCare(msg, "you disappoint me | that was a shit haiku bro | thirty minute blast.");
            reasoningTraces[msg.Id] = $"{msg.Content}\n...was deemed low effort";
            try
            {
                if (msg.Author is DiscordMember member && Config.values.timeoutLazyHaikusForMinutes > 0)
                    await member.TimeoutAsync(DateTime.Now.AddMinutes(Config.values.timeoutLazyHaikusForMinutes), "you disappoint me | that was a shit haiku bro | thirty minute blast.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception while timing out the author of a lazy haiku ({msg.Author})", ex);
            }
            // too late to mark the 
            return;
        }


        // yaaaayyyy, the message is a haiku
    }

    [Command("latestReasonings"), Description("Gets latest reasonings fragments")]
    [RequireApplicationOwner]
    public static async Task LatestReasonings(SlashCommandContext ctx)
    {
        if (reasoningTraces.Count == 0)
        {
            await ctx.RespondAsync("No reasoning traces found.", true);
            return;
        }
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<ulong, string> kvp in reasoningTraces.OrderByDescending(kvp => kvp.Key))
        {
            string line = $"# Message {kvp.Key}\n{kvp.Value}";
            sb.AppendLine(line);
        }
        await ctx.RespondAsync(sb.ToString(), true);
    }

    // [Command("getReasoning")]
    // [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    // [RequireGuild]
    // [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task GetReasoning(SlashCommandContext ctx, DiscordMessage msg)
    {
        await ctx.DeferResponseAsync(true);


        if (!reasoningTraces.TryGetValue(msg.Id, out string? reasoningTrace))
        {
            await ctx.FollowupAsync("No reasoning trace found for this message.", true);
            return;
        }


        await ctx.FollowupAsync($"Reasoning trace for message {msg.Id}:\n{reasoningTrace}", true);
    }

    static string CleanString(string str)
    {
        str = string.Join(' ', str.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return str.ToLowerInvariant();
    }

    [Command("checkSyllables")]
    public static async Task MeasureHaikuSyllables(SlashCommandContext ctx, string line1, string line2, string line3)
    {
        string[] lines = [line1, line2, line3];
        
        int[] syllableCounts = [0, 0, 0];
        for (var i = 0; i < syllableCounts.Length; i++)
        {
            var line = lines[i];
            var stats = TextStatistics.TextStatistics.Parse(line);
            syllableCounts[i] = (int)Math.Round(stats.WordCount * stats.AverageSyllablesPerWord);
        }

        bool isHaiku = syllableCounts.SequenceEqual([5, 7, 5]);

        await ctx.RespondAsync($"Line 1: **{syllableCounts[0]} syllable(s)**\n" +
                         $"-# \"{line1}\"\n" +
                         $"Line 2: **{syllableCounts[1]} syllable(s)**\n" +
                         $"-# \"{line2}\"\n" +
                         $"Line 3: **{syllableCounts[2]} syllable(s)**\n" +
                         $"-# \"{line3}\"\n" +
                         $"\nThis **{(isHaiku ? "is" : "is not")}** a valid haiku\n" +
                         (isHaiku
                             ? $"-# For your copy-pasting convenience:\n-# {line1}\n-# {line2}\n-# {line3}"
                             : "🙁"),
            true);
    }

    [Command("clearUsedHaiku")]
    [RequireApplicationOwner]
    public static async Task ClearUsedHaiku(SlashCommandContext ctx, string line1, string line2, string line3)
    {
        line1 = CleanString(line1);
        line2 = CleanString(line2);
        line3 = CleanString(line3);

        string haikuSerialize = line1 + line2 + line3;
        bool cleared = PersistentData.values.usedHaikus.Remove(haikuSerialize);

        await ctx.RespondAsync(cleared ? "ok cleared" : "doesnt look like that one was ever used. weird.");
    }
}
