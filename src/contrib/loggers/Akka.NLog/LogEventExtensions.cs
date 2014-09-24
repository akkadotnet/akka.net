using Akka.Event;
using NLog;
using System.Collections.Generic;
using AkkaLogLevel = global::Akka.Event.LogLevel;
using NLogLogLevel = global::NLog.LogLevel;

namespace Akka.NLog
{
    internal static class LogEventExtensions
    {
        /// <summary>
        /// Mapping of Akka's log levels on to NLog's log levels
        /// </summary>
        private static readonly Dictionary<AkkaLogLevel, NLogLogLevel> LogLevelsMap = new Dictionary<AkkaLogLevel, NLogLogLevel>()
        {
            { AkkaLogLevel.DebugLevel,   NLogLogLevel.Debug },
            { AkkaLogLevel.InfoLevel,    NLogLogLevel.Info },
            { AkkaLogLevel.WarningLevel, NLogLogLevel.Warn },
            { AkkaLogLevel.ErrorLevel,   NLogLogLevel.Error },
        };

        /// <summary>
        /// Extension method, converting Akka's LogEvent to NLog's log info.
        /// Event timestamp information is persisted.
        /// </summary>
        /// <param name="logEvent"></param>
        /// <returns></returns>
        public static LogEventInfo ToLogEventInfo(this LogEvent logEvent)
        {
            var result = new LogEventInfo(LogLevelsMap[logEvent.LogLevel()], logEvent.LogClass.Name,
                logEvent.Message.ToString())
            {
                TimeStamp = logEvent.Timestamp,
            };

            return result;
        }
    }
}