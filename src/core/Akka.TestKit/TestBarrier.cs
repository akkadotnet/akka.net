//-----------------------------------------------------------------------
// <copyright file="TestBarrier.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.TestKit
{
    /// <summary>
    /// Wraps a <see cref="Barrier"/> for use in testing.
    /// It always uses a timeout when waiting.
    /// Timeouts will always throw an exception. The default timeout is based on 
    /// TestKits default out, see <see cref="TestKitSettings.DefaultTimeout"/>.
    /// </summary>
    public class TestBarrier
    {
        private readonly TestKitBase _testKit;
        private readonly int _count;
        private readonly TimeSpan _defaultTimeout;
        private readonly Barrier _barrier;

        /// <summary>
        /// Obsolete. Use <see cref="TestKitSettings.DefaultTimeout"/> instead.
        /// </summary>
        [Obsolete("This field will be removed in future versions.")]
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);


        /// <summary>
        /// Creates a barrier with the specified number of participants and default wait timeout.
        /// </summary>
        /// <param name="testKit">The test kit used to dilate timeout values.</param>
        /// <param name="count">The number of participants that must wait at the barrier.</param>
        /// <param name="defaultTimeout">The default timeout for waits, or null to use the test kit default.</param>
        public TestBarrier(TestKitBase testKit, int count, [AutoDilate] TimeSpan? defaultTimeout=null)
        {
            _testKit = testKit;
            _count = count;
            _defaultTimeout = defaultTimeout.GetValueOrDefault(testKit.TestKitSettings.DefaultTimeout);
            _barrier = new Barrier(count);
        }

        /// <summary>
        /// Signals arrival at the barrier and waits using the configured default timeout.
        /// </summary>
        public void Await()
        {
            Await(_defaultTimeout);
        }

        /// <summary>
        /// Signals arrival at the barrier and waits until all participants arrive or the timeout elapses.
        /// </summary>
        /// <param name="timeout">The maximum wait duration before timeout dilation.</param>
        public void Await([AutoDilate] TimeSpan timeout)
        {
            _barrier.SignalAndWait(_testKit.Dilated(timeout));
        }

        /// <summary>
        /// Resets the barrier phase by removing and re-adding all configured participants.
        /// </summary>
        public void Reset()
        {
            _barrier.RemoveParticipants(_count);
            _barrier.AddParticipants(_count);
        }
    }
}
