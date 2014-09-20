using System;
using System.Threading;

namespace Akka.TestKit
{

    /// <summary>
    /// Wraps a <see cref="Barrier"/> for use in testing.
    /// It always uses a timeout when waiting.
    /// Timeouts will always throw an exception. The default timeout is 5 seconds.
    /// </summary>
    public class TestBarrier
    {
        private readonly TestKitBase _testKit;
        private readonly int _count;
        private readonly Barrier _barrier;

        #region Static members

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        #endregion

        public TestBarrier(TestKitBase testKit, int count)
        {
            _testKit = testKit;
            _count = count;
            _barrier = new Barrier(count);
        }

        public void Await()
        {
            Await(DefaultTimeout);
        }

        public void Await(TimeSpan timeout)
        {
            _barrier.SignalAndWait(_testKit.Dilated(timeout));

        }

        public void Reset()
        {
            _barrier.RemoveParticipants(_count);
            _barrier.AddParticipants(_count);
        }
    }
}
