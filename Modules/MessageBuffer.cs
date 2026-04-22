using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.ComponentModel;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace BoneBoard.Modules;

[Command("msgbuffer")]
internal partial class MessageBuffer(BoneBot bot) : ModuleBase(bot)
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


    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs && MessageCheck(msgCreatedArgs.Message))
        {
            Task.Run(() => BufferMessage(msgCreatedArgs.Message));

            return true;
        }

        return false;
    }

    private bool MessageCheck(DiscordMessage message)
    {
        DiscordMember? member = message.Author as DiscordMember;
        if (member is null)
            return false;

        bool isBufferExempt = member.Roles.Any(r => Config.values.bufferExemptRoles.Contains(r.Id));
        if (PersistentData.values.bufferedChannels.Contains(message.ChannelId) && !isBufferExempt &&
            !bot.IsMe(message.Author))
        {
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
                    Logger.Put(
                        $"Attachment {attachment.FileName} on message {msg.JumpLink} is too big to buffer! ({Math.Round(attachment.FileSize / 1024.0 / 1024.0, 2)}MB > {Math.Round(fileSizeLimit / 1024.0 / 1024.0, 2)}MB)");
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

                    if (reference is not null &&
                        recreatedContentToReferences.TryGetValue(reference.Content, out DiscordMessage? refMsg))
                        reference = refMsg;


                    string author = recreateMessage.Author is DiscordMember member
                        ? Formatter.Strip(member.DisplayName).Replace("://", "\\://")
                        : recreateMessage.Author?.Username ?? "uhh i forgor";
                    string replyingToAuthor = reference?.Author is DiscordMember replyMember
                        ? Formatter.Strip(replyMember.DisplayName).Replace("https:", "https\\:")
                        : reference?.Author?.Username ?? "NOBODY LOL";
                    string replyingToContent = reference is not null
                        ? Logger.EnsureShorterThan(reference.Content, 200, "(yap)").Replace("\n", "")
                        : "";
                    string content = Logger.EnsureShorterThan(recreateMessage.Content,
                        2000 - replyingToAuthor.Length - replyingToContent.Length - author.Length - 50,
                        "(Truncated due to excessive yapping)");

                    string finalContent = (reference is not null
                                              ? $"Replying to **{replyingToAuthor}**: '{replyingToContent}'\n"
                                              : "")
                                          + (!string.IsNullOrWhiteSpace(content) ? $"\"{content}\"\n" : "")
                                          + $"\\- {author}";

                    recreatedContentToReferences[finalContent] = recreateMessage;

                    var builder = new DiscordMessageBuilder()
                        .WithContent(finalContent)
                        .WithAllowedMentions(Enumerable.Empty<IMention>());

                    foreach (DiscordAttachment attachment in recreateMessage.Attachments)
                    {
                        // todo: make uploads go to extraes.xyz when too big (requires skating around cloudflare's 100mb limit on free accts. try/add chunking!)
                        if (attachment.FileSize > DEFAULT_FILE_SIZE_LIMIT || attachment.Url is null ||
                            attachment.FileName is null)
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

                if (!PersistentData.values.bufferChannelMessageFormats.TryGetValue(channel.Id,
                        out string? bufferFormat))
                {
                    Logger.Error(
                        $"Buffered channel {channel.Name} doesn't have a saved notification message format str!");
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


    [Command("setBufferedChannel")]
    [Description("Toggle whether this channel is un/buffered")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public static async Task SetBufferedChannel(
        SlashCommandContext ctx,
        [Parameter("add")] [Description("True to add, false to remove.")]
        bool add,
        [Parameter("bufferMessageText")] [Description("{0} will be replaced by timestamp.")]
        string bufferMessage = ""
    )
    {
        var channelId = ctx.Channel.Id;

        if (add)
        {
            if (PersistentData.values.bufferedChannels.Contains(channelId))
            {
                await ctx.RespondAsync("This channel is already buffered.", true);
                return;
            }

            if (string.IsNullOrWhiteSpace(bufferMessage))
            {
                await ctx.RespondAsync(
                    "You know... if you want to make a channel buffered, it's best if the people know that it's buffered.",
                    true);
                return;
            }

            var timestamp = "(a yet unknown time)";
            PersistentData.values.bufferedChannels.Add(channelId);
            var msg = await ctx.Channel.SendMessageAsync(string.Format(bufferMessage, timestamp));
            PersistentData.values.bufferChannelMessages[channelId] = msg.Id;
            PersistentData.values.bufferChannelMessageFormats[channelId] = bufferMessage;

            await ctx.RespondAsync("Added channel to buffer list.", true);
        }
        else
        {
            if (!PersistentData.values.bufferedChannels.Contains(channelId))
            {
                await ctx.RespondAsync("This channel isn't buffered.", true);
                return;
            }

            PersistentData.values.bufferedChannels.Remove(channelId);
            PersistentData.values.bufferChannelMessages.Remove(channelId);
            await ctx.RespondAsync("Removed channel from buffer list.", true);
        }

        PersistentData.WritePersistentData();
    }

    [Command("startUnbufferTimer")]
    [Description("Starts the timer to un-buffer messages sent during buffer-time")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task StartUnbufferTimer(SlashCommandContext ctx)
    {
        StartUnbufferTimer();
        var timestamp = Formatter.Timestamp(DateTime.Now.AddMinutes(Config.values.bufferTimeMinutes),
            TimestampFormat.ShortTime);

        StringBuilder sb = new($"Started unbuffer timer. Check back @ {timestamp}\n");

        foreach (var bufferedChannelId in PersistentData.values.bufferedChannels)
        {
            try
            {
                var channel = await ctx.Client.GetChannelAsync(bufferedChannelId);
                var msg = await channel.GetMessageAsync(PersistentData.values.bufferChannelMessages[bufferedChannelId]);
                await msg.ModifyAsync(
                    string.Format(PersistentData.values.bufferChannelMessageFormats[bufferedChannelId], timestamp));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"!!! Error editing channel's buffer notif msg (ID {bufferedChannelId}): {ex.Message}");
            }
        }

        await ctx.RespondAsync(sb.ToString(), true);
    }


    [Command("flushBufferedMessages")]
    [Description("Immediately flushes buffered messages. Doesn't stop the timer.")]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageRoles, DiscordPermission.ManageMessages])]
    public async Task FlushBufferedMessages(SlashCommandContext ctx)
    {
        await ctx.DeferResponseAsync(true);

        await SendBufferedMessages();

        var builder = new DiscordFollowupMessageBuilder().WithContent("Flushed buffered messages in all servers 👍.");
        await ctx.FollowupAsync(builder);
    }
}