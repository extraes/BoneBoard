using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tomlet.Attributes;
using Tomlet;
using DSharpPlus.Entities;

namespace Skeleton;

public interface IPerServerConfig
{
    public static abstract string FilePathFormat { get; }
}

public abstract class PerServerConfigBase
{
    
    #region Subtype provides info to PSCB

    protected virtual bool DisableFileWatcher { get => false; }

    #endregion
    
    #region PSCB provides info to GCB

    /// <summary>
    /// Full path to source file, or <c>""</c> if the file didn't exist and the instance was created using the default
    /// constructor. Not available before the instance creation (will return <c>""</c>).
    /// </summary>
    [TomlNonSerialized]
    protected string LoadedFromFile { get; private set; } = "";
    
    /// <summary>
    /// After this is executed, reflection will be used to set the new values in the existing instance.
    /// </summary>
    protected event Action<PerServerConfigBase>? OnConfigChangedPreMerge;

    /// <summary>
    /// This is executed after reflection is used to set new values into the existing config instance.
    /// </summary>
    protected event Action? OnConfigChanged;
    
    #endregion

    /// <summary>
    /// Fires after any config is changed from disk and those changes are retrieved and set into the config instance.
    /// </summary>
    public static event Action<PerServerConfigBase>? OnAnyConfigChanged;
    
    internal static List<PerServerConfigBase> allLoadedConfigs = new();
    internal static Dictionary<Type, Dictionary<ulong, PerServerConfigBase>> perServerConfigs = new();

    [TomlNonSerialized]
    public ulong guildId;
    [TomlInlineComment("This is here for your convenience. It gets replaced with the guild's real name when it gets loaded.")]
    public string guildName;

    protected PerServerConfigBase(DiscordGuild guild) : this()
    {
        guildId = guild.Id;
        guildName = guild.Name;
    }

    // for deserialization
    protected PerServerConfigBase()
    {
        
    }
    
    

    public static T LoadOrCreateConfig<T>(ulong guildId) where T : PerServerConfigBase, IPerServerConfig, new()
    {
        string path = string.Format(T.FilePathFormat, guildId);
        string fullPath = Path.GetFullPath(path);
        
        
        if (perServerConfigs.TryGetValue(typeof(T), out var serverCfgs) && serverCfgs.TryGetValue(guildId, out var serverCfg))
        {
            return (T)serverCfg;
        }

        T newInstance;
        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            newInstance = new T();
        }
        else
        {
            string cfgText = File.ReadAllText(fullPath);
            newInstance = TomletMain.To<T>(cfgText);
        }


        allLoadedConfigs.Add(newInstance);
        if (!perServerConfigs.TryGetValue(typeof(T), out var configs))
        {
            perServerConfigs[typeof(T)] = configs = new();
        }
        
        configs[guildId] = newInstance;
        
        return newInstance;
    }


    public static string FilePathFormat { get; }
}
