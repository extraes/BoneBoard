using System.Reflection;
using Tomlet;
using Tomlet.Attributes;

namespace Skeleton;

// file static class ServerConfigProviders
// {
//     public Dictionary<Type, Func<PerServerConfigBase>
// }

public interface IGlobalConfig
{
    public static abstract string LoadFromFile { get; }
}

public abstract class GlobalConfigBase<TConfig> : IGlobalConfig where TConfig : GlobalConfigBase<TConfig>, IGlobalConfig, new()
{
    
    #region Subtype provides info to GCB

    protected virtual bool DisableFileWatcher => false;

    /// <summary>
    /// If true, your config will be written immediately if it doesn't already exist. This near-immediately sets
    /// <see cref="LoadedFromFile"/> to a path.
    /// </summary>
    protected virtual bool WriteFileImmediately => true;

    #endregion
    
    #region GCB provides info to GCB

    /// <summary>
    /// Full path to source file, or <c>""</c> if the file didn't exist and the instance was created using the default
    /// constructor. Not available before/during the instance creation (will return <c>""</c>).
    /// <para>Will be set to the file path after the first write.</para>
    /// </summary>
    /// <seealso cref="InstanceExists"/>
    protected static string LoadedFromFile { get; private set; } = "";

    protected static bool InstanceExists => instance is not null;
    
    /// <summary>
    /// After this is executed, reflection will be used to set the new values in the existing instance.
    /// </summary>
    protected static event Action<TConfig>? OnConfigChangedPreMerge;

    /// <summary>
    /// This is executed after reflection is used to set new values into the existing config instance.
    /// </summary>
    public static event Action? OnConfigChanged;
    
    #endregion

    private static FileSystemWatcher? fileWatcher;
    
    private static TConfig? instance;

    public static TConfig Instance => GetOrCreateInstance();

    public static void WriteConfig()
    {
        if (instance is null)
            return;
        
        if (LoadedFromFile == string.Empty)
            LoadedFromFile = Path.GetFullPath(TConfig.LoadFromFile);

        string fileData = TomletMain.TomlStringFrom(instance);
        File.WriteAllText(LoadedFromFile, fileData);
    }
    
    public static TConfig GetOrCreateInstance()
    {
        if (instance is not null) return instance;
        
        string fullPath = Path.GetFullPath(TConfig.LoadFromFile);
        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            LoadedFromFile = "";
            instance = new TConfig();

            CreateFileWatcherIfNeeded(fullPath);
            
            return instance;
        }
            
            
        LoadedFromFile = fullPath;
        string tomlString = File.ReadAllText(fullPath);
        instance = (TConfig)TomletMain.To(typeof(TConfig), tomlString);

        
        CreateFileWatcherIfNeeded(fullPath);
        return instance;
    }

    private static void CreateFileWatcherIfNeeded(string fullPath)
    {
        if (instance?.DisableFileWatcher ?? true || fileWatcher is not null || Path.Exists(fileWatcher.Path))
            return;
        
        fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(fullPath) ?? "", Path.GetFileName(fullPath));
        fileWatcher.EnableRaisingEvents = true;
        fileWatcher.NotifyFilter = NotifyFilters.LastWrite;
        fileWatcher.Changed += FileWatcherOnChanged;
    }

    private static void FileWatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        string tomlString = File.ReadAllText(e.FullPath);
        var incomingInstance = (TConfig)TomletMain.To(typeof(TConfig), tomlString);
        OnConfigChangedPreMerge?.Invoke(incomingInstance);

        foreach (var property in typeof(TConfig).GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            try
            {
                property.SetValue(instance, property.GetValue(incomingInstance));
            }
            catch
            {
                // ignored
            }
        }
        
        foreach (var field in typeof(TConfig).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(f => !f.IsInitOnly))
        {
            field.SetValue(instance, field.GetValue(incomingInstance));
        }

        OnConfigChanged?.Invoke();
    }

    public static string LoadFromFile { get; }
}