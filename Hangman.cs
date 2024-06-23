using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("hang")]
internal partial class Hangman
{
    const string VOWELS = "aeiou";
    BoneBot bot;
    string[] possibleWords = Array.Empty<string>();
    DiscordMessage? hangmanMessage;

    DiscordEmoji thumbsDownEmoji = DiscordEmoji.FromUnicode("👎");
    DiscordEmoji[] joyEmojis =
    {
        DiscordEmoji.FromUnicode("😂"),
        DiscordEmoji.FromUnicode("😹"),
    };
    DiscordEmoji[] xEmojis =
    {
        DiscordEmoji.FromUnicode("❎"),
        DiscordEmoji.FromUnicode("✖️"),
        DiscordEmoji.FromUnicode("❌"),
    };
    DiscordEmoji[] checkEmojis =
    {
        DiscordEmoji.FromUnicode("☑️"),
        DiscordEmoji.FromUnicode("✔️"),
        DiscordEmoji.FromUnicode("✅"),
    };

    public Hangman(BoneBot bot)
    {
        this.bot = bot;
        bot.ConfigureEvents(e =>
        {
            e.HandleMessageCreated(HandleHangman);
        });
    }

    private async Task HandleHangman(DiscordClient sender, MessageCreatedEventArgs args)
    {
        if (hangmanMessage is null || string.IsNullOrWhiteSpace(PersistentData.values.currHangmanWord))
            return;

        if (args.Channel != hangmanMessage.Channel || args.Message.ReferencedMessage != hangmanMessage)
            return;

        if (!PersistentData.values.currHangmanWord.Contains(' ') && args.Message.Content.Contains(' '))
            return; // this means the message was probably not a guess, and was more likely just a comment on the game

        Logger.Put("hangman attempt: " + args.Message.Content);

        if (PersistentData.values.currHangmanState == hangmanStates.Length)
        {
            await BoneBot.TryReact(args.Message, xEmojis.Random());
            return;
        }

        string content = args.Message.Content.ToLower();

        if (content.Length == 1)
        {
            if (PersistentData.values.currHangmanGuessed.Contains(content))
            {
                await BoneBot.TryReact(args.Message, joyEmojis.Random(), thumbsDownEmoji);
                return;
            }

            if (PersistentData.values.currHangmanWord.Contains(content[0]))
            {
                PersistentData.values.currHangmanGuessed += content;
                await BoneBot.TryReact(args.Message, checkEmojis.Random());
                int pointsGiven = VOWELS.Contains(char.ToLower(content[0])) ? 100 : 200;
                if (args.Author is DiscordMember member)
                    bot.casino.GivePoints(member, pointsGiven);
                await UpdateHangmanDisplay();
            }
            else if (char.IsLetter(content[0]))
            {
                await BoneBot.TryReact(args.Message, xEmojis.Random());
                PersistentData.values.currHangmanState++;
                PersistentData.values.currHangmanGuessed += content;
                PersistentData.WritePersistentData();
                await UpdateHangmanDisplay();
            }

        }
        else if (content == PersistentData.values.currHangmanWord)
        {
            PersistentData.values.currHangmanGuessed = new string(PersistentData.values.currHangmanWord.Distinct().ToArray());
            //PersistentData.WritePersistentData(); GivePoints calls WritePersistentData
            if (args.Author is DiscordMember member)
                bot.casino.GivePoints(member, 500);

            await BoneBot.TryReact(args.Message, checkEmojis.Random());
            await UpdateHangmanDisplay();
        }
        else if (content.Length == PersistentData.values.currHangmanWord.Length)
        {
            await BoneBot.TryReact(args.Message, xEmojis.Random());
        }
    }

    public async Task Init()
    {
        await FetchMessage();

        await GetWords();

        Logger.Put("Hangman initialized with...");
        Logger.Put($" - {possibleWords.Length} words");
        Logger.Put($" - {(hangmanMessage is null ? "No" : "A")} message");
    }

    internal async Task FetchMessage()
    {
        if (!string.IsNullOrEmpty(Config.values.hangmanMessageLink))
            hangmanMessage = await bot.GetMessageFromLink(Config.values.hangmanMessageLink);
    }

    private async Task GetWords()
    {
        List<string> words = new List<string>(Config.values.hangmanWords.Select(w => w.ToLower()));

        if (string.IsNullOrEmpty(Config.values.hangmanWordSource))
        {
            possibleWords = words.ToArray();
            return;
        }

        try
        {
            HttpClient client = new HttpClient();
            string str = await client.GetStringAsync(Config.values.hangmanWordSource);
            var strings = str.Split(Config.values.wordSourceSeparator).Select(str => str.Trim().ToLower());
            words.AddRange(strings);
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while fetching and/or separating words", ex);
        }

        possibleWords = words.ToArray();
    }

