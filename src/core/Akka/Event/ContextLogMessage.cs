// <copyright file="ContextLogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    internal interface IContextLogMessage
    {
        IReadOnlyList<LogContextProperty> ContextProperties { get; }
    }

    internal sealed class ContextLogMessage : LogMessage, IContextLogMessage
    {
        private readonly IReadOnlyList<object> _baseParameters;
        private readonly IReadOnlyList<LogContextProperty> _context;
        private readonly ContextLogValues _propertyValues;
        private readonly string _baseUnformatted;

        public IReadOnlyList<LogContextProperty> ContextProperties => _context;

        private ContextLogMessage(
            ILogMessageFormatter formatter,
            string format,
            IReadOnlyList<object> baseParameters,
            IReadOnlyList<string> basePropertyNames,
            IReadOnlyList<LogContextProperty> context,
            string baseUnformatted)
            : base(formatter, format)
        {
            _baseParameters = baseParameters;
            _context = context;
            _baseUnformatted = baseUnformatted;
            _propertyValues = new ContextLogValues(baseParameters, context);

            if (context.Count == 0)
            {
                SetPropertyNames(basePropertyNames);
                return;
            }

            var baseNameCount = basePropertyNames.Count;
            if (basePropertyNames.Count > baseParameters.Count)
                baseNameCount = baseParameters.Count;

            var combinedNames = new string[baseNameCount + context.Count];
            for (var i = 0; i < baseNameCount; i++)
                combinedNames[i] = basePropertyNames[i];

            for (var i = 0; i < context.Count; i++)
                combinedNames[baseNameCount + i] = context[i].Name;

            SetPropertyNames(combinedNames);
        }

        public static ContextLogMessage Create(ILogMessageFormatter formatter, LogMessage baseMessage, string prefix, IReadOnlyList<LogContextProperty> context)
        {
            var baseParameters = NormalizeParameters(baseMessage.Parameters());
            var basePropertyNames = baseMessage.PropertyNames;
            var format = LoggingContextFormatting.ApplyPrefix(prefix, baseMessage.Format);
            return new ContextLogMessage(
                formatter,
                format,
                baseParameters,
                basePropertyNames,
                context,
                baseMessage.Unformatted());
        }

        public static ContextLogMessage Create(ILogMessageFormatter formatter, string format, string prefix, IReadOnlyList<LogContextProperty> context)
        {
            var baseParameters = Array.Empty<object>();
            var finalFormat = LoggingContextFormatting.ApplyPrefix(prefix, format);
            var basePropertyNames = MessageTemplateParser.GetPropertyNames(finalFormat);
            return new ContextLogMessage(
                formatter,
                finalFormat,
                baseParameters,
                basePropertyNames,
                context,
                format);
        }

        private static IReadOnlyList<object> NormalizeParameters(IEnumerable<object> parameters)
        {
            if (parameters is IReadOnlyList<object> readOnlyList)
                return readOnlyList;

            return parameters.ToArray();
        }

        public override string ToString()
        {
            if (_baseParameters.Count == 0)
                return Format;

            return Formatter.Format(Format, _baseParameters);
        }

        public override string Unformatted()
        {
            if (_context.Count == 0)
                return _baseUnformatted;

            return string.Concat(_baseUnformatted, " | context=", LoggingContextFormatting.FormatContextSegments(_context));
        }

        public override IEnumerable<object> Parameters()
        {
            return _baseParameters;
        }

        protected override IReadOnlyList<object> GetPropertyValues()
        {
            if (_context.Count == 0)
                return _baseParameters;

            return _propertyValues;
        }

        private readonly struct ContextLogValues : IReadOnlyList<object>
        {
            private readonly IReadOnlyList<object> _baseValues;
            private readonly IReadOnlyList<LogContextProperty> _context;

            public ContextLogValues(IReadOnlyList<object> baseValues, IReadOnlyList<LogContextProperty> context)
            {
                _baseValues = baseValues ?? Array.Empty<object>();
                _context = context ?? Array.Empty<LogContextProperty>();
            }

            public int Count => _baseValues.Count + _context.Count;

            public object this[int index]
            {
                get
                {
                    if (index < _baseValues.Count)
                        return _baseValues[index];

                    return _context[index - _baseValues.Count].Value;
                }
            }

            public IEnumerator<object> GetEnumerator()
            {
                for (var i = 0; i < Count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
