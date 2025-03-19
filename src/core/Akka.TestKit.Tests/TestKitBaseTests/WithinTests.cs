//-----------------------------------------------------------------------
// <copyright file="WithinTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class WithinTests : AkkaSpec
    {
        [Fact]
        public void Within_should_increase_max_timeout_by_the_provided_epsilon_value()
        {
            Within(TimeSpan.FromSeconds(1), () => ExpectNoMsg(), TimeSpan.FromMilliseconds(50));
        }

        [Fact]
        public void Within_should_respect_minimum_time()
        {
            Within(0.3.Seconds(), 1.Seconds(), () => ExpectNoMsg(0.4.Seconds()), "", 0.1.Seconds());
        }
        
        [Fact]
        public async Task WithinAsync_should_respect_minimum_time()
        {
            await WithinAsync(
                0.3.Seconds(),
                1.Seconds(),
                async () => await ExpectNoMsgAsync(0.4.Seconds()), 
                "", 
                0.1.Seconds());
        }
        
        [Fact]
        public void Within_should_throw_if_execution_is_shorter_than_minimum_time()
        {
            Invoking(() =>
            {
                Within(0.5.Seconds(), 1.Seconds(), () => ExpectNoMsg(0.1.Seconds()), null, 0.1.Seconds());
            }).Should().Throw<XunitException>();
        }
        
        [Fact]
        public async Task WithinAsync_should_throw_if_execution_is_shorter_than_minimum_time()
        {
            await Awaiting(async () =>
            {
                await WithinAsync(
                    0.5.Seconds(),
                    1.Seconds(),
                    async () => await ExpectNoMsgAsync(0.1.Seconds()),
                    null,
                    0.1.Seconds());
            }).Should().ThrowAsync<XunitException>();
        }

        [Fact]
        public async Task WithinAsync_timeout_should_propagate_to_EventFilter()
        {
            // Create a test event filter with a relatively long default timeout
            var testEvent = "test-event-" + Guid.NewGuid().ToString("N");
            var filter = EventFilter.Info(contains: testEvent);
            
            // Use a short WithinAsync timeout (250ms)
            var shortTimeout = 250.Milliseconds();
            var longTimeout = 10.Seconds();
            
            // Set up timing - EventFilter's default timeout would be much longer than our WithinAsync timeout
            var task = WithinAsync(shortTimeout, async () =>
            {
                // This should timeout quickly (inheriting the short timeout from WithinAsync)
                // rather than waiting for the default EventFilter timeout
                await filter.ExpectOneAsync(async () =>
                {
                    // We never log the expected message, so this should time out
                    await Task.Delay(100);
                });
            });
            
            // Measure the time it takes - should be close to the short timeout, not the long one
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Awaiting(() => task).Should().ThrowAsync<Exception>();
            stopwatch.Stop();
            
            // The failure should happen within a timeframe closer to the short timeout
            // Add some buffer for test execution overhead
            stopwatch.ElapsedMilliseconds.Should().BeLessThan((long)shortTimeout.TotalMilliseconds + 500);
        }
    }
}
