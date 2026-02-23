//-----------------------------------------------------------------------
// <copyright file="TestKitBase_AwaitAssert.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.TestKit.Internal;
using Nito.AsyncEx.Synchronous;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract partial class TestKitBase
    {
        /// <summary>
        /// <para>Await until the given assertion does not throw an exception or the timeout
        /// expires, whichever comes first. If the timeout expires the last exception
        /// is thrown.</para>
        /// <para>The action is called, and if it throws an exception the thread sleeps
        /// the specified interval before retrying.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block.</para>
        /// <para>Note that the timeout is scaled using <see cref="Dilated" />,
        /// which uses the configuration entry "akka.test.timefactor".</para>
        /// </summary>
        /// <param name="assertion">The action.</param>
        /// <param name="duration">The timeout.</param>
        /// <param name="interval">The interval to wait between executing the assertion.</param>
        /// <param name="cancellationToken"></param>
        public void AwaitAssert(Action assertion, TimeSpan? duration=null, TimeSpan? interval=null, CancellationToken cancellationToken = default)
        {
            AwaitAssertAsync(assertion, duration, interval, cancellationToken)
                .WaitAndUnwrapException(cancellationToken);
        }
        
        /// <inheritdoc cref="AwaitAssert(Action, TimeSpan?, TimeSpan?, CancellationToken)"/>
        public async Task AwaitAssertAsync(Action assertion, TimeSpan? duration=null, TimeSpan? interval=null, CancellationToken cancellationToken = default)
        {
            var intervalValue = interval.GetValueOrDefault(TimeSpan.FromMilliseconds(100));
            if(intervalValue == Timeout.InfiniteTimeSpan) intervalValue = TimeSpan.MaxValue;
            intervalValue.EnsureIsPositiveFinite(nameof(interval));
            var max = RemainingOrDilated(duration);
            var stop = Now + max;
            var attempts = 0;
            var start = Now;
            while(true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;
                try
                {
                    // TODO: assertion can run forever, need a way to stop this if this happens.
                    assertion();
                    return;
                }
                catch(Exception)
                {
                    var stopped = Now;
                    if (stopped >= stop)
                    {
                        Sys.Log.Warning("AwaitAssert failed, timeout [{0}] is over after [{1}] attempts and [{2}] elapsed time", max, attempts, stopped - start);
                        throw;
                    }
                        
                }
                
                var t = (stop - Now).Min(intervalValue);
                await Task.Delay(t, cancellationToken);
            }
        }

        /// <summary>
        /// <para>Await until the given assertion does not throw an exception or the timeout
        /// expires, whichever comes first. If the timeout expires the last exception
        /// is thrown.</para>
        /// <para>The action is called, and if it throws an exception the thread sleeps
        /// the specified interval before retrying.</para>
        /// <para>If no timeout is given, take it from the innermost enclosing `within`
        /// block.</para>
        /// <para>Note that the timeout is scaled using <see cref="Dilated" />,
        /// which uses the configuration entry "akka.test.timefactor".</para>
        /// </summary>
        /// <param name="assertion">The action.</param>
        /// <param name="duration">The timeout.</param>
        /// <param name="interval">The interval to wait between executing the assertion.</param>
        /// <param name="cancellationToken"></param>
        public async Task AwaitAssertAsync(Func<Task> assertion, TimeSpan? duration=null, TimeSpan? interval=null, CancellationToken cancellationToken = default)
        {
            var intervalValue = interval.GetValueOrDefault(TimeSpan.FromMilliseconds(100));
            if(intervalValue == Timeout.InfiniteTimeSpan) intervalValue = TimeSpan.MaxValue;
            intervalValue.EnsureIsPositiveFinite("interval");
            var max = RemainingOrDilated(duration);
            var stop = Now + max;
            var attempts = 0;
            var start = Now;
            while(true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;
                try
                {
                    await assertion();
                    return;
                }
                catch(Exception)
                {
                    var stopped = Now;
                    if (stopped >= stop)
                    {
                        Sys.Log.Warning("AwaitAssert failed, timeout [{0}] is over after [{1}] attempts and [{2}] elapsed time", max, attempts, stopped - start);
                        throw;
                    }
                }
                
                var t = (stop - Now).Min(intervalValue);
                await Task.Delay(t, cancellationToken);
            }
        }
    }
}
