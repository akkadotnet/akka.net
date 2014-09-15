using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Serilog.Events;
using Serilog.Parsing;

namespace Akka.Serilog.Event.Serilog
{
    public class SerilogLogMessageFormatter : ILogMessageFormatter
    {
        private readonly MessageTemplateCache _templateCache;

        public SerilogLogMessageFormatter()
        {
            _templateCache = new MessageTemplateCache(new MessageTemplateParser());
        }

        public string Format(string format, params object[] args)
        {
            var template = _templateCache.Parse(format);
            var propertyTokens = template.Tokens.OfType<PropertyToken>().ToArray();
            var properties = new Dictionary<string, LogEventPropertyValue>();

            if (propertyTokens.Length != args.Length)
                throw new FormatException("Invalid number or arguments provided.");

            for (var i = 0; i < propertyTokens.Length; i++)
            {
                var arg = args[i];

                properties.Add(propertyTokens[i].PropertyName, new ScalarValue(arg));
            }

            return template.Render(properties);
        }
    }
}