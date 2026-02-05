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
        public static string FormatContextSegments(IReadOnlyList<KeyValuePair<string, object>> context)
        {
            if (context == null || context.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(context.Count * 8);
            foreach (var property in context)
            {
                sb.Append('[').Append(property.Key);
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
    }
}
