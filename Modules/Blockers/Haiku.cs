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
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("haiku")]
internal class Haiku : ModuleBase
{
    public Haiku(BoneBot bot) : base(bot)
    {
        Config.ConfigChanged += () => openAiClient = null;
    }
    
    OpenAIClient? openAiClient;
    static Dictionary<ulong, string> reasoningTraces = new Dictionary<ulong, string>();

    protected override async Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return await MessageCheckAsync(msgCreatedArgs.Message);
        }
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return await MessageCheckAsync(msgUpdatedArgs.Message);
        }

        return false;
    }

    private async Task<bool> MessageCheckAsync(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;

        string content = msg.Content;
        if (msg.Reference?.Type == DiscordMessageReferenceType.Forward && (msg.MessageSnapshots?.Count ?? 0) > 0)
            content = string.Join('\n', msg.MessageSnapshots!.Select(m => m.Message.Content));

        if (content.Length == 0) // empty message/only attachments
            return false;

        if (!Config.values.channelsWhereMessagesMustBeHaikus.Contains(msg.ChannelId))
            return false;


        if (openAiClient is null)
        {
            if (string.IsNullOrEmpty(Config.values.openAiToken))
                return false;
            else
                openAiClient ??= new(new System.ClientModel.ApiKeyCredential(Config.values.openAiToken));
        }

        // do quick check for haiku
        string[] lines = content.Split('\n');
        if (lines.Length < 3)
        {
            await TryDeleteAsync(msg, "message not haiku | ancient japan memory | experience woe.");
            reasoningTraces[msg.Id] = "Less than 3 lines";
            Logger.Put($"not 3 lines '{content}' -- lines length = {lines.Length}");
            return true;
        }

        var clint = openAiClient.GetOpenAIResponseClient(Config.values.haikuAiModel);
        var effortClint = openAiClient.GetChatClient(Config.values.haikuAiModel);

        // determine if the haiku is reused
        string line1 = CleanString(lines[0]);
        string line2 = CleanString(lines[1]);
        string line3 = CleanString(lines[2]);

        string haikuSerialize = line1 + line2 + line3;
        ulong authorId = msg.Author?.Id ?? 0;
        // exempt owners bc it could contain a dev whos testing shit
        if (PersistentData.values.usedHaikus.Contains(haikuSerialize) && !Config.values.owners.Contains(authorId))
        {
            await TryDeleteAsync(msg, "the works of others | the blood, sweat, tears poured in them | are not yours to take");
            reasoningTraces[msg.Id] = "haiku already posted";
            return true;
        }

        // wasnt reused -- now mark it down as used
        PersistentData.values.usedHaikus.Add(haikuSerialize);
        PersistentData.WritePersistentData();

        // determine effort first because 4o is a cheaper, non-reasoning model, lol
        ChatMessage[] messages =
        [
            ChatMessage.CreateSystemMessage(Config.values.haikuEffortPrompt),
            ChatMessage.CreateUserMessage(content)
        ];
        var effortRes = await effortClint.CompleteChatAsync(messages);
        if (effortRes.Value.Role != ChatMessageRole.Assistant)
        {
            Logger.Put($"Why is the LLM giving me a non-assistant response? Why is the {effortRes.Value.Role} yapping?? Why's it saying {effortRes.Value.Content.SelectMany(c => c.Text)}");
        }

        foreach (ChatMessageContentPart part in effortRes.Value.Content)
        {
            switch (part.Kind)
            {
                case ChatMessageContentPartKind.Text:
                    if (part.Text == "No")
                    {
                        await TryDeleteAsync(msg, "you disappoint me | that was a shit haiku bro | thirty minute blast.");
                        reasoningTraces[msg.Id] = $"{msg.Content}\n...was deemed low effort";
                        try
                        {
                            if (msg.Author is DiscordMember member)
                                await member.TimeoutAsync(DateTime.Now.AddMinutes(30), "you disappoint me | that was a shit haiku bro | thirty minute blast.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Exception while timing out the author of a shit haiku ({msg.Author})", ex);
                        }
                        return true;
                    }
                    break;
                default:
                    break;
            }
        }

        // ok now do reasoning
        var options = new ResponseCreationOptions()
        {
            ReasoningOptions = new(ResponseReasoningEffortLevel.Medium) // I've seen a case of low-effort reasoning straight *forgetting* a word when counting, like bro
            {
                //ReasoningSummaryVerbosity =  ResponseReasoningSummaryVerbosity.Detailed
            }, 
            Instructions = Config.values.haikuSystemPrompt,
        };

        var response = await clint.CreateResponseAsync(string.Join("\n", lines[0..3]), options);
        
        if (response.Value.Status == ResponseStatus.Failed)
        {
            Logger.Warn($"Generating OpenAI syllable analysis failed: Code {response.Value.Error.Code} -- {response.Value.Error.Message}");
            Logger.Warn($"The above error was thrown for the following content: {content}");

            return false;
        }

        Logger.Put("Raw pipeline response: " + response.GetRawResponse().Content.ToString(), LogType.Debug);

        string reasoningTrace = "";
        int[] llmSyllableCounts = [];
        foreach (ResponseItem outItem in response.Value.OutputItems)
        {
            Logger.Put($"LLM response item: {outItem}", LogType.Debug);
            if (outItem is ReasoningResponseItem reasoning)
            {
                reasoningTrace += "\n" + string.Join('\n', reasoning.SummaryTextParts);
            }
            else if (outItem is MessageResponseItem llmMessage)
            {
                if (llmMessage.Role != MessageRole.Assistant)
                {
                    continue;
                }

                string accumulator = "";

                foreach (ResponseContentPart contentPart in llmMessage.Content)
                {
                    Logger.Put($"LLM content part: {contentPart.Kind} - {contentPart.Text}", LogType.Debug);
                    switch (contentPart.Kind)
                    {
                        case ResponseContentPartKind.OutputText:
                            accumulator += contentPart.Text;
                            reasoningTrace += contentPart.Text;
                            break;
                        case ResponseContentPartKind.Refusal:
                            Logger.Warn($"LLM refused to analyze message {msg.JumpLink}");
                            return false;
                    }
                }

                Logger.Put($"LLM says '{accumulator}' to {msg}");

                llmSyllableCounts = accumulator.Split("\n", StringSplitOptions.RemoveEmptyEntries)
                    .Select(static line => { int.TryParse(line, out int lambdaRet); return lambdaRet; })
                    .ToArray();

                break;
            }
        }

        reasoningTraces[msg.Id] = reasoningTrace;

        if (!llmSyllableCounts.SequenceEqual([5, 7, 5]))
        {
            await TryDeleteAsync(msg, $"message not haiku | lines do not make 5-7-5 | experience woe.");
            return true;
        }

        // if we got here, the message is a haiku
        return false;
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

    [Command("getReasoning")]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
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
