using System.ComponentModel;
using System.Reflection;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace BoneBoard.Modules.Blockers;

[Command("uwuifier")]
public class Uwuifier(BoneBot bot) : ModuleBase(bot)
{
    record MessageSignature(
        ulong ChannelId,
        string ImitationContent,
        int AttachmentCount,
        DateTime ImitateSendTime,
        DiscordMember Author)
    {
        public DateTime imitateFinishSendTime = ImitateSendTime;
    }

    private static readonly MethodInfo SetMessageAuthor =
        typeof(DiscordMessage).GetProperty(nameof(DiscordMessage.Author))!.SetMethod!;
    private static readonly Queue<MessageSignature> deletedMessages = new(256);
    
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        // ReSharper disable once IdentifierTypo
        if (eventArgs is MessageCreatedEventArgs mcea)
        {
            // The content filter is gonna do the heavy lifting here, lol
            MessageSignature? originalSig = deletedMessages.Where(dms =>
                    mcea.Message.CreationTimestamp > dms.ImitateSendTime
                    && mcea.Message.Content == dms.ImitationContent
                    && mcea.Channel.Id == dms.ChannelId
                    && mcea.Message.Attachments.Count == dms.AttachmentCount)
                .OrderBy(dms => mcea.Message.CreationTimestamp - dms.imitateFinishSendTime)
                .FirstOrDefault();
            // Check IsBot because webhook messages are marked as bot messages :) 
            if (originalSig is not null && mcea.Author.IsBot)
            {
                // Represents the seconds between Skinwalk's purported finish and this message's creation 
                // In my testing *from my college's library*, the time diff was ~0.1sec for a text-only message,
                // and ~1.9sec for a msg with a 5mb video, with this handler executing before Skinwalk had a chance to
                // set imitateFinishSendTime, but it's still useful to have imo.
                var timeDelta = (mcea.Message.CreationTimestamp - originalSig.imitateFinishSendTime).Duration();
                if (timeDelta < TimeSpan.FromSeconds(5))
                {
                    Logger.Put("Replacing the Author of message creation event for message: " + mcea.Message);
                    
                    // MessageCreatedEventArgs reads the message's Author for its Author property,
                    // so this will autopopulate to it.
                    SetMessageAuthor.Invoke(mcea.Message, [ originalSig.Author ]);
                }
                
                return false;
            }
            
            if (!PersistentData.values.uwuifiedUsers.TryGetValue(mcea.Channel.Id, out var users))
                return false;

            if (!users.Contains(mcea.Author.Id))
                return false;
            
            // This has the message's creation event effectively get replaced by the skinwalked message  
            TryDeleteDontCare(mcea.Message);
            
            Task.Run(() => Skinwalk(mcea.Message));
            return true;
        }

        return false;
    }

    async Task Skinwalk(DiscordMessage originalMessage)
    {
        if (originalMessage.Author is not DiscordMember member
            || originalMessage.Channel is null)
            return;
        
        var files = (await originalMessage.CopyAttachments()).ToArray();

        var newContent = new StringBuilder(originalMessage.Content.ToLower());
        newContent.Replace("w", "ww");
        newContent.Replace('r', 'w');
        newContent.Replace('l', 'w');

        
        var randomNum = Random.Shared.NextDouble();
        if (newContent.ToString() == originalMessage.Content)
            randomNum /= 2;
        
        string[] faces =
        [
            ">.<",
            "^.^",
            "☺︎",
            ":)))))",
            "OvO",
            "( ˶ˆᗜˆ˵ )",
            "૮₍  ˶•⤙•˶ ₎ა",
        ];
        const string STARS = "⁂⭐︎⋆✴︎∗";
        switch(randomNum)
        {
            case < 0.15:
                newContent.Append($" {faces.Random()}");
                break;
            case < 0.25:
                newContent.Append(' ');
                for (int i = 0; i < Random.Shared.Next(1, 4); i++)
                {
                    newContent.Append(STARS.Random());
                }
                break;
        }
        
        
        var builder = new DiscordWebhookBuilder()
            .WithContent(newContent.ToString())
            .AddFiles(files);

        var sig = new MessageSignature(originalMessage.ChannelId, builder.Content ?? "", files.Length, DateTime.Now, member);
        deletedMessages.Enqueue(sig);
        await WebhookImitation.Skinwalk(member, originalMessage.Channel, builder);
        // Is poor form to mutate a thing already in a list? Yeah, probably.
        sig.imitateFinishSendTime = DateTime.Now;
    }

    [Command("toggle"),
    Description("ONLY in this channel."),
    RequirePermissions([DiscordPermission.ManageWebhooks], 
        [DiscordPermission.ModerateMembers])]
    public static async Task Toggle(SlashCommandContext sctx, DiscordMember member)
    {
        await sctx.RespondAsync("This command is temporarily disabled." +
                                "Thank folks like:\n" +
                                "<@1122652014913003540> (for using it on a random person in general)\n" +
                                "<@634826035589808168> (for using it on admins)\n" +
                                "and more, for doing shit like using it *on this bot*\n" +
                                "My fault for not immediately putting permission requirements on it, I guess, but " +
                                "is it so difficult to think 'hey maybe I *shouldn't* fuck with that?", true);
        return;
        
        if (!PersistentData.values.uwuifiedUsers.TryGetValue(sctx.Channel.Id, out var users))
        {
            users = [];
            PersistentData.values.uwuifiedUsers[sctx.Channel.Id] = users;
        }
        
        if (users.Contains(member.Id))
            users.Remove(member.Id);
        else
            users.Add(member.Id);
        bool nowUwuified = users.Contains(member.Id);
        Logger.Put($"{member} {(nowUwuified ? "will" : "wont")} be uwuified at the request of {sctx.User}");
        
        PersistentData.WritePersistentData();
        
        await sctx.RespondAsync($"Done! Now {member.DisplayName} {(nowUwuified ? "will" : "won't")} have their messages uwu-ified!", true);
    }
}