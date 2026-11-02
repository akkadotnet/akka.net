//-----------------------------------------------------------------------
// <copyright file="DefaultLogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an <see cref="ILoggingAdapter"/> implementation that uses <see cref="string.Format(string,object[])"/> to format log messages.
    /// </summary>
    public class DefaultLogMessageFormatter : ILogMessageFormatter
    {
        public static readonly DefaultLogMessageFormatter Instance = new();
        private DefaultLogMessageFormatter(){}

        public string Format(string format, params object[] args)
        {
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return $"[INVALID LOG FORMAT] str=[{format}], args=[{string.Join(", ", args)}]. " +
                       "Please fix the format string in the logging call site.";
            }
        }

        public string Format(string format, IEnumerable<object> args)
        {
            var argsArray = args.ToArray();
            try
            {
                return string.Format(format, argsArray);
            }
            catch (FormatException)
            {
                return $"[INVALID LOG FORMAT] str=[{format}], args=[{string.Join(", ", argsArray)}]. " +
                       "Please fix the format string in the logging call site.";
            }
        }
    }
}
