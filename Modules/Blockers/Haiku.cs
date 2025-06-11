using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard.Modules.Blockers;

internal class Haiku : ModuleBase
{
    public Haiku(BoneBot bot) : base(bot)
    {
        Config.ConfigChanged += () => openAiClient = null;
    }
    
    OpenAIClient? openAiClient;
    Dictionary<ulong, string> reasoningTraces = new Dictionary<ulong, string>();

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

        if (msg.Content.Length == 0) // empty message/only attachments
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

        // do quick checks for haiku
        if (msg.Content.Split("\n", StringSplitOptions.RemoveEmptyEntries).Length < 3)
        {
            await TryDeleteAsync(msg, "message not haiku | ancient japan memory | experience woe.");
            return true;
        }

        var clint = openAiClient.GetOpenAIResponseClient(Config.values.haikuAiModel);
        //ChatMessage[] messages =
        //[
        //    ChatMessage.CreateSystemMessage(Config.values.haikuSystemPrompt),
        //    ChatMessage.CreateUserMessage(msg.Content)
        //];

        var options = new ResponseCreationOptions()
        {
            ReasoningOptions = new(ResponseReasoningEffortLevel.Low) // rent due
            {
                ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Concise 
            }, 
            Instructions = Config.values.haikuSystemPrompt,
        };
        var response = await clint.CreateResponseAsync(msg.Content, options);
        
        if (response.Value.Status == ResponseStatus.Failed)
        {
            Logger.Warn($"Generating OpenAI syllable analysis failed: Code {response.Value.Error.Code} -- {response.Value.Error.Message}");
            Logger.Warn($"The above error was thrown for the following content: {msg.Content}");

            return false;
        }

        string reasoningTrace = "";
        int[] llmSyllableCounts = [];
        foreach (ResponseItem outItem in response.Value.OutputItems)
        {
            if (outItem is ReasoningResponseItem reasoning)
            {
                reasoningTrace += string.Join('\n', reasoning.SummaryTextParts);
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
                    switch (contentPart.Kind)
                    {
                        case ResponseContentPartKind.OutputText:
                            accumulator += contentPart.Text;
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

        int onHaikuLine = 0; //zero-indexed, so 0 is first line
        for (int i = 0; i < llmSyllableCounts.Length; i++)
        {
            int neededSyllables = onHaikuLine switch
            {
                0 => 5,
                1 => 7,
                2 => 5,
                _ => -1
            };
            
            if (llmSyllableCounts[i] != neededSyllables)
            {
                await TryDeleteAsync(msg, $"message not haiku | line {i + 1} has {llmSyllableCounts[i]} syllables | experience woe.");
                return true;
            }

            onHaikuLine++;
            if (onHaikuLine > 2) // we only care about the first 3 lines
                break;
        }

        // if we got here, the message is a haiku
        return false;
    }
}
