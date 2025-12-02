using DSharpPlus.EventArgs;

namespace Skeleton.ExtraFeatures;

public class IgnoreBlockedUsers : SkModuleBase
{
    private IBlocklist config;
    
    public IgnoreBlockedUsers(ISklient bot, IBlocklist supplierInstance) : base(bot)
    {
        config = supplierInstance;
    }

    protected override Task<bool> GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        bool needStop = false;
        // quick filtering for blocked users
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
            needStop = needStop || config.BlockedUsers.Contains(msgCreatedArgs.Author.Id);
        else if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
            needStop = needStop || config.BlockedUsers.Contains(msgUpdatedArgs.Author.Id);
        else if (eventArgs is MessageReactionAddedEventArgs rxnArgs)
            needStop = needStop || config.BlockedUsers.Contains(rxnArgs.User.Id);

        return Task.FromResult(needStop);
    }
}