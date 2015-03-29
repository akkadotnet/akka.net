using System;
using System.Threading;
using Akka.TestKit.Internal;

namespace Akka.TestKit
{
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
        public void AwaitAssert(Action assertion, TimeSpan? duration=null, TimeSpan? interval=null)
        {
            var intervalValue = interval.GetValueOrDefault(TimeSpan.FromMilliseconds(800));
            if(intervalValue == Timeout.InfiniteTimeSpan) intervalValue = TimeSpan.MaxValue;
            intervalValue.EnsureIsPositiveFinite("interval");
            var max = RemainingOrDilated(duration);
            var stop = Now + max;
            var t = max.Min(intervalValue);
            while(true)
            {
                try
                {
                    assertion();
                    return;
                }
                catch(Exception)
                {
                    if(Now + t >= stop)
                        throw;
                }
                Thread.Sleep(t);
                t = (stop - Now).Min(intervalValue);
            }
        }
    }
}