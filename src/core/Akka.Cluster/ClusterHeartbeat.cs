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
