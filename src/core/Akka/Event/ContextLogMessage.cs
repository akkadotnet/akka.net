// <copyright file="ContextLogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Event
{
    /// <summary>
    /// Internal transport wrapper used to carry immutable context metadata into log event creation.
    /// </summary>
    internal sealed class ContextLogMessage : LogMessage
    {
        private static readonly IReadOnlyList<object> EmptyParameters = Array.Empty<object>();

        private readonly object _message;
        private readonly LogMessage _inner;

        public ContextLogMessage(ILogMessageFormatter formatter, object message, KeyValuePair<string, object>[] contextProperties)
            : base(formatter,
                message is LogMessage logMessage ? logMessage.Format : message?.ToString() ?? string.Empty)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _inner = message as LogMessage;
            ContextProperties = contextProperties ?? throw new ArgumentNullException(nameof(contextProperties));
        }

        public object Message => _message;

        public KeyValuePair<string, object>[] ContextProperties { get; }

        public override string ToString()
        {
            return _message.ToString();
        }

        public override string Unformatted()
        {
            return _inner?.Unformatted() ?? (_message?.ToString() ?? string.Empty);
        }

        public override IEnumerable<object> Parameters()
        {
            return _inner?.Parameters() ?? EmptyParameters;
        }
    }
}
