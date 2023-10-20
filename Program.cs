﻿namespace BoneBoard
{
    internal class Program
    {
        static BoneBot bot;

        static async Task Main(string[] args)
        {
            Logger.Put("Initializing bot...");
            bot = new BoneBot(Config.values.token);
            bot.Init();
            await Task.Delay(-1);
        }
    }
}