// <copyright file="LoggingContextFormatting.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Event
{
    internal static class LoggingContextFormatting
    {
        public static string CombinePrefix(string existingPrefix, string nextPrefix)
        {
            if (string.IsNullOrEmpty(existingPrefix))
                return nextPrefix;

            if (string.IsNullOrEmpty(nextPrefix))
                return existingPrefix;

            return string.Concat(existingPrefix, ": ", nextPrefix);
        }

        public static string ApplyPrefix(string prefix, string format)
        {
            if (string.IsNullOrEmpty(prefix))
                return format;

            var escapedPrefix = EscapeBraces(prefix);
            return string.Concat(escapedPrefix, ": ", format);
        }

        public static string FormatContextSegments(IReadOnlyList<LogContextProperty> context)
        {
            if (context == null || context.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(context.Count * 8);
            foreach (var property in context)
            {
                sb.Append('[').Append(property.Name);
                if (property.Value != null)
                {
                    sb.Append('=').Append(property.Value);
                }
                else
                {
                    sb.Append("=null");
                }

                sb.Append(']');
            }

            return sb.ToString();
        }

        private static string EscapeBraces(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.IndexOf('{') == -1 && value.IndexOf('}') == -1)
                return value;

            var result = new StringBuilder(value.Length + 8);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '{':
                        result.Append("{{");
                        break;
                    case '}':
                        result.Append("}}");
                        break;
                    default:
                        result.Append(ch);
                        break;
                }
            }

            return result.ToString();
        }
    }
}
