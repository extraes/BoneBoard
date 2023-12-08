namespace BoneBoard
{
    internal class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static BoneBot bot;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        static async Task Main(string[] args)
        {
            Logger.Put("Pre-initializing persistent data...");
            _ = PersistentData.values;

            Logger.Put("Initializing bot...");
            bot = new BoneBot(Config.values.token);
            bot.Init();
            await Task.Delay(-1);
        }
    }
}