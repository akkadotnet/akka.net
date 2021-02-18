using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Utils;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API
    /// marker trait for protobuf-serializable artery messages
    /// </summary>
    internal interface IArteryMessage
    { }

    /// <summary>
    /// INTERNAL API
    /// Marker trait for control messages that can be sent via the system message sub-channel
    /// but don't need full reliable delivery. E.g. `HandshakeReq` and `Reply`.
    /// </summary>
    internal interface IControlMessage : IArteryMessage
    { }

    /// <summary>
    /// INTERNAL API
    /// Marker trait for reply messages
    /// </summary>
    internal interface IReply : IControlMessage
    { }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Quarantined : IControlMessage
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
    internal class ActorSystemTerminatingAck : IArteryMessage
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
        GraphStageWithMaterializedValue<
            FlowShape<IInboundEnvelope, IInboundEnvelope>, 
            InboundControlJunction.IControlMessageSubject>
    {
        /// <summary>
        /// Observer subject for inbound control messages.
        /// Interested observers can attach themselves to the
        /// subject to get notification of incoming control
        /// messages.
        /// </summary>
        internal interface IControlMessageSubject
        {
            TaskCompletionSource<Done> Attach(IControlMessageObserver observer);
            void Detach(IControlMessageObserver observer);
        }

        internal interface IControlMessageObserver
        {
            /// <summary>
            /// Notification of incoming control message.
            /// The message of the envelope is always a `ControlMessage`.
            /// </summary>
            /// <param name="inboundEnvelope"></param>
            void Notify(IInboundEnvelope inboundEnvelope);
            void ControlSubjectCompleted(Try<Done> signal);
        }

        // messages for the stream callback
        internal interface ICallbackMessage { }

        internal sealed class Attach : ICallbackMessage
        {
            public IControlMessageObserver Observer { get; }
            public TaskCompletionSource<Done> Done { get; }

            public Attach(IControlMessageObserver observer, TaskCompletionSource<Done> done)
            {
                Observer = observer;
                Done = done;
            }
        }

        internal sealed class Detach : ICallbackMessage
        {
            public IControlMessageObserver Observer { get; }

            public Detach(IControlMessageObserver observer)
            {
                Observer = observer;
            }
        }

        public Inlet<IInboundEnvelope> In { get; } = new Inlet<IInboundEnvelope>("InboundControlJunction.in");
        public Outlet<IInboundEnvelope> Out { get; } = new Outlet<IInboundEnvelope>("InboundControlJunction.out");

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape => new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);

        private sealed class Logic : InAndOutGraphStageLogic, IControlMessageSubject
        {
            private ImmutableList<IControlMessageObserver> _observers = ImmutableList<IControlMessageObserver>.Empty;
            private readonly Inlet<IInboundEnvelope> _in;
            private readonly Outlet<IInboundEnvelope> _out;
            private readonly Action<ICallbackMessage> _callback;

            public Logic(InboundControlJunction parent) : base(parent.Shape)
            {
                _in = parent.In;
                _out = parent.Out;

                SetHandler(_in, _out, this);

                _callback = GetAsyncCallback<ICallbackMessage>(msg =>
                {
                    switch (msg)
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
                foreach (var observer in _observers)
                    observer.ControlSubjectCompleted(new Try<Done>(Done.Instance));
                _observers = ImmutableList<IControlMessageObserver>.Empty;
            }

            public override void OnPush()
            {
                var inboundEnvelope = Grab(_in);
                switch (inboundEnvelope)
                {
                    case IInboundEnvelope env when env.Message is IControlMessage:
                        foreach (var observer in _observers)
                            observer.Notify(env);
                        Pull(_in);
                        break;
                    default:
                        Push(_out, inboundEnvelope);
                        break;
                }
            }

            public override void OnPull()
                => Pull(_in);

            public TaskCompletionSource<Done> Attach(IControlMessageObserver observer)
            {
                var tcs = new TaskCompletionSource<Done>();
                _callback.Invoke(new Attach(observer, tcs));
                return tcs;
            }

            public void Detach(IControlMessageObserver observer)
            {
                _callback.Invoke(new Detach(observer));
            }
        }

        public override ILogicAndMaterializedValue<IControlMessageSubject> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<IControlMessageSubject>(logic, logic);
        }
    }

    internal class OutboundControlJunction 
        : GraphStageWithMaterializedValue<
            FlowShape<IOutboundEnvelope, IOutboundEnvelope>, 
            OutboundControlJunction.IOutboundControlIngress>
    {
        internal interface IOutboundControlIngress
        {
            void SendControlMessage(IControlMessage message);
        }

        private readonly IOutboundContext _outboundContext;
        private readonly ObjectPool<ReusableOutboundEnvelope> _outboundEnvelopePool;

        public Inlet<IOutboundEnvelope> In = new Inlet<IOutboundEnvelope>("OutboundControlJunction.in");
        public Outlet<IOutboundEnvelope> Out = new Outlet<IOutboundEnvelope>("OutboundControlJunction.out");
        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape => new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(In, Out);

        public OutboundControlJunction(IOutboundContext outboundContext, ObjectPool<ReusableOutboundEnvelope> outboundEnvelopePool)
        {
            _outboundContext = outboundContext;
            _outboundEnvelopePool = outboundEnvelopePool;
        }

        private sealed class Logic : InAndOutGraphStageLogic, IOutboundControlIngress
        {
            private readonly Action<IControlMessage> _sendControlMessageCallback;
            private readonly Queue<IOutboundEnvelope> _buffer;

            private readonly Inlet<IOutboundEnvelope> _in;
            private readonly Outlet<IOutboundEnvelope> _out;
            private readonly ObjectPool<ReusableOutboundEnvelope> _outboundEnvelopePool;

            public Logic(OutboundControlJunction parent) : base(parent.Shape)
            {
                _in = parent.In;
                _out = parent.Out;
                _buffer = new Queue<IOutboundEnvelope>();
                _outboundEnvelopePool = parent._outboundEnvelopePool;

                SetHandler(parent.In, parent.Out, this);

                var maxControlMessageBufferSize = parent._outboundContext.Settings.Advanced.OutboundControlQueueSize;
                _sendControlMessageCallback = GetAsyncCallback<IControlMessage>(message =>
                {
                    if(_buffer.Count == 0 && IsAvailable(_out))
                        Push(_out, Wrap(message));
                    else if(_buffer.Count < maxControlMessageBufferSize)
                        _buffer.Enqueue(Wrap(message));
                    else // it's alright to drop control messages
                        Log.Debug($"Dropping control message [{Logging.MessageClassName(message)}] due to full buffer.");
                });
            }

            // InHandler
            public override void OnPush()
            {
                if (_buffer.Count == 0 && IsAvailable(_out))
                    Push(_out, Grab(_in));
                else
                    _buffer.Enqueue(Grab(_in));
            }

            // OutHandler
            public override void OnPull()
            {
                if (_buffer.Count == 0 && !HasBeenPulled(_in))
                    Pull(_in);
                else if (_buffer.Count != 0)
                    Push(_out, _buffer.Dequeue());
            }

            private IOutboundEnvelope Wrap(IControlMessage message)
                => _outboundEnvelopePool.Acquire().Init(OptionVal.None<RemoteActorRef>(), message, OptionVal.None<IActorRef>());

            public void SendControlMessage(IControlMessage message)
                => _sendControlMessageCallback.Invoke(message);

        }

        public override ILogicAndMaterializedValue<IOutboundControlIngress> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<IOutboundControlIngress>(logic, logic);
        }
    }
}
