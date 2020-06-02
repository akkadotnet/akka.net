using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Util;

// ARTERY: Incomplete implementation
namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// marker trait for protobuf-serializable artery messages
    /// </summary>
    internal interface IArteryMessage
    { 
        // Empty
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker trait for reply messages
    /// </summary>
    internal interface IReply : IControlMessage
    {
        // Empty
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker trait for control messages that can be sent via the system message sub-channel
    /// but don't need full reliable delivery. E.g. `HandshakeReq` and `Reply`.
    /// </summary>
    internal interface IControlMessage : IArteryMessage
    {
        // Empty
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Quarantined : IControlMessage
    {
        public UniqueAddress From { get; }
        public UniqueAddress To { get; }

        public Quarantined(UniqueAddress from, UniqueAddress to)
        {
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ActorSystemTerminating : IControlMessage
    {
        public UniqueAddress From { get; }

        public ActorSystemTerminating(UniqueAddress from)
        {
            From = from;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ActorSystemTerminatingAck : IControlMessage
    {
        public UniqueAddress From { get; }

        public ActorSystemTerminatingAck(UniqueAddress from)
        {
            From = from;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class InboundControlJunction : 
        GraphStageWithMaterializedValue<FlowShape<IInboundEnvelope, IInboundEnvelope>, IControlMessageSubject>
    {
        #region Classes
        /// <summary>
        /// Observer subject for inbound control messages.
        /// Interested observers can attach themselves to the
        /// subject to get notification of incoming control
        /// messages.
        /// </summary>
        public interface IControlMessageSubject
        {
            Task<Done> Attach(IControlMessageObserver observer);
            void Detach(IControlMessageObserver observer);
        }

        public interface IControlMessageObserver
        {
            /// <summary>
            /// Notification of incoming control message. The message
            /// of the envelope is always a `ControlMessage`.
            /// </summary>
            /// <param name="inboundEnvelope"></param>
            void Notify(IInboundEnvelope inboundEnvelope);
            void ControlSubjectCompleted(Try<Done> signal);
        }

        public interface ICallbackMessage { }

        public sealed class Attach : ICallbackMessage
        {
            public IControlMessageObserver Observer { get; }
            public TaskCompletionSource<Done> Done { get; }

            public Attach(IControlMessageObserver observer, TaskCompletionSource<Done> done)
            {
                Observer = observer;
                Done = done;
            }
        }

        public sealed class Detach : ICallbackMessage
        {
            public IControlMessageObserver Observer { get; }

            public Detach(IControlMessageObserver observer)
            {
                Observer = observer;
            }
        }
        #endregion

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        public InboundControlJunction()
        {
            var inlet = new Inlet<IInboundEnvelope>("InboundControlJunction.in");
            var outlet = new Outlet<IInboundEnvelope>("InboundControlJunction.out");
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(inlet, outlet);
        }

        internal sealed class Logic : InAndOutGraphStageLogic, IControlMessageSubject
        {
            private ImmutableList<IControlMessageObserver> _observers = ImmutableList<IControlMessageObserver>.Empty;

            // IInHandler
            public override void OnPush()
            {
                throw new NotImplementedException();
            }

            // IOutHandler
            public override void OnPull()
            {
                throw new NotImplementedException();
            }

            // IControlMessageSubject
            public Task<Done> Attach(IControlMessageObserver observer)
            {
                throw new NotImplementedException();
            }

            public void Detach(IControlMessageObserver observer)
            {
                throw new NotImplementedException();
            }

        }

        public override ILogicAndMaterializedValue<IControlMessageSubject> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new GraphStageLogic(Shape)
        }
    }
}
