using System;
using System.Threading;
using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Countdown latch wrapper for use in testing.
    /// 
    /// It always uses a timeout when waiting and timeouts are specified as durations.
    /// There's a default timeout of 5 seconds and the default count is 1.
    /// Timeouts will always throw an exception.
    /// </summary>
    public class TestLatch
    {
        public TestLatch(ActorSystem system, int count = 1)
        {
            Count = count;
            System = system;
            _latch = new CountdownEvent(count);
        }

        public ActorSystem System { get; private set; }

        protected int Count;

        private CountdownEvent _latch;

        public bool IsOpen
        {
            get { return _latch.CurrentCount == 0; }
        }

        public void CountDown()
        {
            _latch.Signal();
        }

        public void Open()
        {
            while(!IsOpen) CountDown();
        }

        public void Reset()
        {
            _latch.Reset();
        }

        public void Ready(TimeSpan atMost)
        {
            if(atMost == TimeSpan.MaxValue) throw new ArgumentException(string.Format("TestLatch does not support waiting for {0}", atMost));
            var opened = _latch.Wait(atMost);
            if(!opened) throw new TimeoutException(
                string.Format("Timeout of {0}", atMost));
        }

        /// <summary>
        /// Uses the <see cref="DefaultTimeout"/>
        /// </summary>
        public void Ready()
        {
            Ready(DefaultTimeout);
        }

        public void Result(TimeSpan atMost)
        {
            Ready(atMost);
        }

        #region Static methods

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        public static TestLatch Apply(ActorSystem system, int count = 1)
        {
            return new TestLatch(system, count);
        }

        #endregion
    }
}
