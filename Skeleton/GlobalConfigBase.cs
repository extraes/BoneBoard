using System.Reflection;
using Tomlet;
using Tomlet.Attributes;

namespace Skeleton;

// Can't be generic. Statics are shared across all types, so if this were generic, it would be impossible to have a single global config for any assembly.
public abstract class GlobalConfigBase
{
    #region Statics

    internal static Dictionary<Type, GlobalConfigBase> globalConfigInstances = new();

    internal static Dictionary<Assembly, Type> callerAssemblyConfigs = new();
    static HashSet<Type> instantiatedConfigs = new();

    public static Type? SetNewCallerGlobalConfig(Type configType)
    {
        Assembly asm = Assembly.GetCallingAssembly();
        callerAssemblyConfigs.TryGetValue(asm, out Type? existingConfigType);
        callerAssemblyConfigs[asm] = configType;
        return existingConfigType;
    }

    public static GlobalConfigBase GetInstance(Type configType, string loadFromIfDoesntExist = "./config.toml")
    {
        if (!configType.IsAssignableTo(typeof(GlobalConfigBase)) || configType.IsAbstract)
        {
            throw new ArgumentException($"Type {configType.FullName} is not a valid GlobalConfigBase type.");
        }

        if (!globalConfigInstances.TryGetValue(configType, out GlobalConfigBase? instance))
        {
            string tomlString = File.ReadAllText(loadFromIfDoesntExist);
            instance = (GlobalConfigBase)TomletMain.To(configType, tomlString);
            globalConfigInstances[configType] = instance;
            instance.isNewInstance = true; // mark as new instance
        }
        return instance;
    }

    public static T GetInstance<T>(string loadFromIfDoesntExist = "./config.toml") where T : GlobalConfigBase, new()
    {
        var type = typeof(T);
        return GetInstance(type, loadFromIfDoesntExist) as T ?? throw new InvalidCastException($"Could not cast instance of {type.FullName} to {typeof(T).FullName}.");
    }

    public static bool IsInitialized<T>() where T : GlobalConfigBase
    {
        return globalConfigInstances.ContainsKey(typeof(T));
    }

    #endregion

    #region Abstracts/virtuals

    protected Dictionary<ulong, PerServerConfigBase> perServerConfigs = new();

    protected const string DEFAULT_SERVER_CFG_PATH = "./configs";

    protected virtual string GetServerConfigPath(ulong guildId)
    {
        return Path.Combine(DEFAULT_SERVER_CFG_PATH, $"{guildId}.toml");
    }

    protected internal virtual PerServerConfigBase GetConfigFor(Type configType, ulong guildId)
    {
        if (!configType.IsAssignableTo(typeof(PerServerConfigBase)))
        {
            throw new ArgumentException($"Type {configType.FullName} is not a valid PerServerConfigBase type.");
        }

        if (!perServerConfigs.TryGetValue(guildId, out PerServerConfigBase? config))
        {
            string configPath = GetServerConfigPath(guildId);
            string tomlString = File.ReadAllText(configPath);
            config = (PerServerConfigBase)TomletMain.To(configType, tomlString);
            config.guildId = guildId;
            config.isNewInstance = true; // mark as new instance
        }

        return config;
    }

    #endregion

    #region Provided utilities

    [TomlNonSerialized]
    protected bool isNewInstance = false;

    public T GetConfigFor<T>(ulong guildId) where T : PerServerConfigBase, new()
    {
        Type type = typeof(T);
        return GetConfigFor(type, guildId) as T ?? throw new InvalidCastException($"Could not cast instance of {type.FullName} to {typeof(T).FullName}.");
    }

    #endregion


    protected GlobalConfigBase()
    {
        Type t = GetType();
        if (instantiatedConfigs.Contains(t))
            throw new TypeInitializationException(t.FullName, new("Global configs are singleton classes. Instantiating them multiple times is not allowed."));

        instantiatedConfigs.Add(t);
    }
}
