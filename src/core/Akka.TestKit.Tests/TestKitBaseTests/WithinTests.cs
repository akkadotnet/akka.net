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

        /// <summary>
        /// The block overruns the deadline and then fails. The failure must reach the test.
        /// </summary>
        /// <remarks>
        /// <c>max</c> is deliberately above 1.4 seconds. The implementation races the block against
        /// <c>max + 200ms</c>, and the default epsilon is <c>max(0.15 * max, 50ms)</c>, so for any max
        /// above ~1.4 seconds that 200ms of slack sits inside epsilon. The elapsed-time check therefore
        /// cannot report this overrun; only observing the block itself can.
        /// </remarks>
        [Fact(DisplayName = "WithinAsync should fail when the block overruns the deadline and then throws")]
        public async Task WithinAsync_should_not_swallow_failure_of_block_that_overran_the_deadline()
        {
            var max = 2.Seconds();

            // Released only after WithinAsync has returned control, so the block is guaranteed
            // to still be running when the deadline fires. It then throws, as the block in #8483 did.
            var release = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blockFailed = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await Awaiting(async () =>
                {
                    await WithinAsync(max, async () =>
                    {
                        await release.Task;
                        blockFailed.SetResult(Done.Instance);
                        throw new BlockFailedException("block failed after the deadline");
                    });
                }).Should().ThrowAsync<XunitException>();
            }
            finally
            {
                release.SetResult(Done.Instance);
            }

            // The block really did fail; the test above must not have passed silently.
            // Its Task is left faulted on purpose - that is the state #8483 discarded.
            await blockFailed.Task;
        }

        /// <summary>
        /// A block that is still running when the deadline fires must be reported, not skipped.
        /// </summary>
        [Fact(DisplayName = "WithinAsync should fail with a descriptive message when the block is still running at the deadline")]
        public async Task WithinAsync_should_fail_when_block_is_still_running_at_the_deadline()
        {
            var max = 2.Seconds();
            var release = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var exception = await Awaiting(async () =>
                {
                    await WithinAsync(max, async () =>
                    {
                        await release.Task;
                        return Done.Instance;
                    });
                }).Should().ThrowAsync<XunitException>();

                // The failure has to say what happened and name the deadline that was exceeded.
                exception.Which.Message.Should().Contain("still running");
                exception.Which.Message.Should().Contain(Dilated(max).ToString());
            }
            finally
            {
                release.SetResult(Done.Instance);
            }
        }

        /// <summary>
        /// The success path is unchanged: a block that finishes in time returns its result.
        /// </summary>
        [Fact(DisplayName = "WithinAsync should return the result of a block that completes in time")]
        public async Task WithinAsync_should_return_result_of_block_that_completes_in_time()
        {
            var result = await WithinAsync(2.Seconds(), async () =>
            {
                await Task.Yield();
                return 42;
            });

            result.Should().Be(42);
        }

        /// <summary>
        /// A block that fails before the deadline wins the race, so its exception arrives
        /// wrapped in an <see cref="AggregateException"/>. Pinned to keep that path unchanged.
        /// </summary>
        [Fact(DisplayName = "WithinAsync should surface an exception thrown by the block before the deadline")]
        public async Task WithinAsync_should_surface_exception_thrown_before_the_deadline()
        {
            var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            {
                await WithinAsync(5.Seconds(), async () =>
                {
                    await Task.Yield();
                    throw new BlockFailedException("block failed before the deadline");
                });
            });

            exception.InnerException.Should().BeOfType<BlockFailedException>();
        }

        /// <summary>
        /// The synchronous overload never had the swallow: the action runs inline inside the
        /// delegate invocation, so it throws before the deadline race exists. Pinned.
        /// </summary>
        [Fact(DisplayName = "Within should surface an exception thrown by a synchronous action")]
        public void Within_should_surface_exception_thrown_by_synchronous_action()
        {
            Assert.Throws<BlockFailedException>(() =>
            {
                // Exercises the synchronous overload on purpose - that is what this test pins.
                Within(2.Seconds(), () => { throw new BlockFailedException("synchronous block failed"); });
            });
        }

        private sealed class BlockFailedException : Exception
        {
            public BlockFailedException(string message) : base(message)
            {
            }
        }
    }
}
