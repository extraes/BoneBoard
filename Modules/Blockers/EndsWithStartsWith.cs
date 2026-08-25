using System.Globalization;

namespace BoneBoard.Modules.Blockers
{
    public class EndsWithStartsWith(BoneBot bot) : ModuleBase(bot)
    {
        // Most recent message at the end of the list.
        private static readonly Dictionary<DiscordChannel, List<DiscordMessage>> lastMessages = [];
        private static readonly Queue<DiscordMessage> deletedMessages = [];

        protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
        {
            if (eventArgs is MessageCreatedEventArgs mcea)
            {
                return MessageCheck(mcea.Message);
            }

            if (eventArgs is MessageUpdatedEventArgs muea)
            {
                return MessageCheck(muea.Message);
            }

            return false;
        }

        // True: block message. False: don't block message
        protected bool MessageCheck(DiscordMessage msg)
        {
            if (bot.IsMe(msg.Author))
                return false;

            if (msg.Channel is null)
                return false;

            if (!Config.values.channelsWhereMsgsMustStartWithPrevMsgsLastChar.Contains(msg.ChannelId))
                return false;

            if (!lastMessages.TryGetValue(msg.Channel, out var msgQueue)
                || msgQueue.Count == 0)
                return false;


            char lastLetterOfLast = default;

            // The use of < makes the .Where filters for messages older than our message
            // (this is basically just the "support message edits" change)
            foreach (var mostRecentMsg in msgQueue.Where(m => m.Id < msg.Id).Reverse())
            {
                if (TryGetLastChar(mostRecentMsg, out lastLetterOfLast))
                    break;
            }

            if (lastLetterOfLast == default)
            {
                return false; // just give them a pass
            }


            if (!TryGetFirstChar(msg, out char firstLetter))
                return false; // likewise, just give this poster a pass

                    
            bool lettersMatch = CultureInfo.InvariantCulture.CompareInfo.Compare(
                    lastLetterOfLast.ToString(),
                    firstLetter.ToString(),
                    CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreCase
                ) == 0;
            if (lettersMatch) return false;

            deletedMessages.Enqueue(msg);
            if (deletedMessages.Count > 64)
                deletedMessages.Dequeue();

            TryDeleteDontCare(msg, $"Didn't start with '{lastLetterOfLast}' from prev msg");
            return true;
        }

        protected override Task MessageCreated(DiscordClient client, MessageCreatedEventArgs args)
        {
            if (!Config.values.channelsWhereMsgsMustStartWithPrevMsgsLastChar.Contains(args.Channel.Id))
                return Task.CompletedTask;

            if (!TryGetLastChar(args.Message, out _))
            {
                Logger.Put($"Ignoring message because it didn't have a valid last character/letter: '{args.Message.Content}'");
                return Task.CompletedTask;
            }

            // Yes I know a List isn't a Queue. I'm just going to use it like a queue.
            if (!lastMessages.TryGetValue(args.Channel, out var msgQueue))
            {
                msgQueue = [];
                lastMessages[args.Channel] = msgQueue;
            }

            msgQueue.Add(args.Message);
            if (msgQueue.Count > 8)
                msgQueue.RemoveAt(0);
            return Task.CompletedTask;
        }

        protected override Task MessageDeleted(DiscordClient client, MessageDeletedEventArgs args)
        {
            if (!Config.values.channelsWhereMsgsMustStartWithPrevMsgsLastChar.Contains(args.Channel.Id)
                || deletedMessages.Contains(args.Message)
                || !lastMessages.TryGetValue(args.Channel, out var msgQueue))
                return Task.CompletedTask;

            // Ensure the message isn't used anymore 
            msgQueue.Remove(args.Message);
            return Task.CompletedTask;
        }

        protected override Task MessageUpdated(DiscordClient client, MessageUpdatedEventArgs args)
        {
            if (!Config.values.channelsWhereMsgsMustStartWithPrevMsgsLastChar.Contains(args.Channel.Id)
                || !lastMessages.TryGetValue(args.Channel, out var msgQueue))
                return Task.CompletedTask;

            // Could I have just used .MessageBefore or relied on the overridden equality operator? Probably.
            // Did I want to? evidently not really. this avoids relying on hidden control flow that I'm not certain about.
            var oldMsgIdx = msgQueue.FindIndex(m => m.Id == args.Message.Id);

            if (oldMsgIdx != -1)
            {
                // Update the message by replacing its old instance with the new one
                msgQueue.RemoveAt(oldMsgIdx);
                msgQueue.Insert(oldMsgIdx, args.Message);
            }

            return Task.CompletedTask;
        }
        
        private static bool TryGetLastChar(DiscordMessage msg, out char lastChar)
        {
            lastChar = default;
            if (string.IsNullOrWhiteSpace(msg?.Content))
            {
                return false;
            }
            
            string cleanContent = TextThings.CustomEmoji.Replace(msg.Content, "<>");
            cleanContent = TextThings.ChannelMention.Replace(cleanContent, "<>");
            cleanContent = TextThings.UserMention.Replace(cleanContent, "<>");
            
            // Is likely a tenor/klipy GIF or something
            var hasMediaEmbed = msg.Attachments.Count > 0
                                || msg.Embeds.Any(e => e.Type is "image" or "gif" or "gifv" or "video");
            if (string.IsNullOrWhiteSpace(TextThings.Link.Replace(cleanContent, ""))
                && hasMediaEmbed)
                return false;
            
            lastChar = cleanContent.LastOrDefault(char.IsLetter);
            
            return lastChar != default;
        }
        
        private static bool TryGetFirstChar(DiscordMessage msg, out char firstChar)
        {
            firstChar = default;
            if (string.IsNullOrWhiteSpace(msg?.Content))
            {
                return false;
            }
            
            string cleanContent = TextThings.CustomEmoji.Replace(msg.Content, "<>");
            cleanContent = TextThings.ChannelMention.Replace(cleanContent, "<>");
            cleanContent = TextThings.UserMention.Replace(cleanContent, "<>");
            cleanContent = TextThings.Link.Replace(cleanContent, "");
            
            // Could be a tenor or klipy GIF, but Discord sometimes takes time to process those and embed them.
            if (string.IsNullOrWhiteSpace(cleanContent))
                return false;
            
            firstChar = cleanContent.FirstOrDefault(char.IsLetter);
            
            return firstChar != default;
        }
    }
}