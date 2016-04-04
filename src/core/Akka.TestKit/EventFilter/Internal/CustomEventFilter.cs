//-----------------------------------------------------------------------
// <copyright file="CustomEventFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class CustomEventFilter : EventFilterBase
    {
        private readonly Predicate<LogEvent> _predicate;

        public CustomEventFilter(Predicate<LogEvent> predicate)
            : base(null, null)
        {
            _predicate = predicate;
        }

        protected override bool IsMatch(LogEvent evt)
        {
            return _predicate(evt);
        }

        protected override string FilterDescriptiveName { get { return "Custom"; } }
    }
}

