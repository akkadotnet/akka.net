﻿//-----------------------------------------------------------------------
// <copyright file="DefaultLogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
            return string.Format(format, args);
        }

        public string Format(string format, IEnumerable<object> args)
        {
            return string.Format(format, args.ToArray());
        }
    }
}
