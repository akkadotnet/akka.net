//-----------------------------------------------------------------------
// <copyright file="RetrySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.TestKit;
using Xunit;
using static Akka.Pattern.RetrySupport;

namespace Akka.Tests.Pattern
{
    public class RetrySpec : AkkaSpec
    {
        [Fact]
        public Task Pattern_Retry_must_run_a_successful_task_immediately()
        {
            var retried = Retry(() => Task.FromResult(5), 5, TimeSpan.FromSeconds(1), Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var remaining = await retried;
                Assert.Equal(5, remaining);
            });
        }

        [Fact]
        public Task Pattern_Retry_must_run_a_successful_task_only_once()
        {
            var counter = 0;
            var retried = Retry(() =>
            {
                counter++;
                return Task.FromResult(counter);
            }, 5, TimeSpan.FromSeconds(1), Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var remaining = await retried;
                Assert.Equal(1, remaining);
            });
        }

        [Fact]
        public Task Pattern_Retry_must_eventually_return_a_failure_for_a_task_that_will_never_succeed()
        {
            var retried = Retry(() => Task.FromException<int>(new InvalidOperationException("Mexico")), 5, TimeSpan.FromMilliseconds(100), Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => retried);
                Assert.Equal("Mexico", exception.Message);
            });
        }

        [Fact]
        public Task Pattern_Retry_must_return_a_success_for_a_task_that_succeeds_eventually()
        {
            var failCount = 0;

            Task<int> Attempt()
            {
                if (failCount < 5)
                {
                    failCount += 1;
                    return Task.FromException<int>(new InvalidOperationException(failCount.ToString()));
                }
                else
                {
                    return Task.FromResult(5);
                }
            }

            var retried = Retry(() => Attempt(), 10, TimeSpan.FromMilliseconds(100), Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var remaining = await retried;
                Assert.Equal(5, remaining);
            });
        }

        [Fact]
        public Task Pattern_Retry_must_return_a_failure_for_a_task_that_would_have_succeeded_but_retries_were_exhausted()
        {
            var failCount = 0;

            Task<int> Attempt()
            {
                if (failCount < 10)
                {
                    failCount += 1;
                    return Task.FromException<int>(new InvalidOperationException(failCount.ToString()));
                }
                else
                {
                    return Task.FromResult(5);
                }
            }

            var retried = Retry(() => Attempt(), 5, TimeSpan.FromMilliseconds(100), Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => retried);
                Assert.Equal("6", exception.Message);
            });
        }

        [Fact]
        public Task Pattern_Retry_must_return_a_failure_for_a_task_that_would_have_succeeded_but_retries_were_exhausted_with_delay_function()
        {
            var failCount = 0;
            var attemptedCount = 0;

            Task<int> Attempt()
            {
                if (failCount < 10)
                {
                    failCount += 1;
                    return Task.FromException<int>(new InvalidOperationException(failCount.ToString()));
                }
                else
                {
                    return Task.FromResult(5);
                }
            }

            var retried = Retry(() => Attempt(), 5, attempted =>
            {
                attemptedCount = attempted;
                return TimeSpan.FromMilliseconds(100 + attempted);
            }, Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => retried);
                Assert.Equal("6", exception.Message);
                Assert.Equal(5, attemptedCount);
            });
        }

        [Fact]
        public Task Pattern_Retry_can_be_attempted_without_any_delay()
        {
            var failCount = 0;

            Task<int> Attempt()
            {
                if (failCount < 1000)
                {
                    failCount += 1;
                    return Task.FromException<int>(new InvalidOperationException(failCount.ToString()));
                }
                else
                {
                    return Task.FromResult(1);
                }
            }

            var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var retried = Retry(() => Attempt(), 999);

            return WithinAsync(TimeSpan.FromSeconds(1), async () =>
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => retried);
                Assert.Equal("1000", exception.Message);

                var elapse = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - start;
                Assert.True(elapse <= 100);
            });
        }

        [Fact]
        public Task Pattern_Retry_must_handle_thrown_exceptions_in_same_way_as_failed_task()
        {
            var failCount = 0;

            Task<int> Attempt()
            {
                if (failCount < 5)
                {
                    failCount += 1;
                    return Task.FromException<int>(new InvalidOperationException(failCount.ToString()));
                }
                else
                {
                    return Task.FromResult(5);
                }
            }

            var retried = Retry(() => Attempt(), 10, TimeSpan.FromMilliseconds(100), Sys.Scheduler);

            return WithinAsync(TimeSpan.FromSeconds(3), async () =>
            {
                var remaining = await retried;
                Assert.Equal(5, remaining);
            });
        }
    }
}
