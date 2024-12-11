using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Commands;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using OpenAI;
using Csv;
using OpenAI.Chat;
using System.ComponentModel;
using DSharpPlus.Commands.ContextChecks;

namespace BoneBoard.Modules;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("confess")]
internal class Confessional : ModuleBase
{
    static readonly string[] confessionalCsvHeaders = ["WrittenBy", "Timestamp", "Confession", /*"Context"*/];

    DiscordChannel? _confessionalChannel;
    DiscordChannel? ConfessionalChannel
    {
        get
        {
            if (_confessionalChannel is null && Config.values.confessionalChannel != 0)
            {
                _confessionalChannel = bot.allChannels.Values.SelectMany(chs => chs).FirstOrDefault(ch => ch.Id == Config.values.confessionalChannel);
            }

            return _confessionalChannel;
        }
    }

    // todo: propertyize
    Dictionary<DiscordMember, DateTime> confessions = new();
    List<DiscordMessage> confessionsByAi = new();
    OpenAIClient? openAiClient;
    static TextWriter? csvConfessional;
    Task? occasionalConfessional;

    public Confessional(BoneBot bot) : base(bot)
    {
        Config.ConfigChanged += () => _confessionalChannel = null;

        try
        {
            if (csvConfessional is null)
            {
                bool csvExistedBeforehand = File.Exists(Config.values.confessionCsvPath);
                csvConfessional = File.AppendText(Config.values.confessionCsvPath);
                if (!csvExistedBeforehand)
                {
                    CsvWriter.Write(csvConfessional, confessionalCsvHeaders, Enumerable.Empty<string[]>(), ',', false);
                    csvConfessional.Flush();
                }
            }
        }
        catch { }


        if (!string.IsNullOrEmpty(Config.values.openAiToken))
        {
            openAiClient = new(new System.ClientModel.ApiKeyCredential(Config.values.openAiToken));
        }

    }

    protected override async Task InitOneShot(GuildDownloadCompletedEventArgs args)
    {
        try
        {
            if (occasionalConfessional is not null)
                occasionalConfessional = OccasionalAiConfessional();
        }
        catch (Exception ex)
        {
            Logger.Error("Exception in periodic-runner! " + ex);
        }

        if (ConfessionalChannel is not null)
        {
            int restartedCount = 0;
            foreach (var kvp in PersistentData.values.confessionalRevealTime)
            {
                DiscordMessage? monoSodiumGlutemate = await TryFetchMessage(ConfessionalChannel, kvp.Key); // get it? msg? laugh. haw haw haw.
                if (monoSodiumGlutemate is null)
                    continue;

                HandleConfessionVotingFor(monoSodiumGlutemate, kvp.Value);
                restartedCount++;
            }
            Logger.Put($"Restarted confessional vote waiting for {restartedCount} confessions.");
        }

    }

    async Task OccasionalAiConfessional()
    {
        while (true)
        {
            try
            {
                int dist = Config.values.confessionalCooldownHoursMax - Config.values.confessionalCooldownHoursMin;
                double hours = Config.values.confessionalCooldownHoursMin + dist * Random.Shared.NextDouble();
                TimeSpan wait = TimeSpan.FromHours(hours);
                await Task.Delay(wait);

                Logger.Put("Time for an AI confession!");
                await SendAiConfessional();
            }
            catch (Exception ex)
            {
                Logger.Error("Exception in occasional AI confessional! " + ex);
            }
        }
    }

