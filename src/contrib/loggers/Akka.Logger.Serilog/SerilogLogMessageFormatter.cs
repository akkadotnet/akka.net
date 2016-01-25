//-----------------------------------------------------------------------
// <copyright file="SerilogLogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Serilog.Events;
using Serilog.Parsing;

namespace Akka.Logger.Serilog
{
    /// <summary>
    /// This class contains methods used to convert Serilog templated messages
    /// into normal text messages.
    /// </summary>
    public class SerilogLogMessageFormatter : ILogMessageFormatter
    {
        private readonly MessageTemplateCache _templateCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerilogLogMessageFormatter"/> class.
        /// </summary>
        public SerilogLogMessageFormatter()
        {
            _templateCache = new MessageTemplateCache(new MessageTemplateParser());
        }

        /// <summary>
        /// Converts the specified template string to a text string using the specified
        /// token array to match replacements.
        /// </summary>
        /// <param name="format">The template string used in the conversion.</param>
        /// <param name="args">The array that contains values to replace in the template.</param>
        /// <returns>
        /// A text string where the template placeholders have been replaced with
        /// their corresponding values.
        /// </returns>
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

