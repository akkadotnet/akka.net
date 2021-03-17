using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Utils;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery
{
    internal static class TestManagementCommands
    {
        /// <summary>
        /// INTERNAL API
        /// </summary>
        public sealed class FailInboundStreamOnce
        {
            public FailInboundStreamOnce(Exception exception)
            {
                Exception = exception;
            }

            public Exception Exception { get; }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Thread safe mutable state that is shared among the test operators.
    /// </summary>
    internal class SharedTestState
    {
        private readonly AtomicReference<TestState> _state =
            new AtomicReference<TestState>(
                new TestState(
                    ImmutableDictionary<Address, ImmutableHashSet<Address>>.Empty,
                Option<Exception>.None));

        public bool AnyBlackholePresent() => _state.Value.Blackholes.Count > 0;

        public bool IsBlackhole(Address from, Address to)
        {
            if (_state.Value.Blackholes.TryGetValue(from, out var destinations))
            {
                return destinations.Contains(to);
            }
            return false;
        }

        /// <summary>
        /// Enable blackholing between given address in given direction
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="direction"></param>
        public void Blackhole(Address a, Address b, ThrottleTransportAdapter.Direction direction)
        {
            switch (direction)
            {
                case ThrottleTransportAdapter.Direction.Send:
                    AddBlackhole(a, b);
                    break;
                case ThrottleTransportAdapter.Direction.Receive:
                    AddBlackhole(b, a);
                    break;
                case ThrottleTransportAdapter.Direction.Both:
                    AddBlackhole(a, b);
                    AddBlackhole(b, a);
                    break;
            }
        }

        /// <summary>
        /// Cause the inbound stream to fail with the given exception.
        /// Can be used to test inbound stream restart / recovery.
        /// </summary>
        /// <param name="ex"></param>
        public void FailInboundStreamOnce(Exception ex)
        {
            while (true)
            {
                var current = _state.Value;
                if (_state.CompareAndSet(current, current.Copy(failInboundStream: ex))) 
                    break;
            }
        }

        /// <summary>
        /// Get the exception to fail the inbound stream with and immediately reset the state to not-failed.
        /// This is used to simulate a single failure on the stream, where a successful restart recovers operations.
        /// </summary>
        /// <returns></returns>
        public Option<Exception> GetInboundFailureOnce()
        {
            while (true)
            {
                var current = _state.Value;
                if (_state.CompareAndSet(current, current.Copy(failInboundStream: Option<Exception>.None))) 
                    return current.FailInboundStream;
            }
        }

        private void AddBlackhole(Address from, Address to)
        {
            while (true)
            {
                var current = _state.Value;

                TestState newState;
                if (current.Blackholes.TryGetValue(from, out var destinations))
                {
                    newState = current.Copy(blackholes: current.Blackholes.AddOrSet(from, destinations.Add(to)));
                }
                else
                {
                    newState = current.Copy(blackholes: current.Blackholes.AddOrSet(from, new[] { to }.ToImmutableHashSet()));
                }

                if (_state.CompareAndSet(current, newState)) break;
            }
        }

        public void PassThrough(Address a, Address b, ThrottleTransportAdapter.Direction direction)
        {
            switch (direction)
            {
                case ThrottleTransportAdapter.Direction.Send:
                    RemoveBlackhole(a, b);
                    break;
                case ThrottleTransportAdapter.Direction.Receive:
                    RemoveBlackhole(b, a);
                    break;
                case ThrottleTransportAdapter.Direction.Both:
                    RemoveBlackhole(a, b);
                    RemoveBlackhole(b, a);
                    break;
            }
        }

        public void RemoveBlackhole(Address from, Address to)
        {
            while (true)
            {
                var current = _state.Value;
                TestState newState;
                if (current.Blackholes.TryGetValue(from, out var destinations))
                {
                    newState = current.Copy(blackholes: current.Blackholes.AddOrSet(from, destinations.Remove(to)));
                }
                else
                {
                    newState = current;
                }

                if (_state.CompareAndSet(current, newState)) break;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class TestState
    {
        public TestState(ImmutableDictionary<Address, ImmutableHashSet<Address>> blackholes, Option<Exception> failInboundStream)
        {
            Blackholes = blackholes;
            FailInboundStream = failInboundStream;
        }

        public ImmutableDictionary<Address, ImmutableHashSet<Address>> Blackholes { get; }
        public Option<Exception> FailInboundStream { get; }

        public TestState Copy(
            ImmutableDictionary<Address, ImmutableHashSet<Address>> blackholes = null,
            Option<Exception>? failInboundStream = null)
            => new TestState(blackholes ?? Blackholes, failInboundStream ?? FailInboundStream);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class OutboundTestStage : GraphStage<FlowShape<IOutboundEnvelope, IOutboundEnvelope>>
    {
        #region Logic
        
        internal class Logic : TimerGraphStageLogic, IInHandler, IOutHandler, IStageLogging
        {
            private readonly Inlet<IOutboundEnvelope> _in;
            private readonly Outlet<IOutboundEnvelope> _out;
            private readonly SharedTestState _state;
            private readonly IOutboundContext _outboundContext;

            public Logic(OutboundTestStage stage) : base(stage.Shape)
            {
                _in = stage._in;
                _out = stage._out;
                _state = stage._state;
                _outboundContext = stage._outboundContext;

                SetHandler(_in, this);
                SetHandler(_out, this);
            }

            protected internal override void OnTimer(object timerKey) { }

            public void OnPush()
            {
                var env = Grab(_in);
                if (_state.IsBlackhole(_outboundContext.LocalAddress.Address, _outboundContext.RemoteAddress))
                {
                    Log.Debug("dropping outbound message [{0}] to [{1}] because of blackhole",
                        Logging.MessageClassName(env.Message),
                        _outboundContext.RemoteAddress);
                    Pull(_in);
                }
                else
                {
                    Push(_out, env);
                }
            }

            public void OnPull() => Pull(_in);

            public void OnUpstreamFinish() => GraphInterpreter.Current.ActiveStage.CompleteStage();

            public void OnUpstreamFailure(Exception e) => GraphInterpreter.Current.ActiveStage.FailStage(e);

            public void OnDownstreamFinish() => GraphInterpreter.Current.ActiveStage.CompleteStage();

        }

        #endregion

        private readonly IOutboundContext _outboundContext;
        private readonly SharedTestState _state;

        private readonly Inlet<IOutboundEnvelope> _in = new Inlet<IOutboundEnvelope>("OutboundTestStage.in");
        private readonly Outlet<IOutboundEnvelope> _out = new Outlet<IOutboundEnvelope>("OutboundTestStage.out");
        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape { get; }

        public OutboundTestStage(IOutboundContext outboundContext, SharedTestState state)
        {
            _outboundContext = outboundContext;
            _state = state;
            Shape = new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(_in, _out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class InboundTestStage : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        #region Logic
        
        internal class Logic : TimerGraphStageLogic, IInHandler, IOutHandler, IStageLogging
        {
            private readonly Inlet<IInboundEnvelope> _in;
            private readonly Outlet<IInboundEnvelope> _out;
            private readonly SharedTestState _state;
            private readonly IInboundContext _inboundContext;

            public Logic(InboundTestStage stage) : base(stage.Shape)
            {
                _in = stage._in;
                _out = stage._out;
                _state = stage._state;
                _inboundContext = stage._inboundContext;

                SetHandler(_in, this);
                SetHandler(_out, this);
            }

            protected internal override void OnTimer(object timerKey) { }

            public void OnPush()
            {
                var shouldFailEx = _state.GetInboundFailureOnce();
                if (shouldFailEx.HasValue)
                {
                    Log.Info("Fail inbound stream from [{0}]: {1}", nameof(InboundTestStage), shouldFailEx.Value.Message);
                    FailStage(shouldFailEx.Value);
                }
                else
                {
                    var env = Grab(_in);
                    switch (env.Association)
                    {
                        case None<IOutboundContext> _:
                            // unknown, handshake not completed
                            if (_state.AnyBlackholePresent())
                                Log.Debug(
                                    "inbound message [{0}] before handshake completed, cannot check if remote is blackholed, letting through",
                                    Logging.MessageClassName(env.Message));
                            Push(_out, env);
                            break;

                        case Some<IOutboundContext> association:
                            if (_state.IsBlackhole(_inboundContext.LocalAddress.Address, association.Get.RemoteAddress))
                            {
                                Log.Debug(
                                    "dropping inbound message [{}] from [{}] with UID [{}] because of blackhole",
                                    Logging.MessageClassName(env.Message),
                                    association.Get.RemoteAddress,
                                    env.OriginUid);
                                Pull(_in); // drop message
                            }
                            else
                            {
                                Push(_out, env);
                            }
                            break;
                    }
                }
            }

            public void OnPull() => Pull(_in);

            public void OnUpstreamFinish() => GraphInterpreter.Current.ActiveStage.CompleteStage();

            public void OnUpstreamFailure(Exception e) => GraphInterpreter.Current.ActiveStage.FailStage(e);

            public void OnDownstreamFinish() => GraphInterpreter.Current.ActiveStage.CompleteStage();
        }

        #endregion

        private readonly IInboundContext _inboundContext;
        private readonly SharedTestState _state;

        private readonly Inlet<IInboundEnvelope> _in = new Inlet<IInboundEnvelope>("InboundTestStage.in");
        private readonly Outlet<IInboundEnvelope> _out = new Outlet<IInboundEnvelope>("InboundTestStage.out");
        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        public InboundTestStage(IInboundContext inboundContext, SharedTestState state)
        {
            _inboundContext = inboundContext;
            _state = state;

            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(_in, _out);

        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            throw new NotImplementedException();
        }
    }
}