    internal async Task SendAiConfessional()
    {
        if (ConfessionalChannel is null)
            return;

        DiscordMember botInServer;
        try
        {
            botInServer = await ConfessionalChannel.Guild.GetMemberAsync(bot.client.CurrentUser.Id);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Bot not found in server/other exception while trying to get member object for {bot.client.CurrentUser} in {ConfessionalChannel?.Guild}! " + ex);
            return;
        }

        if (openAiClient is null)
            return;

        string mainModel = Config.values.openAiConfessionalModel;
        string mainSysPrompt = Config.values.openAiConfessionalSystemPrompt;
        string mainUserPrompt = Config.values.openAiConfessionalPrompt;
        string sanityModel = Config.values.openAiSanityModel;
        string sanitySysPrompt = Config.values.openAiConfessionalSanityPrompt;
        string sanityUserPrompt = Config.values.openAiConfessionalSanityPrompt;
        if (string.IsNullOrEmpty(mainModel) || string.IsNullOrEmpty(mainSysPrompt) || string.IsNullOrEmpty(mainUserPrompt) || string.IsNullOrEmpty(sanityModel) || string.IsNullOrEmpty(sanitySysPrompt) || string.IsNullOrEmpty(sanityUserPrompt))
            return;

        Logger.Put("Now generating an AI confession!");

        var mainClient = openAiClient.GetChatClient(Config.values.openAiConfessionalModel);
        var sanityClient = openAiClient.GetChatClient(Config.values.openAiSanityModel);
        ChatCompletionOptions mainOptions = new()
        {
            MaxTokens = 256,
            Temperature = 0.5f,
        };

        string chatResponse = "j";
        bool sanitySignoff = false;
        for (int i = 0; i < 25; i++)
        {
            var mainPrompt = new ChatMessage[]
            {
                new SystemChatMessage(mainSysPrompt),
                new UserChatMessage(mainUserPrompt)
            };
            var mainRes = await mainClient.CompleteChatAsync(mainPrompt, mainOptions);

            chatResponse = mainRes.Value.ToString();
            if (string.IsNullOrEmpty(chatResponse))
                continue;


            Logger.Put($"Got AI response for round {i + 1} - {chatResponse}");

            var altPrompt = new ChatMessage[]
            {
                new SystemChatMessage(sanitySysPrompt),
                new UserChatMessage(chatResponse)
            };

            var sanityRes = await sanityClient.CompleteChatAsync(altPrompt);
            string sanityResponse = sanityRes.Value.ToString();
            Logger.Put($"Got AI sanitychecker response for round {i + 1} - {sanityResponse}");


            if (string.IsNullOrEmpty(sanityResponse))
                continue;

            int pastConfessionCount = PersistentData.values.previousAiConfessions.Count(prevConfession => chatResponse.Equals(prevConfession, StringComparison.InvariantCultureIgnoreCase));
            if (Random.Shared.Next(pastConfessionCount) != 0)
            {
                Logger.Put("AI confession was a repeat, trying again...");
                continue;
            }

            if (sanityResponse.Contains(Config.values.sanityAffirmative, StringComparison.CurrentCultureIgnoreCase))
            {
                Logger.Put("Sanity check passed, posting confession!");
                sanitySignoff = true;
                break;
            }
        }

        if (!sanitySignoff)
            return;

        PersistentData.values.previousAiConfessions.Add(chatResponse);
        PersistentData.WritePersistentData();
        DiscordMessage? message = await SendConfessional(botInServer, chatResponse);
        if (message is not null)
            confessionsByAi.Add(message);
    }

    public async Task<DiscordMessage?> SendConfessional(DiscordMember member, string text)
    {
        static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789qwertyuiopasdfghjklzxcvbnm";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
        }

        if (ConfessionalChannel is null)
            return null;


        if (!bot.IsMe(member))
        {
            if (Config.values.confessionalRestrictions.HasFlag(ConfessionalRequirements.ROLE))
            {
                if (member.Roles.All(r => r.Id != Config.values.confessionalRole))
                    return null;
            }

            if (Config.values.confessionalRestrictions.HasFlag(ConfessionalRequirements.COOLDOWN))
            {
                if (confessions.TryGetValue(member, out DateTime lastConfession) && (DateTime.Now - lastConfession).TotalHours < 6)
                    return null;
                confessions[member] = DateTime.Now;
            }
        }

        string[] slurPatterns =
        {
            "*fag*",
            "niga",
            "niger",
            "*nigg*",
        };

        foreach (string pattern in slurPatterns)
        {
            if (Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeString(text, pattern, Microsoft.VisualBasic.CompareMethod.Text))
            {
                Logger.Put($"The confession '{text}' failed against the pattern {pattern} and is rejected.");
                return null;
            }
        }

        Logger.Put($"Going to send a msg in the confessional channel #{ConfessionalChannel.Name} - {text}");

        var deb = new DiscordEmbedBuilder()
            .WithTitle($"A {member.Guild.Name} confession...")
            .WithDescription(text);

        var dmb = new DiscordMessageBuilder()
            .WithAllowedMentions(Enumerable.Empty<IMention>())
            .AddEmbed(deb);

        // confessions usually only reference themselves and not outside messages
        //string context = "None";
        //try
        //{
        //    if (confessionalChannel.LastMessageId.HasValue)
        //    {
        //        await foreach (DiscordMessage recentMessage in confessionalChannel.GetMessagesBeforeAsync(confessionalChannel.LastMessageId.Value, 3))
        //        {

        //        }

        //    }
        //}
        //catch { }

        string[] values =
        {
            bot.IsMe(member) ? "AI" : "Human",
            DateTime.Now.ToString(),
            text,
            //context
        };

        try
        {
            if (csvConfessional is not null)
            {
                CsvWriter.Write(csvConfessional, confessionalCsvHeaders, [values], skipHeaderRow: true);
                csvConfessional.Flush();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to write to confessional CSV! ", ex);
        }

        byte[] textBytes = Encoding.UTF8.GetBytes($"{RandomString(Random.Shared.Next(8))} {member.Username} - id {member.Id} {RandomString(Random.Shared.Next(8))}");
        string authorString = Convert.ToBase64String(textBytes);
        try
        {
            DiscordMessage msg = await ConfessionalChannel.SendMessageAsync(dmb);

            if (bot.IsMe(member))
            {
                Logger.Put($"Confession sent by an AI. Be proud of your little bot. It's trying.", LogType.Debug);
                PersistentData.values.aiConfessionals.Add(msg.Id);
            }
            else
                Logger.Put($"Confession sent was from the following B64-encoded user: {authorString}", LogType.Debug);



            HandleConfessionVotingFor(msg, DateTime.Now.AddHours(Config.values.confessionalAiVotingPeriodHours));
            return msg;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send a confessional by {authorString} - {ex}");
            return null;
        }

    }