    private async Task UpdateHangmanDisplay()
    {
        if (hangmanMessage is null || string.IsNullOrWhiteSpace(Config.values.hangmanMessageFormat))
            return;

        string word = PersistentData.values.currHangmanWord;
        string guessed = PersistentData.values.currHangmanGuessed;

        StringBuilder display = new();
        foreach (char letter in word)
        {
            if (char.IsWhiteSpace(letter))
                display.Append(letter);
            else if (guessed.Contains(letter))
                display.Append(letter);
            else
                display.Append(@" \_ ");
        }

        int state = Math.Clamp(PersistentData.values.currHangmanState, 0, hangmanStates.Length - 1);

        string stateStr = Formatter.BlockCode(hangmanStates[state]);

        string msgContent = string.Format(Config.values.hangmanMessageFormat, display.ToString(), stateStr, guessed.Length == 0 ? "none" : guessed);

        if (!Config.values.hangmanMessageFormat.Contains("{0}"))
            msgContent += "\nalso it looks like whoever set this message forgot a place for the word. here ya go: " + display;

        if (state == hangmanStates.Length - 1)
            msgContent += "\n\nyou all lost! the word was: " + word;

        try
        { 
            await hangmanMessage.ModifyAsync(msgContent);
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while updating hangman display message", ex);
        }

        if (display.ToString() == word)
        {
            Logger.Put("Starting new hangman game because it was won: " + word);
            await NewHangmanMessage();
            await StartNewGame();
        }
        else if (state == hangmanStates.Length - 1)
        {
            Logger.Put("Starting new hangman game because it was lost: " + word);
            await NewHangmanMessage();
            await StartNewGame();
        }
    }

    public async Task NewHangmanMessage()
    {
        if (hangmanMessage is null || string.IsNullOrWhiteSpace(Config.values.hangmanMessageFormat))
            return;


        DiscordMessage msg = await hangmanMessage.Channel.SendMessageAsync(Config.values.hangmanMessageFormat.Replace("{0}", "(word)").Replace("{1}", "(hangman)").Replace("{2}", "(guessed letters)"));

        Config.values.hangmanMessageLink = msg.JumpLink.OriginalString;
        Config.WriteConfig();
        await FetchMessage();
    }

    public async Task StartNewGame(string? word = null)
    {
        if (hangmanMessage is null || string.IsNullOrWhiteSpace(Config.values.hangmanMessageFormat))
            return;

        PersistentData.values.currHangmanWord = string.IsNullOrEmpty(word) ? possibleWords.Random() : word;
        PersistentData.values.currHangmanGuessed = "";
        PersistentData.values.currHangmanState = 0;
        PersistentData.WritePersistentData();

        Logger.Put("Starting new hangman game. Spoilers, but the word is: " + PersistentData.values.currHangmanWord);

        await UpdateHangmanDisplay();
    }

    [Command("createMsg"), Description("Creates the message that will be used for hangman. \\n will be replaced w newline")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MDOERATOR_PERMS)]
    public static async Task CreateHangmanMsg(SlashCommandContext ctx,
        [Parameter("msgContent"), Description("{0} -> word, {1} -> hangman, {2} -> guesses")] string content)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        content = content.Replace("\\n", "\n");

        DiscordMessage msg = await ctx.Channel.SendMessageAsync(content.Replace("{0}", "(word)").Replace("{1}", "(hangman)").Replace("{2}", "(guessed letters)"));

        Config.values.hangmanMessageLink = msg.JumpLink.OriginalString;
        Config.values.hangmanMessageFormat = content;
        Config.WriteConfig();
        await BoneBot.Bots[ctx.Client].hangman.FetchMessage();

        await ctx.RespondAsync("Created hangman message!", true);
    }

    [Command("start"), Description("Starts a new hangman game!")]
    [RequirePermissions(DiscordPermissions.None, SlashCommands.MDOERATOR_PERMS)]
    public static async Task StartHangman(SlashCommandContext ctx,
        [Parameter("word"), Description("Force the word to be one of your choosing")] string word = "")
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        if (string.IsNullOrWhiteSpace(Config.values.hangmanMessageFormat))
        {
            await ctx.RespondAsync("You need to create a hangman message first!", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.values.hangmanMessageLink))
        {
            await ctx.RespondAsync("You need to create a hangman message first!", true);
            return;
        }

        await BoneBot.Bots[ctx.Client].hangman.StartNewGame(word);

        await ctx.RespondAsync($"Started! Check [the hangman message](<{Config.values.hangmanMessageLink}>)!", true);
    }
}
