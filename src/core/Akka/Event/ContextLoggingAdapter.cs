// <copyright file="ContextLoggingAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    internal sealed class ContextLoggingAdapter : ILoggingAdapter
    {
        private readonly ILoggingAdapter _inner;
        private readonly LogContextProperty[] _context;
        private readonly string _prefix;

        public ContextLoggingAdapter(ILoggingAdapter inner, string prefix = null, LogContextProperty[] context = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _prefix = prefix;
            _context = context ?? Array.Empty<LogContextProperty>();
        }

        public ILogMessageFormatter Formatter => _inner.Formatter;

        public bool IsDebugEnabled => _inner.IsDebugEnabled;

        public bool IsInfoEnabled => _inner.IsInfoEnabled;

        public bool IsWarningEnabled => _inner.IsWarningEnabled;

        public bool IsErrorEnabled => _inner.IsErrorEnabled;

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public ILoggingAdapter WithContext(string name, object value)
        {
            var nextContext = new LogContextProperty[_context.Length + 1];
            Array.Copy(_context, nextContext, _context.Length);
            nextContext[^1] = new LogContextProperty(name, value);
            return new ContextLoggingAdapter(_inner, _prefix, nextContext);
        }

        public ILoggingAdapter WithPrefix(string prefix)
        {
            var combinedPrefix = LoggingContextFormatting.CombinePrefix(_prefix, prefix);
            return new ContextLoggingAdapter(_inner, combinedPrefix, _context);
        }

        public void Log(LogLevel logLevel, Exception cause, string format)
        {
            if (_context.Length == 0 && string.IsNullOrEmpty(_prefix))
            {
                _inner.Log(logLevel, cause, format);
                return;
            }

            if (_context.Length == 0)
            {
                var prefixedFormat = LoggingContextFormatting.ApplyPrefix(_prefix, format);
                _inner.Log(logLevel, cause, prefixedFormat);
                return;
            }

            var contextMessage = ContextLogMessage.Create(Formatter, format, _prefix, _context);
            _inner.Log(logLevel, cause, contextMessage);
        }

        public void Log(LogLevel logLevel, Exception cause, LogMessage message)
        {
            if (_context.Length == 0 && string.IsNullOrEmpty(_prefix))
            {
                _inner.Log(logLevel, cause, message);
                return;
            }

            var contextMessage = ContextLogMessage.Create(Formatter, message, _prefix, _context);
            _inner.Log(logLevel, cause, contextMessage);
        }
    }
}
