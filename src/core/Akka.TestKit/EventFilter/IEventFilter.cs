﻿//-----------------------------------------------------------------------
// <copyright file="IEventFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    public interface IEventFilter
    {
        bool Apply(LogEvent logEvent);
    }
}

