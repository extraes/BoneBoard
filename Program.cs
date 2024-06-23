using System.Runtime.CompilerServices;

namespace BoneBoard
{
    internal class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static BoneBot bot;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        [MethodImpl(MethodImplOptions.NoOptimization)]
        static async Task Main(string[] args)
        {
            Logger.Put("Pre-initializing persistent data...");
            _ = PersistentData.values;

            Logger.Put("Initializing bot...");
            bot = new BoneBot(Config.values.token);
            bot.Init();

            while(true)
            {
                await Task.Delay(60 * 1000);
                // just here so i can use the debugger if my fucking internet kills itself again
            }
        }
    }
}