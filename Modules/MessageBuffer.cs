using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Skeleton;

namespace BoneBoard.Modules;

internal partial class MessageBuffer : ModuleBase
{
    const long DEFAULT_FILE_SIZE_LIMIT = 10 * 1000 * 1000; // 10MB
    static readonly Dictionary<DiscordPremiumTier, long> FileSizeLimits = new()
    {
        { DiscordPremiumTier.None, DEFAULT_FILE_SIZE_LIMIT },
        { DiscordPremiumTier.Tier_1, DEFAULT_FILE_SIZE_LIMIT },
        { DiscordPremiumTier.Tier_2, 50 * 1000 * 1000 },
        { DiscordPremiumTier.Tier_3, 100 * 1000 * 1000 },
    };

    //message buffer
    Timer? dumpMessagesTimer;
    Dictionary<DiscordChannel, Queue<DiscordMessage>> queuedMessages = new();
    Dictionary<string, DiscordMessage> recreatedContentToReferences = new();
    Dictionary<string, string> cachedQueuedAttachmentPaths = new();
    HttpClient attachmentDownloadClient = new();

    //sanitizing
    static readonly Regex mdCleaningRegex = MarkdownCleaningRegex();

    public MessageBuffer(BoneBot bot) : base(bot) { }

    protected override async Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return await MessageCheckAsync(msgCreatedArgs.Message);
        }

        return false;
    }

    private async Task<bool> MessageCheckAsync(DiscordMessage message)
    {
        DiscordMember? member = message.Author as DiscordMember;
        if (member is null)
            return false;

        bool isBufferExempt = member is not null && member.Roles.Any(r => Config.values.bufferExemptRoles.Contains(r.Id));
        if (PersistentData.values.bufferedChannels.Contains(message.ChannelId) && !isBufferExempt && !bot.IsMe(message.Author))
        {
            await BufferMessage(message);
            return true;
        }

        return false;
    }

    async Task BufferMessage(DiscordMessage msg)
    {
        try
        {
            if (msg.Channel is null)
            {
                Logger.Error("Message to buffer has no channel!");
                return;
            }

            if (!FileSizeLimits.TryGetValue(msg.Channel.Guild.PremiumTier, out long fileSizeLimit))
                fileSizeLimit = DEFAULT_FILE_SIZE_LIMIT;

            foreach (DiscordAttachment attachment in msg.Attachments)
            {
                if (attachment.FileSize > fileSizeLimit)
                {
                    Logger.Put($"Attachment {attachment.FileName} on message {msg.JumpLink} is too big to buffer! ({Math.Round(attachment.FileSize / 1024.0 / 1024.0, 2)}MB > {Math.Round(fileSizeLimit / 1024.0 / 1024.0, 2)}MB)");
                    continue;
                }
                if (string.IsNullOrEmpty(attachment.Url))
                {
                    Logger.Error("Attachment on message " + msg.JumpLink + " has no URL!");
                    continue;
                }

                string path = Path.GetTempFileName();
                using FileStream fs = File.OpenWrite(path);
                using Stream dlStream = await attachmentDownloadClient.GetStreamAsync(attachment.Url);
                await dlStream.CopyToAsync(fs);

                cachedQueuedAttachmentPaths[attachment.Url] = path;
            }

            if (!queuedMessages.TryGetValue(msg.Channel, out Queue<DiscordMessage>? queue))
            {
                queue = new();
                queuedMessages[msg.Channel] = queue;
            }

            queue.Enqueue(msg);

            await msg.DeleteAsync("Buffering message 😃👍"); // 😃👍
            Logger.Put($"Buffered message from {msg.Author} in {msg.Channel.Name} - {msg.Content}");
        }
        catch (Exception ex)
        {
            Logger.Error("Exception while buffering message! " + ex);
            bot.logChannel?.SendMessageAsync("Exception while buffering message! " + ex);
        }
    }

    public async Task SendBufferedMessages()
    {
        DateTime nextTime = DateTime.Now.AddMinutes(Config.values.bufferTimeMinutes);
        List<FileStream> openedFiles = new();

        foreach ((DiscordChannel channel, Queue<DiscordMessage> deletedMessages) in queuedMessages)
        {
            try
            {
                while (deletedMessages.Count > 0)
                {
                    DiscordMessage recreateMessage = deletedMessages.Dequeue();
                    Logger.Put($"Sending buffered message from {recreateMessage.Author}");
                    DiscordMessage? reference = recreateMessage.ReferencedMessage;

                    if (reference is not null && recreatedContentToReferences.TryGetValue(reference.Content, out DiscordMessage? refMsg))
                        reference = refMsg;


                    string author = recreateMessage.Author is DiscordMember member ? mdCleaningRegex.Replace(member.DisplayName, @$"\$1").Replace("://", "\\://") : recreateMessage.Author?.Username ?? "uhh i forgor";
                    string replyingToAuthor = reference?.Author is DiscordMember replyMember ? mdCleaningRegex.Replace(replyMember.DisplayName, @$"\$1").Replace("https:", "https\\:") : reference?.Author?.Username ?? "NOBODY LOL";
                    string replyingToContent = reference is not null ? Logger.EnsureShorterThan(reference.Content, 200, "(yap)").Replace("\n", "") : "";
                    string content = Logger.EnsureShorterThan(recreateMessage.Content, 2000 - replyingToAuthor.Length - replyingToContent.Length - author.Length - 50, "(Truncated due to excessive yapping)");

                    string finalContent = (reference is not null ? $"Replying to **{replyingToAuthor}**: '{replyingToContent}'\n" : "")
                        + (!string.IsNullOrWhiteSpace(content) ? $"\"{content}\"\n" : "")
                        + $"\\- {author}";

                    recreatedContentToReferences[finalContent] = recreateMessage;

                    var builder = new DiscordMessageBuilder()
                        .WithContent(finalContent)
                        .WithAllowedMentions(Enumerable.Empty<IMention>());

                    foreach (DiscordAttachment attachment in recreateMessage.Attachments)
                    {
                        // todo: make uploads go to extraes.xyz when too big (requires skating around cloudflare's 100mb limit on free accts. try/add chunking!)
                        if (attachment.FileSize > DEFAULT_FILE_SIZE_LIMIT || attachment.Url is null || attachment.FileName is null)
                            continue;

                        if (!cachedQueuedAttachmentPaths.TryGetValue(attachment.Url, out string? path))
                        {
                            Logger.Error("An attachment was on a queued message, but it wasn't cached on-disk!");
                            continue;
                        }

                        if (!File.Exists(path))
                        {
                            Logger.Error("An attachment supposedly queued, but it doesn't exist on-disk!");
                            continue;
                        }

                        FileStream fs = File.OpenRead(path);
                        string fileName = attachment.FileName;
                        while (builder.Files.Any(f => f.FileName == fileName))
                        {

                            fileName = Random.Shared.Next(10) + "_" + fileName;
                        }
                        builder.AddFile(fileName, fs);
                        openedFiles.Add(fs);
                    }

                    await channel.SendMessageAsync(builder);

                    await Task.Delay(1000); // rate limit

                    foreach (FileStream fs in openedFiles)
                    {
                        fs.Close();
                        File.Delete(fs.Name); // 👍
                    }
                    openedFiles.Clear();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception sending buffered message! " + ex);
                bot.logChannel?.SendMessageAsync("Exception sending buffered message! " + ex);
            }

            try
            {
                if (!PersistentData.values.bufferChannelMessages.TryGetValue(channel.Id, out ulong bufferNotifMsgId))
                {
                    Logger.Error($"Buffered channel {channel.Name} doesn't have a saved notification message ID!");
                    continue;
                }
                if (!PersistentData.values.bufferChannelMessageFormats.TryGetValue(channel.Id, out string? bufferFormat))
                {
                    Logger.Error($"Buffered channel {channel.Name} doesn't have a saved notification message format str!");
                    continue;
                }

                DiscordMessage? bufferNotifMsg = await TryFetchMessage(channel, bufferNotifMsgId);
                if (bufferNotifMsg is not null)
                    await bufferNotifMsg.DeleteAsync("outlived its usefulness. FOLD.");

                string content = string.Format(bufferFormat, Formatter.Timestamp(nextTime, TimestampFormat.ShortTime));
                DiscordMessage msg = await channel.SendMessageAsync(content);
                PersistentData.values.bufferChannelMessages[channel.Id] = msg.Id;
                PersistentData.WritePersistentData();
            }
            catch (Exception ex)
            {
                Logger.Error("Exception while editing buffer notif msg! " + ex);
            }
        }

        TimeSpan waitTime = TimeSpan.FromMinutes(Config.values.bufferTimeMinutes);

        dumpMessagesTimer?.Change(nextTime - DateTime.Now, waitTime);
    }


    public void StartUnbufferTimer()
    {
        dumpMessagesTimer?.Change(Timeout.Infinite, Timeout.Infinite); // "neuter" the old timer
        dumpMessagesTimer?.Dispose();

        TimeSpan waitTime = TimeSpan.FromMinutes(Config.values.bufferTimeMinutes);
        dumpMessagesTimer = new(_ => _ = SendBufferedMessages(), null, waitTime, waitTime);
    }


    [GeneratedRegex(@"(?<!\\)(\[|\]|\*|_|~|`|<|>|#)", RegexOptions.Compiled)]
    private static partial Regex MarkdownCleaningRegex();
}
