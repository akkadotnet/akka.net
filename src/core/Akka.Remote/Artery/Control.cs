using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

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
    /// Observer subject for inbound control messages.
    /// Interested observers can attach themselves to the
    /// subject to get notification of incoming control
    /// messages.
    /// </summary>
    internal interface IControlMessageSubject
    {
        Task<Done> Attach(IControlMessageObserver observer);
        void Detach(IControlMessageObserver observer);
    }

    internal interface IControlMessageObserver
    {
        /// <summary>
        /// Notification of incoming control message. The message
        /// of the envelope is always a `ControlMessage`.
        /// </summary>
        /// <param name="inboundEnvelope"></param>
        void Notify(IInboundEnvelope inboundEnvelope);
        void ControlSubjectCompleted(Try<Done> signal);
    }


    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class InboundControlJunction : 
        GraphStageWithMaterializedValue<FlowShape<IInboundEnvelope, IInboundEnvelope>, IControlMessageSubject>
    {
        #region Classes
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

        #region Logic
        internal sealed class Logic : InAndOutGraphStageLogic, IControlMessageSubject
        {
            private readonly InboundControlJunction _stage;
            private ImmutableList<IControlMessageObserver> _observers = ImmutableList<IControlMessageObserver>.Empty;
            private readonly Action<ICallbackMessage> _callback;

            public Logic(InboundControlJunction stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, stage.Outlet, this);

                _callback = GetAsyncCallback<ICallbackMessage>(msg =>
                {
                    switch(msg)
                    {
                        case Attach a:
                            _observers = _observers.Add(a.Observer);
                            a.Done.SetResult(Done.Instance);
                            break;
                        case Detach d:
                            _observers = _observers.Remove(d.Observer);
                            break;
                    }
                });
            }

            public override void PostStop()
            {
                foreach(var observer in _observers)
                    observer.ControlSubjectCompleted(new Try<Done>(Done.Instance));

                _observers = ImmutableList<IControlMessageObserver>.Empty;
            }

            // IInHandler
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override void OnPush()
            {
                var envelope = Grab(_stage.Inlet);
                switch (envelope)
                {
                    case IInboundEnvelope env when env.Message is IControlMessage:
                        foreach (var observer in _observers)
                            observer.Notify(env);
                        Pull(_stage.Inlet);
                        break;
                    default:
                        Push(_stage.Outlet, envelope);
                        break;
                }
            }

            // IOutHandler
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override void OnPull() => Pull(_stage.Inlet);

            // IControlMessageSubject
            // ARTERY: This port implementation is suspect, I don't think this is right.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Task<Done> Attach(IControlMessageObserver observer)
            {
                var p = TaskEx.NonBlockingTaskCompletionSource<Done>();
                _callback.Invoke(new Attach(observer, p));
                return p.Task;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Detach(IControlMessageObserver observer) => _callback.Invoke(new Detach(observer));
        }
        #endregion

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }
        public Inlet<IInboundEnvelope> Inlet { get; } = new Inlet<IInboundEnvelope>("InboundControlJunction.in");
        public Outlet<IInboundEnvelope> Outlet { get; } = new Outlet<IInboundEnvelope>("InboundControlJunction.out");

        public InboundControlJunction()
        {
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(Inlet, Outlet);
        }

        // ARTERY: This port implementation is suspect, I don't think this is right.
        public override ILogicAndMaterializedValue<IControlMessageSubject> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Logic>(logic, logic);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IOutboundControlIngress
    {
        void SendControlMessage(IControlMessage message);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class OutboundControlJunction :
        GraphStageWithMaterializedValue<FlowShape<IOutboundEnvelope, IOutboundEnvelope>, IOutboundControlIngress>
    {
        public IOutboundContext OutboundContext { get; }
        public ObjectPool<ReusableOutboundEnvelope> OutboundEnvelopePool { get; }
        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape { get; }
        public Inlet<IOutboundEnvelope> Inlet { get; } = new Inlet<IOutboundEnvelope>("OutboundControlJunction.in");
        public Outlet<IOutboundEnvelope> Outlet { get; } = new Outlet<IOutboundEnvelope>("OutboundControlJunction.in");


    }
}
