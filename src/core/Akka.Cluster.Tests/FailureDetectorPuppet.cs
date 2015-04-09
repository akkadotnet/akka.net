//-----------------------------------------------------------------------
// <copyright file="FailureDetectorPuppet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
using Akka.Util;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// User controllable "puppet" failure detector.
    /// </summary>
    public class FailureDetectorPuppet : FailureDetector
    {
        public FailureDetectorPuppet(Config config, EventStream ev)
        {
        }

        public enum Status
        {
            Up,
            Down,
            Unknown
        }

        readonly AtomicReference<Status> _status = new AtomicReference<Status>(Status.Unknown);

        public void MarkNodeAsUnavailable()
        {
            var oldStatus = _status.Value;
            bool set;
            do
            {
                set = _status.CompareAndSet(oldStatus, Status.Down);
            } while (!set);

        }

        public void MarkNodeAsAvailable()
        {
            var oldStatus = _status.Value;
            bool set;
            do
            {
                set = _status.CompareAndSet(oldStatus, Status.Up);
            } while (!set);
        }

        public override bool IsAvailable
        {
            get
            {
                var status = _status.Value;
                return status == Status.Up || status == Status.Unknown;
            }
        }

        public override bool IsMonitoring
        {
            get { return _status.Value != Status.Unknown; }
        }

        public override void HeartBeat()
        {
            _status.CompareAndSet(Status.Unknown, Status.Up);
        }
    }
}

