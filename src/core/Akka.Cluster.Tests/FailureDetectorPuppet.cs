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
            _status.Value = Status.Down;
        }

        public void MarkNodeAsAvailable()
        {
            _status.Value = Status.Up;
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
