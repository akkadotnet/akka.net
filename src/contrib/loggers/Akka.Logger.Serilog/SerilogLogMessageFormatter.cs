using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Serilog.Events;
using Serilog.Parsing;

namespace Akka.Logger.Serilog
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

            for (var i = 0; i < args.Length; i++)
            {
                var propertyToken = propertyTokens.ElementAtOrDefault(i);
                if (propertyToken == null)
                    break;

                properties.Add(propertyToken.PropertyName, new ScalarValue(args[i]));
            }

            return template.Render(properties);
        }
    }
}