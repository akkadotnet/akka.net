// <copyright file="ContextLoggingAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Event
{
    /// <summary>
    /// Internal wrapper used to enrich log messages with structured context properties.
    /// </summary>
    internal sealed class ContextLoggingAdapter : ILoggingAdapter
    {
        private readonly ILoggingAdapter _inner;
        private readonly IReadOnlyList<KeyValuePair<string, object>> _context;

        public ContextLoggingAdapter(ILoggingAdapter inner, IReadOnlyList<KeyValuePair<string, object>> context = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _context = context ?? Array.Empty<KeyValuePair<string, object>>();
        }

        public ILogMessageFormatter Formatter => _inner.Formatter;

        public bool IsDebugEnabled => _inner.IsDebugEnabled;

        public bool IsInfoEnabled => _inner.IsInfoEnabled;

        public bool IsWarningEnabled => _inner.IsWarningEnabled;

        public bool IsErrorEnabled => _inner.IsErrorEnabled;

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public ILoggingAdapter WithContext(string name, object value)
        {
            var nextContext = new KeyValuePair<string, object>[_context.Count + 1];
            for (var i = 0; i < _context.Count; i++)
                nextContext[i] = _context[i];
            nextContext[^1] = new KeyValuePair<string, object>(name, value);
            return new ContextLoggingAdapter(_inner, nextContext);
        }

        public void Log(LogLevel logLevel, Exception cause, string format)
        {
            if (_context.Count == 0)
            {
                _inner.Log(logLevel, cause, format);
                return;
            }

            var contextMessage = ContextLogMessage.Create(Formatter, format, _context);
            _inner.Log(logLevel, cause, contextMessage);
        }

        public void Log(LogLevel logLevel, Exception cause, LogMessage message)
        {
            if (_context.Count == 0)
            {
                _inner.Log(logLevel, cause, message);
                return;
            }

            var contextMessage = ContextLogMessage.Create(Formatter, message, _context);
            _inner.Log(logLevel, cause, contextMessage);
        }
    }
}
