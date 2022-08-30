using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Akka.TestKit.Hosting.Internals
{
    public class XUnitLogger: ILogger
    {
        private const string NullFormatted = "[null]";

        private readonly string _category;
        private readonly ITestOutputHelper _helper;
        private readonly LogLevel _logLevel;

        public XUnitLogger(string category, ITestOutputHelper helper, LogLevel logLevel)
        {
            _category = category;
            _helper = helper;
            _logLevel = logLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            if (!TryFormatMessage(state, exception, formatter, out var formattedMessage))
                return;
            
            WriteLogEntry(logLevel, eventId, formattedMessage, exception);
        }

        private void WriteLogEntry(LogLevel logLevel, EventId eventId, string message, Exception exception)
        {
            var level = logLevel switch
            {
                LogLevel.Critical => "CRT",
                LogLevel.Debug => "DBG",
                LogLevel.Error => "ERR",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Trace => "DBG",
                _ => "???"
            };
            
            var msg = $"{DateTime.Now}:{level}:{_category}:{eventId} {message}";
            if (exception != null)
                msg += $"\n{exception.GetType()} {exception.Message}\n{exception.StackTrace}";
            _helper.WriteLine(msg);
        }
        
        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.None => false,
                _ => logLevel >= _logLevel
            };
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }
        
        private static bool TryFormatMessage<TState>(
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter,
            out string result)
        {
            formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            
            var formattedMessage = formatter(state, exception);
            if (formattedMessage == NullFormatted)
            {
                result = null;
                return false;
            }
            
            result = formattedMessage;
            return true;
        }
    }    
}

