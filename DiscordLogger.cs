using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard
{
    // do whatever the fuck this shit is in order to get dsharpplus to use Logger.Put
    internal class DiscordLogger : ILogger
    {
        internal class Provider : ILoggerProvider
        {
            public void Dispose()
            {
                // if (Debugger.IsAttached)
                //     throw new NotImplementedException();
                // else
                Logger.Warn("Logger disposed. When the hell does this happen?");
            }

            public ILogger CreateLogger(string categoryName)
            {
                if (Config.values.ignoreDiscordLogsFrom.Any(str =>
                        categoryName.Contains(str, StringComparison.InvariantCultureIgnoreCase)))
                {
                    Logger.Warn($"Giving Discord a null logger for the category {categoryName}");
                    return NullLogger.Instance;
                }
                
                return new DiscordLogger(categoryName);
            }
        }
        
        internal class Factory : ILoggerFactory
        {
            //List<ILoggerProvider> providers = new();
            public void AddProvider(ILoggerProvider provider)
            {
                //providers.Add(provider);
            }

            public ILogger CreateLogger(string categoryName)
            {
                DiscordLogger logger = new(categoryName);
                return logger;
            }

            public void Dispose() { }
        }

        static LogType ToReason(LogLevel level)
            => level switch
            {
                LogLevel.Debug => LogType.Trace,
                LogLevel.Information => LogType.Normal,
                LogLevel.Warning => LogType.Warn,
                LogLevel.Error => LogType.Fatal,
                LogLevel.Critical => LogType.Fatal,
                _ => throw new InvalidEnumArgumentException(nameof(level), (int)level, typeof(LogLevel)),
            };

        readonly string catName;

        private DiscordLogger(string catName)
        {
            this.catName = catName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

        public bool IsEnabled(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.None:
                case LogLevel.Trace:
                    // "long term" my ass, I don't need to know that I got a 200 response, that's log spam from the HTTP client. 
                case LogLevel.Debug or LogLevel.Information when !Config.values.logDiscordDebug:
                    return false;
                default:
                    return true;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            LogType reason = ToReason(logLevel);

            if (Config.values.ignoreDiscordLogsWith.Any(str =>
                    message.Contains(str, StringComparison.InvariantCultureIgnoreCase)))
                return;
            
            if (eventId.Name is not null) reason.name = $"{reason.name} @ {eventId.Name}";

            Logger.Put(catName + " => " + message, reason);
        }
    }
}
