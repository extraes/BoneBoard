using System.Diagnostics.CodeAnalysis;

namespace BoneBoard.Modules.Blockers;

[SuppressMessage("ReSharper", "IdentifierTypo")]
public class BlockUserFromChannel(BoneBot bot) : ModuleBase(bot)
{
    // ReSharper disable once IdentifierTypo
    
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        ulong channelId;
        DiscordUser user;
        switch (eventArgs)
        {
            case MessageCreatedEventArgs mcea:
            {
                channelId = mcea.Channel.Id;
                user = mcea.Author;
                if (Config.values.blockUsersFromChannels.TryGetValue(user.Id.ToString(), out ulong chId)
                    && chId == channelId)
                {
                    TryDeleteDontCare(mcea.Message);
                    return true;
                }
                    
                break;
            }
            case MessageUpdatedEventArgs muea:
            {
                channelId = muea.Channel.Id;
                user = muea.Author;
                
                if (Config.values.blockUsersFromChannels.TryGetValue(user.Id.ToString(), out ulong chId)
                    && chId == channelId)
                {
                    TryDeleteDontCare(muea.Message);
                    return true;
                }

                break;
            }
            case MessageDeletedEventArgs mdea:
            {
                if (mdea.Message.Author is null)
                    return false;
                
                channelId = mdea.Channel.Id;
                user = mdea.Message.Author;
                if (Config.values.blockUsersFromChannels.TryGetValue(user.Id.ToString(), out ulong chId)
                    && chId == channelId)
                {
                    return true;
                }
                
                break;
            }
            case MessageReactionAddedEventArgs mrea:
            {
                channelId = mrea.Channel.Id;
                user = mrea.User;

                if (Config.values.blockUsersFromChannels.TryGetValue(user.Id.ToString(), out ulong chId)
                    && chId == channelId)
                {
                    TryDeleteDontCare(mrea.Message, mrea.User, mrea.Emoji, "User blocked from this channel");
                    return true;
                }
                
                break;
            }
            case MessageReactionRemovedEventArgs mrmea:
            {
                channelId = mrmea.Channel.Id;
                user = mrmea.User;

                if (Config.values.blockUsersFromChannels.TryGetValue(user.Id.ToString(), out ulong chId)
                    && chId == channelId)
                {
                    return true;
                }
                break;
            }
            
            default:
                return false;
        }

        return false;
    }
}