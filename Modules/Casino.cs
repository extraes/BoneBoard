using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Commands.Processors.SlashCommands;
using System.ComponentModel;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Trees.Metadata;

namespace BoneBoard.Modules;

[AllowedProcessors(typeof(SlashCommandProcessor))]
[Command("casino")]
internal partial class Casino : ModuleBase
{
    // bb.bj.{2: followup id}.{3: user id}.{4: dealer hand}.{5: player hand}.{6: wager}.{7: action} (.Split idx's)

    string[] slotMachineEmojis = // courtesy of chatgpt
    {
        "🍒", // Cherry
        "🍋", // Lemon
        "🍇", // Grapes
        "🍊", // Orange
        "🍉", // Watermelon
        "🍓", // Strawberry
        "🔔", // Bell
        "💰", // Money Bag
        "💎", // Gem
        "🎰"  // Slot Machine
    };

    static TimeSpan pointsTimeout = TimeSpan.FromMinutes(15);
    Dictionary<DiscordUser, DateTime> lastTimePointsGotten = new();

    public Casino(BoneBot bot) : base(bot)
    {
        bot.ConfigureEvents(e =>
        {
            e.HandleComponentInteractionCreated(DispatchInteractions);
        });
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
            case "bj":
                await HandleBlackjackInteraction(args.Interaction, args.Message, splits);
                break;
        }
    }

    protected override async Task MessageCreated(DiscordClient sender, MessageCreatedEventArgs args)
    {
        if (args.Author is not DiscordMember member)
            return;

        if (member.Roles.All(r => !Config.values.casinoRoleIds.Contains(r.Id)))
            return;

        if (lastTimePointsGotten.TryGetValue(member, out var lastTime) && DateTime.Now - lastTime < pointsTimeout)
            return;

        lastTimePointsGotten[member] = DateTime.Now;

        int points = Random.Shared.Next(1, 5) * 100;
        GivePoints(member, points);
        Logger.Put($"{member} got {points} for a total of {PersistentData.values.casinoPoints[member.Id]} pts", LogType.Debug);
    }

    internal void GivePoints(ulong userId, int points)
    {
        if (PersistentData.values.casinoPoints.TryGetValue(userId, out var currentPoints))
            PersistentData.values.casinoPoints[userId] = Math.Max(0, currentPoints + points);
        else
            PersistentData.values.casinoPoints[userId] = Math.Max(0, points);

        PersistentData.WritePersistentData();
    }

    internal void GivePoints(DiscordUser member, int points) => GivePoints(member.Id, points);

    internal async Task CheckPoints(SlashCommandContext ctx, bool ephemeral = true)
    {
        if (!PersistentData.values.casinoPoints.TryGetValue(ctx.User.Id, out int points) || points == 0)
        {
            //await ctx.RespondAsync("You don't have any points to gamble!");
            await ctx.RespondAsync("https://tenor.com/view/broke-no-cash-gif-25565154", ephemeral);
            return;
        }

        await ctx.RespondAsync($"{(ephemeral || ctx.Member is null ? "You" : ctx.Member.DisplayName)} {(ephemeral ? "have" : "has")} {points} point{(points == 1 ? "" : "s")}!", ephemeral);
    }

    internal async Task GambleSlots(SlashCommandContext ctx, int amountGambled, bool ephemeral = true)
    {
        try
        {
            if (!PersistentData.values.casinoPoints.TryGetValue(ctx.User.Id, out int hasPoints) || hasPoints < amountGambled)
            {
                await ctx.RespondAsync($"You don't have {amountGambled} points to gamble! You only have {hasPoints} points!", ephemeral);
                return;
            }
            else if (amountGambled < 100)
            {
                await ctx.RespondAsync("lol. broke bitch. try over a hundred next time.", ephemeral);
                return;
            }

            await ctx.DeferResponseAsync();

            int winAmount = amountGambled * 20;

            DiscordMessage followup = await ctx.FollowupAsync("Rolling...", ephemeral);

            var builder = new DiscordWebhookBuilder();

            int spinCount = Random.Shared.Next(2, 9);

            for (int i = 0; i < spinCount; i++)
            {
                await Task.Delay(1000);

                string emojis = slotMachineEmojis.Random() + " " + slotMachineEmojis.Random() + " " + slotMachineEmojis.Random();
                builder.WithContent("Rolling...\n" + emojis);
                
                if (amountGambled > PersistentData.values.casinoPoints[ctx.User.Id])
                {
                    builder.WithContent("https://tenor.com/view/money-wallet-broke-gif-7855913");
                    await ctx.EditFollowupAsync(followup.Id, builder);
                    return;
                }

                await ctx.EditFollowupAsync(followup.Id, builder);
            }

            await Task.Delay(2000);

            string emojisFinal = slotMachineEmojis.Random() + " " + slotMachineEmojis.Random() + " " + slotMachineEmojis.Random();
            //if (Config.values.owners.Contains(ctx.User.Id))
            //    emojisFinal = "🎰 🎰 🎰";
            string[] emojisFinalSplit = emojisFinal.Split(' ').ToArray();
            string[] emojisFinalDistinct = emojisFinalSplit.Distinct().ToArray();
            builder.WithContent("Rolling....\n" + emojisFinal);
            await ctx.EditFollowupAsync(followup.Id, builder);
            await Task.Delay(1000);

            if (amountGambled > PersistentData.values.casinoPoints[ctx.User.Id])
                builder.WithContent("https://tenor.com/view/money-wallet-broke-gif-7855913");


            // as if the above code wasnt bad... below is even fucking worse. BLUGH.
            if (emojisFinalDistinct.Length == 1)
            {
                if (emojisFinalDistinct[0] == "🎰")
                    winAmount *= 20;
                builder.WithContent($"{builder.Content}\nYou won {winAmount} points! You now have {hasPoints + winAmount}!");
                GivePoints(ctx.User, winAmount);
            }
            else if (emojisFinalDistinct.Length == 2)
            {
                if (emojisFinalSplit.Count(str => str == "🎰") == 2)
                {

                    if (emojisFinalSplit[0] == emojisFinalSplit[2])
                    {
                        builder.WithContent($"{builder.Content}\nTwo slots, double your points! You have {hasPoints + amountGambled} points!");
                        GivePoints(ctx.User, amountGambled);
                    }
                    else
                    {
                        builder.WithContent($"{builder.Content}\nTwo slots in a row, you quintuple your points! You now have {hasPoints + amountGambled * 5} points!");
                        GivePoints(ctx.User, amountGambled * 5);
                    }

                }
                else
                {
                    if (emojisFinalSplit[0] == emojisFinalSplit[2])
                        builder.WithContent($"{builder.Content}\nTwo of a kind, you keep your points! You have {hasPoints} points!");
                    else
                    {
                        builder.WithContent($"{builder.Content}\nTwo in a row, you double your points! You now have {hasPoints + amountGambled} points!");
                        GivePoints(ctx.User, amountGambled);
                    }
                }
            }
            else
            {
                builder.WithContent($"{builder.Content}\nYou lost {amountGambled} points! You now have {hasPoints - amountGambled}");
                GivePoints(ctx.User, -amountGambled);
            }

            await ctx.EditFollowupAsync(followup.Id, builder);
            PersistentData.WritePersistentData();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error while gambling with {ctx.User}", ex);
        }
    }

    [Command("checkPoints"), Description("Check your points!")]
    public static async Task CheckCasinoPoints(SlashCommandContext ctx,
        [Parameter("sendSecretly"), Description("Whether to show to only you.")] bool ephemeral = true)
    {
        await BoneBot.Bots[ctx.Client].casino.CheckPoints(ctx, ephemeral);
    }

    [Command("slots"), Description("GAMBLING GAMBLING GAMBLING")]
    public static async Task GambleSlotsCmd(SlashCommandContext ctx,
        [Parameter("amount"), Description("How many points to gamble.")] int amount,
        [Parameter("sendSecretly"), Description("Whether to only show to you.")] bool ephemeral = true)
    {
        await BoneBot.Bots[ctx.Client].casino.GambleSlots(ctx, amount, ephemeral);
    }

    [Command("givePoints"), Description("Give a user points")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task GivePoints(SlashCommandContext ctx,
        [Parameter("amount"), Description("How many points to give.")] int amount,
        [Parameter("sendTo"), Description("Who to give points to.")] DiscordMember user)
    {
        if (await SlashCommands.ModGuard(ctx, true))
            return;

        BoneBot.Bots[ctx.Client].casino.GivePoints(user, amount);

        try
        {
            await ctx.RespondAsync($"Done, they now have {PersistentData.values.casinoPoints[user.Id]} points.", true);
        }
        catch (Exception ex)
        {
            Logger.Warn("Exception while giving points", ex);
        }
    }
}