    async void HandleConfessionVotingFor(DiscordMessage msg, DateTime revealAt)
    {
        if (openAiClient is null)
            return;

        DiscordEmoji? botEmoji, humanEmoji;
        try
        {
            if (!DiscordEmoji.TryFromUnicode(Config.values.aiConfessionIsBotEmoji, out botEmoji))
                if (ulong.TryParse(Config.values.aiConfessionIsBotEmoji, out ulong botEmojiId))
                    DiscordEmoji.TryFromGuildEmote(bot.client, botEmojiId, out botEmoji);

            if (!DiscordEmoji.TryFromUnicode(Config.values.aiConfessionIsHumanEmoji, out humanEmoji))
                if (ulong.TryParse(Config.values.aiConfessionIsHumanEmoji, out ulong humanEmojiId))
                    DiscordEmoji.TryFromGuildEmote(bot.client, humanEmojiId, out botEmoji);

            if (botEmoji is null)
            {
                Logger.Put($"Failed to get DiscordEmoji from '{Config.values.aiConfessionIsBotEmoji}' for confessional AI-or-not reactions! Bailing!");
                return;
            }

            if (humanEmoji is null)
            {
                Logger.Put($"Failed to get DiscordEmoji from '{Config.values.aiConfessionIsHumanEmoji}' for confessional AI-or-not reactions! Bailing!");
                return;
            }


            await BoneBot.TryReact(msg, humanEmoji, botEmoji);
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while adding emoji for confessional AI-or-not reactions!", ex);
            return;
        }

        PersistentData.values.confessionalRevealTime[msg.Id] = revealAt;
        
        if (revealAt > DateTime.Now) // task.delay throws if given negative values
            await Task.Delay(revealAt - DateTime.Now);

        PersistentData.values.confessionalRevealTime.Remove(msg.Id);

        bool isAi = confessionsByAi.Contains(msg);

        try
        {
            DiscordEmbedBuilder deb = new(msg.Embeds[0]);
            deb.WithFooter(isAi ? "From an AI" : "From a human");
            await msg.ModifyAsync(deb.Build());
        }
        catch (Exception ex)
        {
            Logger.Warn($"Exception when editing confession message ({msg}) to reveal ground-truth!", ex);
        }

        try
        {
            DiscordEmoji correctEmoji = isAi ? botEmoji : humanEmoji;
            DiscordEmoji incorrectEmoji = isAi ? humanEmoji : botEmoji;
            var correctUsers = msg.GetReactionsAsync(correctEmoji).ToBlockingEnumerable();
            var incorrectUsers = msg.GetReactionsAsync(incorrectEmoji).ToBlockingEnumerable();

            foreach (DiscordUser user in correctUsers)
            {
                if (incorrectUsers.Contains(user))
                    bot.casino.GivePoints(user, -2000);
                else
                    bot.casino.GivePoints(user, isAi ? 1000 : 100);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Exception giving out points for confessional AI voting! ", ex);
        }
    }

    [Command("send")]
    [Description("confess your sins my child")]
    public static async Task SendConfessional(
        SlashCommandContext ctx,
        [Parameter("message")] string text
       )
    {
        if (ctx.Member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        DiscordMessage? msg = await BoneBot.Bots[ctx.Client].confessions.SendConfessional(ctx.Member, text);

        if (msg is null)
        {
            await ctx.RespondAsync("There was either an error or you tried sending a message before your 6 hours are up. Or you dont have the required role. LOL.", true);
            return;
        }

        await ctx.RespondAsync("Confessed successfully. May the holy spirit cleanse you of your sins.", true);
    }

    [Command("sendAiConfession"), Description("Sends an AI confession.")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MODERATOR_PERMS)]
    public static async Task TestSendAiConfession(SlashCommandContext ctx)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        Logger.Put($"Prompting AI confession at the request of {ctx.User}.");
        await ctx.DeferResponseAsync(true);

        await BoneBot.Bots[ctx.Client].confessions.SendAiConfessional();

        await ctx.FollowupAsync("Attempted AI confession.");
    }

    [Command("reveal")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MODERATOR_PERMS)]
    [SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [DirectMessageUsage(DirectMessageUsage.DenyDMs)]
    public static async Task CheckWasFromAi(SlashCommandContext ctx, DiscordMessage msg)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        
        if (PersistentData.values.aiConfessionals.Contains(msg.Id))
            await ctx.RespondAsync("This message was from an AI.", true);
        else
            await ctx.RespondAsync("This message was from a human.", true);
    }

}
