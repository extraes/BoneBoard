using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;

namespace BoneBoard.Plumbing;

public sealed class EnforceUserPermissionsCheck : IContextCheck<RequirePermissionsAttribute>
{
    private async Task<DiscordMember?> TryGetMember(CommandContext ctx)
    {
        if (ctx.Member is not null)
            return ctx.Member;

        if (ctx.Guild is null)
            return null;
        try
        {
            return await ctx.Guild.GetMemberAsync(ctx.User.Id);
        }
        catch
        {
            return null;
        }
    }
    
    public async ValueTask<string?> ExecuteCheckAsync(RequirePermissionsAttribute attribute, CommandContext context)
    {
        if (context.Guild is null)
            return "This command can only be executed from within a server";
        
        var member = await TryGetMember(context);
        if (member is null)
            return $"Could not identify member from user: {context.User}";
        
        if (!member.PermissionsIn(context.Channel).HasAllPermissions(attribute.UserPermissions))
            return "You must have the following permissions to run this command: " + attribute.UserPermissions;

        // allow cmd execution
        return null;
    }
}