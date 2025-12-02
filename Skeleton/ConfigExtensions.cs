using DSharpPlus.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Skeleton;

public static class ConfigExtensions
{
    public static T? GetConfig<T>(this AbstractContext ctx) where T : PerServerConfigBase, new()
    {
        if (ctx.Guild is null)
            return null;

        Assembly caller = Assembly.GetCallingAssembly();
        GlobalConfigBase.callerAssemblyConfigs.TryGetValue(caller, out var configType);
        if (configType == null)
        {
            throw new InvalidOperationException($"No config type registered for assembly {caller.FullName} -- Set a GlobalConfigBase type using GlobalConfigBase.SetNewCallerGlobalConfig");
        }
        GlobalConfigBase mainCfg = GlobalConfigBase.GetInstance(configType);
        T serverCfg = mainCfg.GetConfigFor<T>(ctx.Guild.Id);
        return serverCfg;
    }

    // public static T? GetConfig<T>(this AbstractContext ctx) where T : PerServerConfigBase, IPerServerConfig, new()
    // {
    //     if (ctx.Guild is null)
    //         return null;
    //
    //     PerServerConfigBase.LoadOrCreateConfig<T>(ctx.);
    // }
    
}
