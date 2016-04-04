//-----------------------------------------------------------------------
// <copyright file="WrappedTerminated.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

