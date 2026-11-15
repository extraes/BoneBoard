using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;

namespace BoneBoard.Plumbing;

public class EnforceHomeServer : IContextCheck<UnconditionalCheckAttribute>
{
    public async ValueTask<string?> ExecuteCheckAsync(UnconditionalCheckAttribute attribute, CommandContext context)
    {
        if (Config.values.homeServerId == default)
            return null;
        
        // Allow owners to act outside of home server (e.g. in DMs)
        if (context.Client.CurrentApplication.Owners?.Contains(context.User)
            ?? false)
            return null;

        if (context.Guild is not null
            && context.Guild.Id != Config.values.homeServerId)
            return null;
        
        var currUser = context.Client.CurrentUser;
        return $"{currUser.GlobalName ?? currUser.GlobalName} has been configured with a home server, so commands must be run inside that server.";
    }
}