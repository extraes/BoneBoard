using System.Diagnostics.Contracts;
using DSharpPlus;
using Microsoft.Extensions.DependencyInjection;

namespace Skeleton;

/// <summary>
/// You probably don't mean to use this. You probably mean to use <see cref="ISklient{T}"/>.
/// </summary>
public interface ISklient
{
    /// <summary>
    /// Should return <see langword="null"/> after initialization.
    /// </summary>
    public static abstract DiscordClientBuilder? ClientBuilder { get; } 
    public static abstract DiscordClient Client { get; }

    #region Instance accessors so this library can access info on "client" implementations.
    
    [Pure]
    public DiscordClientBuilder? GetClientBuilder();
    
    [Pure]
    public DiscordClient GetClient();
    
    #endregion
}

public abstract class Sklient<T> where T : Sklient<T>, ISklient
{
    #region Instance accessors so this library can access info on "client" implementations. 
    
    // DiscordClientBuilder? ISklient.GetClientBuilder() => T.ClientBuilder;
    //
    // DiscordClient ISklient.GetClient() => T.Client;
    
    #endregion
    //
    // public static DiscordClientBuilder? ClientBuilder { get; }
    // public static DiscordClient Client { get; }
    
    
    private static DiscordClientBuilder builder;
    
    public Sklient(string token, DiscordIntents intents)
    {
        if (builder is not null)
            throw new InvalidOperationException($"{typeof(T).FullName} is a singleton type!");
        
        builder = DiscordClientBuilder.CreateDefault(token, intents);
        builder.ConfigureServices(x => x.AddSingleton(typeof(T), this));
    }
}