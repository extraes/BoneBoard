using System.Collections.Concurrent;
using System.ComponentModel;
using ConcurrentCollections;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace BoneBoard.Modules;

[Command("sticky")]
internal class StickyMessages : ModuleBase
{
    private static List<DiscordMessage> stickyMessages = new();

    public StickyMessages(BoneBot bot) : base(bot) { }

    protected override async Task FetchGuildResources()
    {
        stickyMessages.Clear();
        
        foreach (string jumpLink in PersistentData.values.stickiedMessages)
        {
            DiscordMessage? msg = await bot.GetMessageFromLink(jumpLink);

            if (msg is not null)
            {
                stickyMessages.Add(msg);
                Logger.Put($"Found sticky message @ {jumpLink} in {msg.Channel}");
            }
            else
            {
                Logger.Put($"Couldn't find sticky message @ {jumpLink} -- ignoring!");
            }
        }
        
        UpdatePersistentData();
        Logger.Put($"Initialized with {stickyMessages.Count} messages in {stickyMessages.Select(m => m.ChannelId).Distinct().Count()} channels");
    }

    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (args.Author.IsBot || bot.IsMe(args.Author))
            return;

        if (args.Message.MessageType != DiscordMessageType.Default)
            return;


        int prelength = stickyMessages.Count;
        stickyMessages = stickyMessages.DistinctBy(m => m.Id).ToList();
        Logger.Put($"Removed {prelength - stickyMessages.Count} duplicates from stickymessages", LogType.Trace);
            
        Lazy<List<DiscordMessage>> newMessages = new();
        Lazy<List<DiscordMessage>> deletedStickies = new();

        for (int i = stickyMessages.Count - 1; i >= 0; i--)
        {
            var sticky = stickyMessages[i];
            if (sticky.ChannelId != args.Message.ChannelId)
                continue;

            string content = sticky.Content;
            Logger.Put(
                $"Resending sticky message in {args.Channel} w/ content: '{Logger.EnsureShorterThan(content, 50)}'");
            DiscordMessage newMsg;
            try
            {
                newMsg = await args.Channel.SendMessageAsync(content);
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to send Sticky message: ", ex);
                continue;
            }

            newMessages.Value.Add(newMsg);
            stickyMessages.Add(newMsg);
            
            TryDeleteDontCare(sticky);
            stickyMessages.Remove(sticky);
            deletedStickies.Value.Add(sticky);
        }

        if (!newMessages.IsValueCreated)
            return; // nothing done

        UpdatePersistentData();
        Logger.Put($"Re-stickied {newMessages.Value.Count} messages (deleted {deletedStickies.Value.Count} msgs)");
    }

    private static void UpdatePersistentData()
    {
        PersistentData.values.stickiedMessages = stickyMessages.Select(m => m.JumpLink.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        PersistentData.WritePersistentData();
    }

    [Command("create"), Description("Creates a new sticky message in this channel.")]
    public static async Task CreateStickyMessage(
        SlashCommandContext ctx,
        [Description("\\n will be replaced with a newline.")] string content)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        content = content.Replace("\\n", "\n");
        try
        {
            var newSticky = await ctx.Channel.SendMessageAsync(content);
            stickyMessages.Add(newSticky);
        }
        catch (Exception ex)
        {
            Logger.Warn("Failed to create new Sticky message: ", ex);
            await ctx.RespondAsync("Failed to create message!\n" + Logger.EnsureShorterThan(ex.ToString(), 1950), true);
            return;
        }
        
        
        await ctx.RespondAsync("Created message! You should be able to see it!", true);
        UpdatePersistentData();
    }
    
    [Command("clear"), Description("Stops any stickied messages in this channel from being re-stickied in the future.")]
    public static async Task ClearStickyMessages(SlashCommandContext ctx)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        List<DiscordMessage> removeMessages = new();
        foreach (var msg in stickyMessages)
        {
            if (msg.ChannelId != ctx.Channel.Id)
                continue;
            
            removeMessages.Add(msg);
        }
        
        foreach (DiscordMessage msg in removeMessages)
            stickyMessages.Remove(msg);
        
        
        await ctx.RespondAsync($"Un-stickied {removeMessages.Count} message(s)", true);
        UpdatePersistentData();
    }
    
    [Command("remove"), Description("Stops a specific stickied message from being re-stickied in the future.")]
    public static async Task RemoveStickyMessage(SlashCommandContext ctx, string jumpLink)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        DiscordMessage? msg = await BoneBot.Bots[ctx.Client].GetMessageFromLink(jumpLink);
        if (msg is null)
        {
            await ctx.RespondAsync("Nothing was found from that link lol");
            return;
        }

        stickyMessages.Remove(msg);
        
        await ctx.RespondAsync($"Un-stickied that message!", true);
        UpdatePersistentData();
    }
    
    
    [Command("edit"), Description("Edits a stickied message")]
    public static async Task EditStickyMessage(SlashCommandContext ctx, string jumpLink, string newContent)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;
        
        DiscordMessage? msg = await BoneBot.Bots[ctx.Client].GetMessageFromLink(jumpLink);
        if (msg is null)
        {
            await ctx.RespondAsync("Nothing was found from that link lol");
            return;
        }

        newContent = newContent.Replace("\\n", "\n");
        try
        {
            await msg.ModifyAsync(newContent);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to edit Sticky message @ {jumpLink}", ex);
            await ctx.RespondAsync("Failed to edit message!\n" + Logger.EnsureShorterThan(Formatter.Sanitize(ex.ToString()), 1950), true);
            return;
        }
        
        
        await ctx.RespondAsync("Edited message!", true);
        UpdatePersistentData();
    }
}