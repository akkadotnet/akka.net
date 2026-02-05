// <copyright file="ContextLogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    internal interface IContextLogMessage
    {
        IReadOnlyList<KeyValuePair<string, object>> ContextProperties { get; }
    }

    /// <summary>
    /// Internal <see cref="LogMessage"/> wrapper that preserves original message formatting while
    /// appending additional semantic properties used for context enrichment.
    /// </summary>
    internal sealed class ContextLogMessage : LogMessage, IContextLogMessage
    {
        private readonly string _baseFormat;
        private readonly IReadOnlyList<object> _baseParameters;
        private readonly IReadOnlyList<KeyValuePair<string, object>> _context;
        private readonly IReadOnlyList<object> _allParameters;

        public IReadOnlyList<KeyValuePair<string, object>> ContextProperties => _context;

        private ContextLogMessage(
            ILogMessageFormatter formatter,
            string baseFormat,
            IReadOnlyList<object> baseParameters,
            IReadOnlyList<KeyValuePair<string, object>> context)
            : base(formatter, BuildContextTemplate(baseFormat, context))
        {
            _baseFormat = baseFormat;
            _baseParameters = baseParameters;
            _context = context;
            _allParameters = BuildAllParameters(baseParameters, context);
        }

        public static ContextLogMessage Create(
            ILogMessageFormatter formatter,
            LogMessage baseMessage,
            IReadOnlyList<KeyValuePair<string, object>> context)
        {
            return new ContextLogMessage(
                formatter,
                baseMessage.Format,
                NormalizeParameters(baseMessage.Parameters()),
                context);
        }

        public static ContextLogMessage Create(
            ILogMessageFormatter formatter,
            string format,
            IReadOnlyList<KeyValuePair<string, object>> context)
        {
            return new ContextLogMessage(formatter, format, Array.Empty<object>(), context);
        }

        private static IReadOnlyList<object> NormalizeParameters(IEnumerable<object> parameters)
        {
            if (parameters is IReadOnlyList<object> list)
                return list;

            return parameters.ToArray();
        }

        private static IReadOnlyList<object> BuildAllParameters(
            IReadOnlyList<object> baseParameters,
            IReadOnlyList<KeyValuePair<string, object>> context)
        {
            if (context.Count == 0)
                return baseParameters;

            var allParameters = new object[baseParameters.Count + context.Count];
            for (var i = 0; i < baseParameters.Count; i++)
                allParameters[i] = baseParameters[i];

            for (var i = 0; i < context.Count; i++)
                allParameters[baseParameters.Count + i] = context[i].Value;

            return allParameters;
        }

        private static string BuildContextTemplate(string baseFormat, IReadOnlyList<KeyValuePair<string, object>> context)
        {
            if (context.Count == 0)
                return baseFormat;

            var builder = new System.Text.StringBuilder(baseFormat.Length + context.Count * 12);
            builder.Append(baseFormat);

            foreach (var entry in context)
                builder.Append(' ').Append('{').Append(entry.Key).Append('}');

            return builder.ToString();
        }

        public override string ToString()
        {
            return Formatter.Format(_baseFormat, _baseParameters);
        }

        public override string Unformatted()
        {
            if (_baseParameters.Count == 0)
                return string.Empty;

            return string.Join(", ", _baseParameters.Select(arg => arg is null ? "null" : arg.ToString()));
        }

        public override IEnumerable<object> Parameters()
        {
            return _allParameters;
        }
    }
}
