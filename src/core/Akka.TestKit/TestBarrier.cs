//-----------------------------------------------------------------------
// <copyright file="TestBarrier.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD 
        /// </summary>
        /// <param name="testKit">TBD</param>
        /// <param name="count">TBD</param>
        /// <param name="defaultTimeout">TBD</param>
        public TestBarrier(TestKitBase testKit, int count, TimeSpan? defaultTimeout=null)
        {
            _testKit = testKit;
            _count = count;
            _defaultTimeout = defaultTimeout.GetValueOrDefault(testKit.TestKitSettings.DefaultTimeout);
            _barrier = new Barrier(count);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Await()
        {
            Await(_defaultTimeout);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public void Await(TimeSpan timeout)
        {
            _barrier.SignalAndWait(_testKit.Dilated(timeout));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Reset()
        {
            _barrier.RemoveParticipants(_count);
            _barrier.AddParticipants(_count);
        }
    }
}
