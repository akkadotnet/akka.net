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
            Within(TimeSpan.FromSeconds(1), () => ExpectNoMsg(), TimeSpan.FromMilliseconds(500));
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
            // This test passes if:
            // 1. The test fails quickly due to the short WithinAsync timeout (expected)
            // 2. The timeoutOccurred flag remains false - meaning our short timeout was respected
            //
            // This test will fail if:
            // 1. The EventFilter ignores the WithinAsync timeout and uses its own longer default timeout
            // 2. The timeoutOccurred flag will be set to true in that case
            
            var testEvent = "test-event-" + Guid.NewGuid().ToString("N");
            var filter = EventFilter.Info(contains: testEvent);
            
            // Use a very short timeout for WithinAsync - something that would definitely
            // fail if the EventFilter is using its own longer default timeout
            var shortTimeout = 200.Milliseconds();
            
            // Create a custom timeout tracker for precise measurement
            var timeoutOccurred = false;
            var timerCts = new System.Threading.CancellationTokenSource();
            var timerTask = Task.Run(async () => {
                try 
                {
                    // Wait slightly longer than the short timeout
                    await Task.Delay(shortTimeout.Add(300.Milliseconds()), timerCts.Token);
                    // If we get here, the test is taking too long
                    timeoutOccurred = true;
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // This is expected if the test completes in time
                }
            });
            
            try
            {
                // This should fail quickly with the short timeout
                // The timeout error is wrapped in an AggregateException when using our fix
                await Assert.ThrowsAsync<AggregateException>(async () =>
                {
                    await WithinAsync(shortTimeout, async () =>
                    {
                        // This won't receive any messages and should inherit the short timeout
                        await filter.ExpectOneAsync(() => Task.CompletedTask);
                    });
                });
                
                // Cancel the timeout tracker since we've already completed
                timerCts.Cancel();
                await Task.WhenAny(timerTask);
                
                // Verify the test completed before our manual timeout
                Assert.False(timeoutOccurred, 
                    "The test took longer than expected. EventFilter likely did not inherit WithinAsync timeout.");
            }
            finally
            {
                timerCts.Cancel();
            }
        }
    }
}
