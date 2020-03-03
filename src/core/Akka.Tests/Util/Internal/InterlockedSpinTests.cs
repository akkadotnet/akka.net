//-----------------------------------------------------------------------
// <copyright file="InterlockedSpinTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Util.Internal
{
    public class InterlockedSpinTests
    {
        [Fact]
        public void When_a_shared_variable_is_updated_on_another_thread_Then_the_update_method_is_rerun()
        {
            var sharedVariable = "";
            var hasEnteredUpdateMethod = new ManualResetEvent(false);
            var okToContinue = new ManualResetEvent(false);
            var numberOfCallsToUpdateWhenSignaled = 0;

            //This is what we want to test:
            //  sharedVariable = ""
            //  Fork to two threads:
            //  THREAD 1                               THREAD 2
            //                                         Call InterlockedSpin.Swap
            //                                             It calls updateWhenSignaled(0)
            //                                                Signal thread 1 it can do it's work, and wait for it
            //  set sharedVariable = "-"
            //  signal thread 2 it can continue
            //  and wait for it.
            //                                                 return "updated" from updateWhenSignaled
            //                                             Interlocked.CompareExchange will update sharedVariable to "updated" if it still is ""
            //                                               which it isn't so it will fail. It will then loop.
            //                                             Call updateWhenSignaled("-")
            //                                                 return "updated" from updateWhenSignaled
            //                                             Interlocked.CompareExchange will update sharedVariable to "updated" if it still is "-"
            //                                               which it is, and we return.
            //  Test that sharedVariable="updated"												 
            //  Test that updateWhenSignaled was called twice

            Func<string, string> updateWhenSignaled = i =>
            {
                numberOfCallsToUpdateWhenSignaled++;
                hasEnteredUpdateMethod.Set();	//Signal THREAD 1 to update sharedVariable
                okToContinue.WaitOne(TimeSpan.FromSeconds(2));	//Wait for THREAD 1
                return "updated";
            };
            var task = Task.Run(() => InterlockedSpin.Swap(ref sharedVariable, updateWhenSignaled));
            hasEnteredUpdateMethod.WaitOne(TimeSpan.FromSeconds(2)); //Wait for THREAD 2 to enter updateWhenSignaled
            sharedVariable = "-";
            okToContinue.Set();	//Signal THREAD 1 it can continue in updateWhenSignaled
            task.Wait(TimeSpan.FromSeconds(2));	//Wait for THREAD 1

            sharedVariable.ShouldBe("updated");
            numberOfCallsToUpdateWhenSignaled.ShouldBe(2);
        }
        [Fact]
        public void When_a_shared_variable_is_updated_on_another_thread_Then_the_update_method_is_rerun_using_tuples()
        {
            var sharedVariable = "";
            var hasEnteredUpdateMethod = new ManualResetEvent(false);
            var okToContinue = new ManualResetEvent(false);
            var numberOfCallsToUpdateWhenSignaled = 0;

            //This is what we want to test:
            //  sharedVariable = ""
            //  Fork to two threads:
            //  THREAD 1                               THREAD 2
            //                                         Call InterlockedSpin.Swap
            //                                             It calls updateWhenSignaled(0)
            //                                                Signal thread 1 it can do it's work, and wait for it
            //  set sharedVariable = "-"
            //  signal thread 2 it can continue
            //  and wait for it.
            //                                                 return "updated" from updateWhenSignaled
            //                                             Interlocked.CompareExchange will update sharedVariable to "updated" if it still is ""
            //                                               which it isn't so it will fail. It will then loop.
            //                                             Call updateWhenSignaled("-")
            //                                                 return "updated" from updateWhenSignaled
            //                                             Interlocked.CompareExchange will update sharedVariable to "updated" if it still is "-"
            //                                               which it is, and we return.
            //  Test that sharedVariable="updated"												 
            //  Test that updateWhenSignaled was called twice

            Func<string, (bool, string, string)> updateWhenSignaled = i =>
            {
                numberOfCallsToUpdateWhenSignaled++;
                hasEnteredUpdateMethod.Set();	//Signal THREAD 1 to update sharedVariable
                okToContinue.WaitOne(TimeSpan.FromSeconds(2));	//Wait for THREAD 1
                return (true, "updated", "returnValue");
            };
            string result;
            var task = Task.Run(() => { result= InterlockedSpin.ConditionallySwap(ref sharedVariable, updateWhenSignaled); });
            hasEnteredUpdateMethod.WaitOne(TimeSpan.FromSeconds(2)); //Wait for THREAD 2 to enter updateWhenSignaled
            sharedVariable = "-";
            okToContinue.Set();	//Signal THREAD 1 it can continue in updateWhenSignaled
            task.Wait(TimeSpan.FromSeconds(2));	//Wait for THREAD 1

            sharedVariable.ShouldBe("updated");
            numberOfCallsToUpdateWhenSignaled.ShouldBe(2);
        }

        [Fact]
        public void When_a_shared_variable_is_updated_on_another_thread_Then_the_update_method_is_rerun_but_as_the_break_condition_is_fulfilled_it_do_not_update()
        {
            var sharedVariable = "";
            var hasEnteredUpdateMethod = new ManualResetEvent(false);
            var okToContinue = new ManualResetEvent(false);
            var numberOfCallsToUpdateWhenSignaled = 0;

            //This is what we want to test:
            //  sharedVariable = ""
            //  Fork to two threads:
            //  THREAD 1                               THREAD 2
            //                                         Call InterlockedSpin.Swap
            //                                             It calls updateWhenSignaled(0)
            //                                                Signal thread 1 it can do it's work, and wait for it
            //  set sharedVariable = "-"
            //  signal thread 2 it can continue
            //  and wait for it.
            //                                                 Since value=="" we do not want to break
            //                                                 return <false,"updated","update"> from updateWhenSignaled
            //                                             Interlocked.CompareExchange will update sharedVariable to "updated" if it still is ""
            //                                               which it isn't so it will fail. It will then loop.
            //                                             Call updateWhenSignaled("-")
            //                                                 Since value!="" we want to break
            //                                                 return <true,"updated","break"> from updateWhenSignaled
            //                                             Since first item in tuple==true, we break and return item 3: "break"
            //  Test that sharedVariable="-"												 
            //  Test that updateWhenSignaled was called twice
            //  Test that return from updateWhenSignaled is "break"

            Func<string, (bool, string, string)> updateWhenSignaled = s =>
            {
                numberOfCallsToUpdateWhenSignaled++;
                hasEnteredUpdateMethod.Set();	//Signal to start-thread that we have entered the update method (it will chang
                okToContinue.WaitOne(TimeSpan.FromSeconds(2));	//Wait to be signalled
                var shouldUpdate = s == "";
                return (shouldUpdate, "updated", shouldUpdate ? "update" : "break");
            };
            string result = "";
            var task = Task.Run(() => { result = InterlockedSpin.ConditionallySwap(ref sharedVariable, updateWhenSignaled); });
            hasEnteredUpdateMethod.WaitOne(TimeSpan.FromSeconds(2));
            sharedVariable = "-";
            okToContinue.Set();
            task.Wait(TimeSpan.FromSeconds(2));

            sharedVariable.ShouldBe("-");
            numberOfCallsToUpdateWhenSignaled.ShouldBe(2);
            result.ShouldBe("break");
        }

    }


}

