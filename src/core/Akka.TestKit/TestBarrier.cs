//-----------------------------------------------------------------------
// <copyright file="TestBarrier.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        [Obsolete("This field will be removed in future versions.")]
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);


       
        public TestBarrier(TestKitBase testKit, int count, TimeSpan? defaultTimeout=null)
        {
            _testKit = testKit;
            _count = count;
            _defaultTimeout = defaultTimeout.GetValueOrDefault(testKit.TestKitSettings.DefaultTimeout);
            _barrier = new Barrier(count);
        }

        public void Await()
        {
            Await(_defaultTimeout);
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
