using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{

    internal class ClusterHeartbeatSender : UntypedActor
    {
        public ClusterHeartbeatSender()
        {
            throw new NotImplementedException();
        }

        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }

        #region Messaging classes

        /// <summary>
        /// Sent at regular intervals for failure detection
        /// </summary>
        internal sealed class Heartbeat : IClusterMessage
        {
            public Heartbeat(Address @from)
            {
                From = @from;
            }

            public Address From { get; private set; }

#pragma warning disable 659 //there might very well be multiple heartbeats from the same address. overriding GetHashCode may have uninteded side effects
            public override bool Equals(object obj)
#pragma warning restore 659
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Heartbeat && Equals((Heartbeat)obj);
            }

            private bool Equals(Heartbeat other)
            {
                return Equals(From, other.From);
            }
        }

        /// <summary>
        /// Sends replies to <see cref="Heartbeat"/> messages
        /// </summary>
        internal sealed class HeartbeatRsp : IClusterMessage
        {
            public HeartbeatRsp(UniqueAddress @from)
            {
                From = @from;
            }

            public UniqueAddress From { get; private set; }

#pragma warning disable 659 //there might very well be multiple heartbeats from the same address. overriding GetHashCode may have uninteded side effects
            public override bool Equals(object obj)
#pragma warning restore 659
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is HeartbeatRsp && Equals((HeartbeatRsp)obj);
            }

            private bool Equals(HeartbeatRsp other)
            {
                return Equals(From, other.From);
            }
        }

        /// <summary>
        /// Sent to self only
        /// </summary>
        private class HeartbeatTick { }

        internal sealed class ExpectedFirstHeartbeat
        {
            public ExpectedFirstHeartbeat(UniqueAddress @from)
            {
                From = @from;
            }

            public UniqueAddress From { get; private set; }
        }

        #endregion
    }

    class ClusterHeartbeatReceiver : UntypedActor, IActorLogging
    {
        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }

        public LoggingAdapter Log { get; private set; }
    }
}
