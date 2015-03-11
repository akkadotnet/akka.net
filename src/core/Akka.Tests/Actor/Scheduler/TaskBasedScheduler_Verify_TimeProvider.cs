using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor.Scheduler
{
    // ReSharper disable once InconsistentNaming
    public class TaskBasedScheduler_Verify_TimeProvider
    {
        [Fact]
        public void Now_Should_be_accurate()
        {
            ITimeProvider timeProvider = new TaskBasedScheduler();
            Math.Abs((timeProvider.Now - DateTimeOffset.Now).TotalMilliseconds).ShouldBeLessThan(20);
        }
    }
}