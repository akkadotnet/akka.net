//-----------------------------------------------------------------------
// <copyright file="LogEventExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    /// <summary>
    /// Extension methods for accessing semantic logging properties from <see cref="LogEvent"/> instances.
    /// These methods make it easy for custom logger implementations to extract structured properties.
    /// </summary>
    public static class LogEventExtensions
    {
        /// <summary>
        /// Attempts to extract structured properties from the log event message.
        /// </summary>
        /// <param name="evt">The log event</param>
        /// <param name="properties">The extracted properties dictionary (if successful)</param>
        /// <returns>True if properties were extracted, false if message is a pre-formatted string</returns>
        /// <example>
        /// <code>
        /// if (logEvent.TryGetProperties(out var properties))
        /// {
        ///     // Use structured properties with your native logger
        ///     foreach (var prop in properties)
        ///     {
        ///         Console.WriteLine($"{prop.Key} = {prop.Value}");
        ///     }
        /// }
        /// </code>
        /// </example>
        public static bool TryGetProperties(
            this LogEvent evt,
            out IReadOnlyDictionary<string, object>? properties)
        {
            var hasContext = !evt.ContextProperties.IsEmpty;

            if (evt.Message is LogMessage msg)
            {
                var messageProperties = msg.GetProperties();
                if (!hasContext)
                {
                    properties = messageProperties;
                    return true;
                }

                properties = MergeProperties(messageProperties, evt.ContextProperties);
                return true;
            }

            if (hasContext)
            {
                properties = MergeProperties(null, evt.ContextProperties);
                return true;
            }

            properties = null;
            return false;
        }

        private static IReadOnlyDictionary<string, object> MergeProperties(
            IReadOnlyDictionary<string, object>? messageProperties,
            LogContextProperties contextProperties)
        {
            var messageCount = messageProperties?.Count ?? 0;
            var contextCount = contextProperties.Count;
            var merged = new Dictionary<string, object>(messageCount + contextCount);

            if (messageProperties != null)
            {
                foreach (var kvp in messageProperties)
                    merged[kvp.Key] = kvp.Value;
            }

            switch (contextCount)
            {
                case 1:
                {
                    var item0 = contextProperties[0];
                    merged[item0.Key] = item0.Value;
                    break;
                }
                case 2:
                {
                    var item0 = contextProperties[0];
                    merged[item0.Key] = item0.Value;
                    var item1 = contextProperties[1];
                    merged[item1.Key] = item1.Value;
                    break;
                }
                case 3:
                {
                    var item0 = contextProperties[0];
                    merged[item0.Key] = item0.Value;
                    var item1 = contextProperties[1];
                    merged[item1.Key] = item1.Value;
                    var item2 = contextProperties[2];
                    merged[item2.Key] = item2.Value;
                    break;
                }
                default:
                    for (var i = 0; i < contextCount; i++)
                    {
                        var contextProperty = contextProperties[i];
                        merged[contextProperty.Key] = contextProperty.Value;
                    }
                    break;
            }

            return merged;
        }

        /// <summary>
        /// Gets the property names from the log event's message template.
        /// Returns empty list if message is a pre-formatted string.
        /// </summary>
        /// <param name="evt">The log event</param>
        /// <returns>List of property names, or empty list</returns>
        /// <example>
        /// <code>
        /// var names = logEvent.GetPropertyNames();
        /// // For "User {UserId} logged in", returns ["UserId"]
        /// </code>
        /// </example>
        public static IReadOnlyList<string> GetPropertyNames(this LogEvent evt)
        {
            return evt.Message is LogMessage msg
                ? msg.PropertyNames
                : Array.Empty<string>();
        }

        /// <summary>
        /// Gets the message template format string from the log event.
        /// </summary>
        /// <param name="evt">The log event</param>
        /// <returns>Template string if LogMessage, otherwise the string representation</returns>
        /// <example>
        /// <code>
        /// var template = logEvent.GetTemplate();
        /// // For semantic logs, returns "User {UserId} logged in"
        /// // For pre-formatted strings, returns the actual message
        /// </code>
        /// </example>
        public static string GetTemplate(this LogEvent evt)
        {
            return evt.Message is LogMessage msg
                ? msg.Format
                : evt.Message?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Gets the parameter values from the log message.
        /// Returns empty enumerable if message is a pre-formatted string.
        /// </summary>
        /// <param name="evt">The log event</param>
        /// <returns>Parameter values, or empty enumerable</returns>
        /// <example>
        /// <code>
        /// var parameters = logEvent.GetParameters().ToArray();
        /// // For log.Info("User {0}", 123), returns [123]
        /// </code>
        /// </example>
        public static IEnumerable<object> GetParameters(this LogEvent evt)
        {
            return evt.Message is LogMessage msg
                ? msg.Parameters()
                : Enumerable.Empty<object>();
        }

    }
}
