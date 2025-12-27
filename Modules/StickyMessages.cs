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
internal class StickyMessages(BoneBot bot) : ModuleBase(bot)
{
    private ConcurrentHashSet<DiscordMessage> stickyMessages = new();
    
    protected override async Task FetchGuildResources()
    {
        stickyMessages.Clear();
        
        foreach (string jumpLink in PersistentData.values.stickiedMessages)
        {
            DiscordMessage? msg = await bot.GetMessageFromLink(jumpLink);
            if (msg is not null)
                stickyMessages.Add(msg);
        }
    }

    protected override async Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        if (args.Author.IsBot)
            return;

        Lazy<List<DiscordMessage>> newMessages = new();
        Lazy<List<DiscordMessage>> deletedStickies = new();
        
        foreach (var sticky in stickyMessages)
        {
            if (sticky.ChannelId != args.Message.ChannelId)
                continue;
            
            string content = sticky.Content;
            DiscordMessage msg;
            try
            {
                msg = await args.Channel.SendMessageAsync(content);
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to send Sticky message: ", ex);
                continue;
            }

            newMessages.Value.Add(msg);
            if (await TryDeleteAsync(sticky))
                deletedStickies.Value.Add(msg);
            
        }

        if (!newMessages.IsValueCreated)
            return; // nothing done

        foreach (DiscordMessage msg in deletedStickies.Value)
            stickyMessages.TryRemove(msg);
        
        foreach (DiscordMessage msg in newMessages.Value)
            stickyMessages.Add(msg);
        
        UpdatePersistentData();
        Logger.Put($"Re-stickied {newMessages.Value.Count} messages (deleted {deletedStickies.Value.Count} msgs)");
    }

    private void UpdatePersistentData()
    {
        PersistentData.values.stickiedMessages = stickyMessages.Select(m => m.JumpLink.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        PersistentData.WritePersistentData();
    }

    [Command("create"), Description("Creates a new sticky message in this channel.")]
    public async Task CreateStickyMessage(
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
    public async Task ClearStickyMessages(SlashCommandContext ctx)
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
            stickyMessages.TryRemove(msg);
        
        
        await ctx.RespondAsync($"Un-stickied {removeMessages.Count} message(s)", true);
        UpdatePersistentData();
    }
    
    [Command("remove"), Description("Stops a specific stickied message from being re-stickied in the future.")]
    public async Task RemoveStickyMessage(SlashCommandContext ctx, string jumpLink)
    {
        if (await SlashCommands.ModGuard(ctx))
            return;

        DiscordMessage? msg = await bot.GetMessageFromLink(jumpLink);
        if (msg is null)
        {
            await ctx.RespondAsync("Nothing was found from that link lol");
            return;
        }

        stickyMessages.TryRemove(msg);
        
        await ctx.RespondAsync($"Un-stickied that message!", true);
        UpdatePersistentData();
    }
}