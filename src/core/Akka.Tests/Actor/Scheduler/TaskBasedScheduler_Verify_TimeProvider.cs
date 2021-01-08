//-----------------------------------------------------------------------
// <copyright file="TaskBasedScheduler_Verify_TimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor.Scheduler
{
    // ReSharper disable once InconsistentNaming
    public class DefaultScheduler_Verify_TimeProvider
    {
        [Fact]
        public void Now_Should_be_accurate()
        {
            using (var sys = ActorSystem.Create("Foo"))
            {
                
                ITimeProvider timeProvider = new HashedWheelTimerScheduler(sys.Settings.Config, sys.Log);
                try
                {
                    Math.Abs((timeProvider.Now - DateTimeOffset.Now).TotalMilliseconds).ShouldBeLessThan(20);
                }
                finally
                {
                    timeProvider.AsInstanceOf<IDisposable>().Dispose();
                }
            }
        }
    }
}

