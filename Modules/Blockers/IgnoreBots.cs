namespace BoneBoard.Modules.Blockers;

public class IgnoreBots(BoneBot bot) : ModuleBase(bot)
{
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        var user = GetUser(eventArgs);
        return user?.IsBot ?? false;
    }
}