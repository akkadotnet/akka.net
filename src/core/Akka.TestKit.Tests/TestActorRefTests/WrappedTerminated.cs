﻿//-----------------------------------------------------------------------
// <copyright file="WrappedTerminated.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WrappedTerminated
    {
        public WrappedTerminated(Terminated terminated)
        {
            Terminated = terminated;
        }

        public Terminated Terminated { get; }
    }
}

