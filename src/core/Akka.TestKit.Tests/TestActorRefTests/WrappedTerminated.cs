//-----------------------------------------------------------------------
// <copyright file="WrappedTerminated.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WrappedTerminated
    {
        private readonly Terminated _terminated;

        public WrappedTerminated(Terminated terminated)
        {
            _terminated = terminated;
        }

        public Terminated Terminated { get { return _terminated; } }
    }
}

