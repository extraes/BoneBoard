namespace Skeleton.ExtraFeatures;

public interface IBlocklist : IGlobalConfig
{
    public ulong[] BlockedUsers { get; }
}