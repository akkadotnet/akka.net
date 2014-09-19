using System;
using System.Threading;

namespace Akka.TestKit
{

    /// <summary>
    /// A barrier wrapper for use in testing.
    /// It always uses a timeout when waiting and timeouts are specified as <see cref="TimeSpan"/>s.
    /// Timeouts will always throw an exception. The default timeout is 5 seconds.
    /// </summary>
    public class TestBarrier
    {
        private readonly int _count;
        private readonly Barrier _barrier;

        #region Static members

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        #endregion

        public TestBarrier(int count)
        {
            _count = count;
            _barrier = new Barrier(count);
        }

        public void Await()
        {
            Await(DefaultTimeout);
        }

        public void Await(TimeSpan timeout)
        {
            _barrier.SignalAndWait(timeout);

        }

        public void Reset()
        {
            _barrier.RemoveParticipants(_count);
            _barrier.AddParticipants(_count);
        }
    }
}
