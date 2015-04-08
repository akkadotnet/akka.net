//-----------------------------------------------------------------------
// <copyright file="MessageTemplateCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Serilog.Events;
using Serilog.Parsing;

namespace Akka.Logger.Serilog
{
    /// <summary>
    /// Taken directly from Serilog as the cache was internal.
    /// https://github.com/serilog/serilog/blob/master/src/Serilog/Core/Pipeline/MessageTemplateCache.cs
    /// </summary>
    internal class MessageTemplateCache
    {
        private readonly MessageTemplateParser _innerParser;
        private readonly Dictionary<string, MessageTemplate> _templates = new Dictionary<string, MessageTemplate>();
        private readonly object _templatesLock = new object();

        const int MaxCacheItems = 1000;

        public MessageTemplateCache(MessageTemplateParser innerParser)
        {
            if (innerParser == null) throw new ArgumentNullException("innerParser");
            _innerParser = innerParser;
        }

        public MessageTemplate Parse(string messageTemplate)
        {
            if (messageTemplate == null) throw new ArgumentNullException("messageTemplate");

            MessageTemplate result;
            lock (_templatesLock)
                if (_templates.TryGetValue(messageTemplate, out result))
                    return result;

            result = _innerParser.Parse(messageTemplate);

            lock (_templatesLock)
            {
                // Exceeding MaxCacheItems is *not* the sunny day scenario; all we're doing here is preventing out-of-memory
                // conditions when the library is used incorrectly. Correct use (templates, rather than
                // direct message strings) should barely, if ever, overflow this cache.

                if (_templates.Count <= MaxCacheItems)
                    _templates[messageTemplate] = result;
            }

            return result;
        }
    }
}
