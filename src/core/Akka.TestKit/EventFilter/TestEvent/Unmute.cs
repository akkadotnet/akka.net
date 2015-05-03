﻿//-----------------------------------------------------------------------
// <copyright file="Unmute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Internal;

namespace Akka.TestKit.TestEvent
{
    public sealed class Unmute : INoSerializationVerificationNeeded
    {
        private readonly IReadOnlyCollection<EventFilterBase> _filters;

        public Unmute(params EventFilterBase[] filters)
        {
            _filters = filters;
        }

        public Unmute(IReadOnlyCollection<EventFilterBase> filters)
        {
            _filters = filters;
        }

        public IReadOnlyCollection<EventFilterBase> Filters { get { return _filters; } }
    }
}

