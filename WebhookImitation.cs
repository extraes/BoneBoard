using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace BoneBoard;

public static class WebhookImitation
{
    private static Dictionary<DiscordChannel, DiscordWebhook> webhooks = [];
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="member">The guild member to impersonate. This will grab the </param>
    /// <param name="inChannel">The channel to send the message in.</param>
    /// <param name="partialBuilder">Webhook message builder. Its username and avatar will be set by this method.</param>
    /// <returns></returns>
    public static async Task<DiscordMessage> Skinwalk(DiscordMember member,
                                               DiscordChannel inChannel,
                                               DiscordWebhookBuilder partialBuilder)
    {
        DiscordChannel hookChannel = inChannel.IsThread ? inChannel.Parent : inChannel;
        ulong? threadId = inChannel.IsThread ? inChannel.Id : null;
        
        partialBuilder.WithUsername(member.DisplayName)
            .WithAvatarUrl(member.DisplayAvatarUrl);
        
        if (!webhooks.TryGetValue(hookChannel, out var hook))
        {
            var channelsHooks = await hookChannel.GetWebhooksAsync();

            hook = channelsHooks.FirstOrDefault(wh => wh.Name == "Skinwalker")
                        ?? await hookChannel.CreateWebhookAsync("Skinwalker");
            
            webhooks.Add(hookChannel, hook);
        }

        if (threadId.HasValue)
        {
            partialBuilder.WithThreadId(threadId.Value);
        }
        
        return await hook.ExecuteAsync(partialBuilder);
    }

    public static async Task Skinwalk(SlashCommandContext sctx, DiscordMember member, string text)
    {
        var builder = new DiscordWebhookBuilder()
            .WithContent(text);

        await Skinwalk(member, sctx.Channel, builder);
        await sctx.RespondAsync("Done!", true);
    }
}