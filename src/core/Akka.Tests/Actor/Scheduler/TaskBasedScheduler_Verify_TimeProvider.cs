//-----------------------------------------------------------------------
// <copyright file="DedicatedThreadScheduler_Verify_TimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor.Scheduler
{
    // ReSharper disable once InconsistentNaming
    /*TODO: this class is not used*/public class DedicatedThreadScheduler_Verify_TimeProvider
    {
        [Fact]
        public void Now_Should_be_accurate()
        {
            using (var sys = ActorSystem.Create("Foo"))
            {
                ITimeProvider timeProvider = new DedicatedThreadScheduler(sys);
                Math.Abs((timeProvider.Now - DateTimeOffset.Now).TotalMilliseconds).ShouldBeLessThan(20);
            }
        }
    }
}

