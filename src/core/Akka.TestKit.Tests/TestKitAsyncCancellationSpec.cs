using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.TestKit
{
    /// <summary>
    /// Tests for GitHub issue #7743 - Excessive exception nesting when cancelling ExpectMsgAsync
    /// </summary>
    public class TestKitAsyncCancellationSpec : AkkaSpec
    {
        public TestKitAsyncCancellationSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "ExpectMsgAsync should not have excessive exception nesting when cancelled")]
        public async Task ExpectMsgAsync_Should_Not_Have_Excessive_Exception_Nesting_When_Cancelled()
        {
            var probe = CreateTestProbe();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception caughtException = null;
            try
            {
                await probe.ExpectMsgAsync<int>(cancellationToken: cts.Token);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
            
            // The key issue from #7743 is that we should NOT have AggregateException containing AggregateException
            // We should have at most one level of wrapping
            if (caughtException is AggregateException aggEx)
            {
                // The inner exception should NOT be another AggregateException
                Assert.IsNotType<AggregateException>(aggEx.InnerException);
                // It should be some form of OperationCanceledException
                Assert.IsAssignableFrom<OperationCanceledException>(aggEx.InnerException);
            }
            else
            {
                // Or it could be OperationCanceledException directly (which is fine)
                Assert.IsAssignableFrom<OperationCanceledException>(caughtException);
            }
        }

        [Fact(DisplayName = "ExpectMsgAsync accessed via Task.Exception should not have excessive nesting")]
        public async Task ExpectMsgAsync_Task_Exception_Should_Not_Have_Excessive_Nesting()
        {
            var probe = CreateTestProbe();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = probe.ExpectMsgAsync<int>(cancellationToken: cts.Token).AsTask();
            
            // Wait for the task to complete
            await Task.Delay(100);

            // The task should be either Canceled or Faulted
            Assert.True(task.IsCanceled || task.IsFaulted);
            
            if (task.IsFaulted)
            {
                Assert.NotNull(task.Exception);
                
                // The key issue: we should NOT have nested AggregateExceptions
                var aggEx = Assert.IsType<AggregateException>(task.Exception);
                
                // Verify no excessive nesting - inner should not be AggregateException
                Assert.IsNotType<AggregateException>(aggEx.InnerException);
                
                // It should be some form of OperationCanceledException
                Assert.IsAssignableFrom<OperationCanceledException>(aggEx.InnerException);
            }
            // If task.IsCanceled is true, Task.Exception will be null, which is also valid
        }

        [Fact(DisplayName = "Synchronous ExpectMsg with cancellation should not have excessive nesting")]
        public void ExpectMsg_Synchronous_Should_Not_Have_Excessive_Nesting()
        {
            var probe = CreateTestProbe();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception syncException = null;
            try
            {
                probe.ExpectMsg<int>(cancellationToken: cts.Token);
            }
            catch (Exception ex)
            {
                syncException = ex;
            }

            Assert.NotNull(syncException);

            // The key issue: verify no excessive nesting
            if (syncException is AggregateException aggEx)
            {
                // The inner exception should NOT be another AggregateException
                Assert.IsNotType<AggregateException>(aggEx.InnerException);
                // It should be some form of OperationCanceledException
                Assert.IsAssignableFrom<OperationCanceledException>(aggEx.InnerException);
            }
            else
            {
                // Or it could be OperationCanceledException directly
                Assert.IsAssignableFrom<OperationCanceledException>(syncException);
            }
        }

        [Fact(DisplayName = "Original bug report scenario from issue #7743")]
        public async Task Original_Bug_Report_Scenario_Should_Pass()
        {
            // This is the exact test from the bug report that was failing
            var probe = CreateTestProbe();

            using var stopper = new CancellationTokenSource();
            stopper.Cancel();

            var task = probe.ExpectMsgAsync<int>(
                cancellationToken: stopper.Token).AsTask();

            await Task.Delay(TimeSpan.FromSeconds(1)); // default timeout is 3 seconds

            // Original test expected double nesting - we're verifying this is fixed
            if (task.IsFaulted && task.Exception != null)
            {
                var outer = task.Exception;
                Assert.IsType<AggregateException>(outer);
                
                // The bug was that InnerException was ALSO an AggregateException
                // Now it should be OperationCanceledException directly
                Assert.IsNotType<AggregateException>(outer.InnerException);
                Assert.IsAssignableFrom<OperationCanceledException>(outer.InnerException);
            }
            // Task might also be in Canceled state, which is valid
        }
    }
}