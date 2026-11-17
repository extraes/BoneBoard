namespace BoneBoard.Modules.Blockers;

public class HomeRestriction(BoneBot bot) : ModuleBase(bot)
{
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (Config.values.homeServerId == 0)
            return false;
        
        var guild = GetGuild(eventArgs);
        if (guild is null)
            return false;

        if (guild.Id == Config.values.homeServerId)
            return false;
        
        return true;
    }
}