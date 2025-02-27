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
using DSharpPlus;

namespace BoneBoard.Modules;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("confess")]
internal class Confessional : ModuleBase
{
    private record RewriteInfo(SlashCommandContext ctx, string originalConfession, string rewrittenConfession, DateTime lastRewrittenAt, DateTime nextRewriteAllowedAt);

    static readonly string[] confessionalCsvHeaders = ["WrittenBy", "Timestamp", "Confession", /*"Context"*/];

    const string REWRITE_INTERACTION_FORMAT = "bb.cr.{0}.{1}"; // 0: origin interaction ID -> keyed into info dictionary | 1: action (accept, reject, send original)
    const string REWRITE_ACCEPT = "a";
    const string REWRITE_TRYAGAIN = "r";
    const string REWRITE_SENDORIGINAL = "o";


    Dictionary<ulong, RewriteInfo> rewriteInfos = new();

    private static TimeSpan RewriteCooldown => TimeSpan.FromSeconds(10);
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
        bot.clientBuilder.ConfigureEventHandlers(x => x.HandleComponentInteractionCreated(DispatchInteractions));

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

    private async Task DispatchInteractions(DiscordClient sender, ComponentInteractionCreatedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Interaction.Data.CustomId))
            return;

        if (!args.Interaction.Data.CustomId.StartsWith("bb"))
            return;

        string[] splits = args.Interaction.Data.CustomId.Split('.');
        switch (splits[1])
        {
            case "cr":
                await HandleRewriteInteraction(args.Interaction, args.Message, splits);
                break;
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
            MaxOutputTokenCount = 256,
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

            chatResponse = mainRes.Value.Content[0].Text;
            if (string.IsNullOrEmpty(chatResponse))
                continue;


            Logger.Put($"Got AI response for round {i + 1} - {chatResponse}");

            var altPrompt = new ChatMessage[]
            {
                new SystemChatMessage(sanitySysPrompt),
                new UserChatMessage(chatResponse)
            };

            var sanityRes = await sanityClient.CompleteChatAsync(altPrompt);
            string sanityResponse = sanityRes.Value.Content[0].Text;
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

        bool isAi = PersistentData.values.aiConfessionals.Contains(msg.Id);

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
        [Parameter("message")] string text,
        [Parameter("rewrite"), Description("Have an LLM to rewrite your confession (with your confirmation), so it's less identifiable as you")] bool rewriteAi = false
        //[Parameter("delay"), Description("")] double delaySend = 0.0 DONT ADD THIS, it makes tracking cooldowns harder!!!! (as in, there are more design decisions to be made and i dont wanna)
       )
    {
        if (ctx.Member is null)
        {
            await ctx.RespondAsync("😂👎", true);
            return;
        }

        if (rewriteAi)
        {
            await BeginConfessionalRewriting(ctx, text);
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

    private static async Task BeginConfessionalRewriting(SlashCommandContext ctx, string confessionToRewrite)
    {
        Confessional confessional = BoneBot.Bots[ctx.Client].confessions;
        DiscordMember member = ctx.Member!;

        if (Config.values.confessionalRestrictions.HasFlag(ConfessionalRequirements.ROLE))
        {
            if (member.Roles.All(r => r.Id != Config.values.confessionalRole))
            {
                await ctx.FollowupAsync("You don't have the required role to confess, so your confession can't be rewritten.", true);
                return;
            }
        }

        if (Config.values.confessionalRestrictions.HasFlag(ConfessionalRequirements.COOLDOWN))
        {
            if (confessional.confessions.TryGetValue(member, out DateTime lastConfession) && (DateTime.Now - lastConfession).TotalHours < 6)
            {
                await ctx.FollowupAsync("You've already confessed in the last 6 hours, so your confession can't be rewritten.", true);
                return;
            }
        }
        
        if (confessional.openAiClient is null)
        {
            await ctx.FollowupAsync("AI rewriting is currently unavailable, so your confession can't be rewritten.", true);
            return;
        }

        await ctx.RespondAsync("Rewriting your confession...", true);
        await RewriteConfession(ctx, confessionToRewrite, confessional);
    }

    private static async Task RewriteConfession(SlashCommandContext ctx, string confessionToRewrite, Confessional confessional)
    {
        ChatClient clint = confessional.openAiClient!.GetChatClient(Config.values.openAiConfessionRewriteModel);
        BoneBot.Bots[ctx.Client].confessions.rewriteInfos[ctx.Interaction.Id] = new(ctx, confessionToRewrite, "I'm impatient and can't wait for an LLM to crunch its numbers!", DateTime.Now, DateTime.Now + TimeSpan.FromDays(1));

        string rewritten;
        try
        {
            var res = await clint.CompleteChatAsync(
            [
                new SystemChatMessage(Config.values.openAiConfessionRewritePrompt),
                new UserChatMessage(confessionToRewrite)
            ], new ChatCompletionOptions() { MaxOutputTokenCount = 256 });
            rewritten = res.Value.Content[res.Value.Content.Count - 1].Text;
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while trying to rewrite a confession! ", ex);
            await ctx.EditResponseAsync("An error occurred while trying to rewrite your confession. Please try again.");
            return;
        }

        DateTime nextRewriteAllowedAt = DateTime.Now + RewriteCooldown + TimeSpan.FromMilliseconds(confessionToRewrite.Length * 2 + rewritten.Length * 4);
        confessional.rewriteInfos[ctx.Interaction.Id] = new(ctx, confessionToRewrite, rewritten, DateTime.Now, nextRewriteAllowedAt);

        DiscordButtonComponent accept = new(DiscordButtonStyle.Primary, string.Format(REWRITE_INTERACTION_FORMAT, ctx.Interaction.Id, REWRITE_ACCEPT), "Accept");
        DiscordButtonComponent tryagain = new(DiscordButtonStyle.Secondary, string.Format(REWRITE_INTERACTION_FORMAT, ctx.Interaction.Id, REWRITE_TRYAGAIN), "Rewrite again");
        DiscordButtonComponent sendOriginal = new(DiscordButtonStyle.Secondary, string.Format(REWRITE_INTERACTION_FORMAT, ctx.Interaction.Id, REWRITE_SENDORIGINAL), "Send original");
        string originalDisplay = Logger.EnsureShorterThan(confessionToRewrite.Replace("\n", " // ").Replace('`', '\''), 700);
        string rewrittenDisplay = Logger.EnsureShorterThan(rewritten.Replace("\n", " // ").Replace('`', '\''), 800);
        var dmb = new DiscordMessageBuilder().WithContent($"Rewrote: `{originalDisplay}`\nInto: `{rewrittenDisplay}`\nNext rewrite allowed {Formatter.Timestamp(nextRewriteAllowedAt, TimestampFormat.RelativeTime)}" +
            $"{(rewritten.Contains('\n') ? "\n-# Line breaks have been replaced with \" // \" here for the sake of visibility. They will appear normally if this rewrite is chosen." : "")}" +
            $"{(rewritten.Contains('`') ? "\n-# Backticks have been replaced with single quotes (`'`) here for the sake of formatting. They will appear normally if this rewrite is chosen." : "")}")
            .AddComponents(accept, tryagain, sendOriginal);

        await ctx.EditResponseAsync(dmb);
    }

    private async Task HandleRewriteInteraction(DiscordInteraction interaction, DiscordMessage message, string[] splits)
    {
        var res = new DiscordInteractionResponseBuilder().AsEphemeral(true);
        if (!ulong.TryParse(splits[2], out ulong originInteractionId))
        {
            await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent($"Unable to parse `{splits[2]}` into an interaction ID."));
            return;
        }

        if (!rewriteInfos.TryGetValue(originInteractionId, out RewriteInfo? info))
        {
            await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent($"Unable to find `{splits[2]}` in the interaction info dictionary."));
            return;
        }

        if (interaction.User != info.ctx.User)
        {
            await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent($"You're not the originator of this interaction."));
            return;
        }

        switch (splits[3])
        {
            case REWRITE_ACCEPT:
                await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent("👍"));
                var response = await SendConfessional(info.ctx.Member!, info.rewrittenConfession);
                await info.ctx.EditResponseAsync(response is not null ? "Sent!" : "It seems there was an issue sending your confession. Consider telling the bot operator to check the log.");
                break;
            case REWRITE_TRYAGAIN:
                if (info.nextRewriteAllowedAt > DateTime.Now)
                {
                    await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent($"You can't request a rewrite again yet. Try again {Formatter.Timestamp(info.nextRewriteAllowedAt, TimestampFormat.RelativeTime)}"));
                    return;
                }

                await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent("👍"));
                await info.ctx.EditResponseAsync("Rewriting your confession...");
                await RewriteConfession(info.ctx, info.originalConfession, this);
                break;
            case REWRITE_SENDORIGINAL:
                await interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, res.WithContent("👍"));
                var responseOriginal = await SendConfessional(info.ctx.Member!, info.originalConfession);
                await info.ctx.EditResponseAsync(responseOriginal is not null ? "Sent!" : "It seems there was an issue sending your confession. Consider telling the bot operator to check the log.");
                break;
        }
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
