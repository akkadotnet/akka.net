//-----------------------------------------------------------------------
// <copyright file="ActorGraphInterpreter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Stage;
using Reactive.Streams;
using static Akka.Streams.Implementation.Fusing.GraphInterpreter;

// ReSharper disable MemberHidesStaticFromOuterClass
namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class GraphModule : AtomicModule
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IModule[] MaterializedValueIds;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly GraphAssembly Assembly;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="assembly">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="materializedValueIds">TBD</param>
        public GraphModule(GraphAssembly assembly, Shape shape, Attributes attributes, IModule[] materializedValueIds)
        {
            Assembly = assembly;
            Shape = shape;
            Attributes = attributes;
            MaterializedValueIds = materializedValueIds;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes) => new GraphModule(Assembly, Shape, attributes, MaterializedValueIds);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes.None, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="newShape">TBD</param>
        /// <returns>TBD</returns>
        public override IModule ReplaceShape(Shape newShape) =>
            !newShape.Equals(Shape) ? (IModule)CompositeModule.Create(this, newShape) : this;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "GraphModule\n" +
                                             $"  {Assembly.ToString().Replace("\n", "\n  ")}\n" +
                                             $"  shape={Shape}, attributes={Attributes}\n" +
                                             $"  MaterializedValueIds={string.Join<IModule>("\n   ", MaterializedValueIds)}";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class GraphInterpreterShell
    {
        private readonly GraphAssembly _assembly;
        private readonly Connection[] _connections;
        private readonly GraphStageLogic[] _logics;
        private readonly Shape _shape;
        private readonly ActorMaterializerSettings _settings;
        /// <summary>
        /// TBD
        /// </summary>
        internal readonly ExtendedActorMaterializer Materializer;

        /// <summary>
        /// Limits the number of events processed by the interpreter before scheduling
        /// a self-message for fairness with other actors. The basic assumption here is
        /// to give each input buffer slot a chance to run through the whole pipeline
        /// and back (for the elements).
        /// 
        /// Considered use case:
        ///  - assume a composite Sink of one expand and one fold 
        ///  - assume an infinitely fast source of data
        ///  - assume maxInputBufferSize == 1
        ///  - if the event limit is greater than maxInputBufferSize * (ins + outs) than there will always be expand activity
        ///  because no data can enter "fast enough" from the outside
        /// </summary>
        private readonly int _shellEventLimit;

        // Limits the number of events processed by the interpreter on an abort event.
        private readonly int _abortLimit;
        private readonly ActorGraphInterpreter.BatchingActorInputBoundary[] _inputs;
        private readonly ActorGraphInterpreter.IActorOutputBoundary[] _outputs;

        private ILoggingAdapter _log;
        private GraphInterpreter _interpreter;
        private int _subscribersPending;
        private int _publishersPending;
        private bool _resumeScheduled;
        private bool _waitingForShutdown;
        private Action<object> _enqueueToShortCircuit;
        private bool _interpreterCompleted;
        private readonly ActorGraphInterpreter.Resume _resume;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="assembly">TBD</param>
        /// <param name="connections">TBD</param>
        /// <param name="logics">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="settings">TbD</param>
        /// <param name="materializer">TBD</param>
        public GraphInterpreterShell(GraphAssembly assembly, Connection[] connections, GraphStageLogic[] logics, Shape shape, ActorMaterializerSettings settings, ExtendedActorMaterializer materializer)
        {
            _assembly = assembly;
            _connections = connections;
            _logics = logics;
            _shape = shape;
            _settings = settings;
            Materializer = materializer;

            _inputs = new ActorGraphInterpreter.BatchingActorInputBoundary[shape.Inlets.Count()];
            _outputs = new ActorGraphInterpreter.IActorOutputBoundary[shape.Outlets.Count()];
            _subscribersPending = _inputs.Length;
            _publishersPending = _outputs.Length;
            _shellEventLimit = settings.MaxInputBufferSize * (assembly.Inlets.Length + assembly.Outlets.Length);
            _abortLimit = _shellEventLimit * 2;

            _resume = new ActorGraphInterpreter.Resume(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsInitialized => Self != null;
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsTerminated => _interpreterCompleted && CanShutdown;
        /// <summary>
        /// TBD
        /// </summary>
        public bool CanShutdown => _subscribersPending + _publishersPending == 0;
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Self { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public ILoggingAdapter Log => _log ?? (_log = GetLogger());
        /// <summary>
        /// TBD
        /// </summary>
        public GraphInterpreter Interpreter => _interpreter ?? (_interpreter = GetInterpreter());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="subMat">TBD</param>
        /// <param name="enqueueToShourtCircuit">TBD</param>
        /// <param name="eventLimit">TBD</param>
        /// <returns>TBD</returns>
        public int Init(IActorRef self, SubFusingActorMaterializerImpl subMat, Action<object> enqueueToShourtCircuit, int eventLimit)
        {
            Self = self;
            _enqueueToShortCircuit = enqueueToShourtCircuit;

            for (int i = 0; i < _inputs.Length; i++)
            {
                var input = new ActorGraphInterpreter.BatchingActorInputBoundary(_settings.MaxInputBufferSize, i);
                _inputs[i] = input;
                Interpreter.AttachUpstreamBoundary(_connections[i], input);
            }

            var offset = _assembly.ConnectionCount - _outputs.Length;
            for (int i = 0; i < _outputs.Length; i++)
            {
                var outputType = _shape.Outlets[i].GetType().GetGenericArguments().First();
                var output = (ActorGraphInterpreter.IActorOutputBoundary) typeof(ActorGraphInterpreter.ActorOutputBoundary<>).Instantiate(outputType, Self, this, i);
                _outputs[i] = output;
                Interpreter.AttachDownstreamBoundary(_connections[i + offset], (DownstreamBoundaryStageLogic) output);
            }

            Interpreter.Init(subMat);
            return RunBatch(eventLimit);
        }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        /// <param name="eventLimit">TBD</param>
        /// <returns>TBD</returns>
        public int Receive(ActorGraphInterpreter.IBoundaryEvent e, int eventLimit)
        {
            _resumeScheduled = false;

            if (_waitingForShutdown)
            {
                switch (e)
                {
                    case ActorGraphInterpreter.ExposedPublisher exposedPublisher:
                        _outputs[exposedPublisher.Id].ExposedPublisher(exposedPublisher.Publisher);
                        _publishersPending--;
                        if (CanShutdown)
                            _interpreterCompleted = true;
                        break;

                    case ActorGraphInterpreter.OnSubscribe onSubscribe:
                        ReactiveStreamsCompliance.TryCancel(onSubscribe.Subscription);
                        _subscribersPending--;
                        if (CanShutdown)
                            _interpreterCompleted = true;
                        break;

                    case ActorGraphInterpreter.Abort _:
                        TryAbort(new TimeoutException(
                            $"Streaming actor has been already stopped processing (normally), but not all of its inputs or outputs have been subscribed in [{_settings.SubscriptionTimeoutSettings.Timeout}]. Aborting actor now."));
                        break;
                }
                return eventLimit;
            }

            // Cases that are most likely on the hot path, in decreasing order of frequency
            switch (e)
            {
                case ActorGraphInterpreter.OnNext onNext:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  OnNext {onNext.Event} id={onNext.Id}");
                    _inputs[onNext.Id].OnNext(onNext.Event);
                    return RunBatch(eventLimit);

                case ActorGraphInterpreter.RequestMore requestMore:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  Request {requestMore.Demand} id={requestMore.Id}");
                    _outputs[requestMore.Id].RequestMore(requestMore.Demand);
                    return RunBatch(eventLimit);

                case ActorGraphInterpreter.Resume _:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  Resume");
                    if (Interpreter.IsSuspended)
                        return RunBatch(eventLimit);
                    return eventLimit;

                case ActorGraphInterpreter.AsyncInput asyncInput:
                    Interpreter.RunAsyncInput(asyncInput.Logic, asyncInput.Event, asyncInput.Handler);
                    if (eventLimit == 1 && _interpreter.IsSuspended)
                    {
                        SendResume(true);
                        return 0;
                    }
                    return RunBatch(eventLimit - 1);

                case ActorGraphInterpreter.OnError onError:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  OnError id={onError.Id}");
                    _inputs[onError.Id].OnError(onError.Cause);
                    return RunBatch(eventLimit);

                case ActorGraphInterpreter.OnComplete onComplete:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  OnComplete id={onComplete.Id}");
                    _inputs[onComplete.Id].OnComplete();
                    return RunBatch(eventLimit);

                case ActorGraphInterpreter.OnSubscribe onSubscribe:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  OnSubscribe id={onSubscribe.Id}");
                    _subscribersPending--;
                    _inputs[onSubscribe.Id].OnSubscribe(onSubscribe.Subscription);
                    return RunBatch(eventLimit);

                case ActorGraphInterpreter.Cancel cancel:
                    if (IsDebug) Console.WriteLine($"{Interpreter.Name}  Cancel id={cancel.Id}");
                    _outputs[cancel.Id].Cancel();
                    return RunBatch(eventLimit);

                case ActorGraphInterpreter.SubscribePending subscribePending:
                    _outputs[subscribePending.Id].SubscribePending();
                    return eventLimit;

                case ActorGraphInterpreter.ExposedPublisher exposedPublisher:
                    _publishersPending--;
                    _outputs[exposedPublisher.Id].ExposedPublisher(exposedPublisher.Publisher);
                    return eventLimit;
            }

            return eventLimit;
        }
#pragma warning restore CS0162

        /**
         * Attempts to abort execution, by first propagating the reason given until either
         *  - the interpreter successfully finishes
         *  - the event limit is reached
         *  - a new error is encountered
         */
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        public void TryAbort(Exception reason)
        {
            var ex = reason is ISpecViolation
                ? new IllegalStateException("Shutting down because of violation of the Reactive Streams specification",
                    reason)
                : reason;

            // This should handle termination while interpreter is running. If the upstream have been closed already this
            // call has no effect and therefore does the right thing: nothing.
            try
            {
                foreach (var input in _inputs)
                    input.OnInternalError(ex);

                Interpreter.Execute(_abortLimit);
                Interpreter.Finish();
            }
            catch (Exception) { /* swallow? */ }
            finally
            {
                _interpreterCompleted = true;
                // Will only have an effect if the above call to the interpreter failed to emit a proper failure to the downstream
                // otherwise this will have no effect
                foreach (var output in _outputs)
                    output.Fail(ex);
                foreach (var input in _inputs)
                    input.Cancel();
            }
        }

        private int RunBatch(int actorEventLimit)
        {
            try
            {
                var usingShellLimit = _shellEventLimit < actorEventLimit;
                var remainingQuota = _interpreter.Execute(Math.Min(actorEventLimit, _shellEventLimit));

                if (Interpreter.IsCompleted)
                {
                    // Cannot stop right away if not completely subscribed
                    if (CanShutdown)
                        _interpreterCompleted = true;
                    else
                    {
                        _waitingForShutdown = true;
                        Materializer.ScheduleOnce(_settings.SubscriptionTimeoutSettings.Timeout,
                            () => Self.Tell(new ActorGraphInterpreter.Abort(this)));
                    }
                }
                else if (Interpreter.IsSuspended && !_resumeScheduled)
                    SendResume(!usingShellLimit);

                return usingShellLimit ? actorEventLimit - _shellEventLimit + remainingQuota : remainingQuota;
            }
            catch (Exception reason)
            {
                TryAbort(reason);
                return actorEventLimit - 1;
            }
        }

        private void SendResume(bool sendResume)
        {
            _resumeScheduled = true;
            if (sendResume)
                Self.Tell(_resume);
            else
                _enqueueToShortCircuit(_resume);
        }

        private GraphInterpreter GetInterpreter()
        {
            return new GraphInterpreter(_assembly, Materializer, Log, _logics, _connections,
                (logic, @event, handler) =>
                {
                    var asyncInput = new ActorGraphInterpreter.AsyncInput(this, logic, @event, handler);
                    var currentInterpreter = CurrentInterpreterOrNull;
                    if (currentInterpreter == null || !Equals(currentInterpreter.Context, Self))
                        Self.Tell(new ActorGraphInterpreter.AsyncInput(this, logic, @event, handler));
                    else
                        _enqueueToShortCircuit(asyncInput);
                }, _settings.IsFuzzingMode, Self);
        }

        private BusLogging GetLogger()
        {
            return new BusLogging(Materializer.System.EventStream, Self.ToString(), typeof(GraphInterpreterShell), new DefaultLogMessageFormatter());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"GraphInterpreterShell\n  {_assembly.ToString().Replace("\n", "\n  ")}";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public class ActorGraphInterpreter : ActorBase
    {
        #region messages

        /// <summary>
        /// TBD
        /// </summary>
        public interface IBoundaryEvent : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct OnError : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Exception Cause;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            /// <param name="cause">TBD</param>
            public OnError(GraphInterpreterShell shell, int id, Exception cause)
            {
                Shell = shell;
                Id = id;
                Cause = cause;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct OnComplete : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public OnComplete(GraphInterpreterShell shell, int id)
            {
                Shell = shell;
                Id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct OnNext : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly object Event;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            /// <param name="event">TBD</param>
            public OnNext(GraphInterpreterShell shell, int id, object @event)
            {
                Shell = shell;
                Id = id;
                Event = @event;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct OnSubscribe : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ISubscription Subscription;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            /// <param name="subscription">TBD</param>
            public OnSubscribe(GraphInterpreterShell shell, int id, ISubscription subscription)
            {
                Shell = shell;
                Id = id;
                Subscription = subscription;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct RequestMore : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly long Demand;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            /// <param name="demand">TBD</param>
            public RequestMore(GraphInterpreterShell shell, int id, long demand)
            {
                Shell = shell;
                Id = id;
                Demand = demand;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct Cancel : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public Cancel(GraphInterpreterShell shell, int id)
            {
                Shell = shell;
                Id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct SubscribePending : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public SubscribePending(GraphInterpreterShell shell, int id)
            {
                Shell = shell;
                Id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct ExposedPublisher : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorPublisher Publisher;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            /// <param name="publisher">TBD</param>
            public ExposedPublisher(GraphInterpreterShell shell, int id, IActorPublisher publisher)
            {
                Shell = shell;
                Id = id;
                Publisher = publisher;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct AsyncInput : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly GraphStageLogic Logic;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly object Event;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Action<object> Handler;
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            /// <param name="logic">TBD</param>
            /// <param name="event">TBD</param>
            /// <param name="handler">TBD</param>
            /// <returns>TBD</returns>
            public AsyncInput(GraphInterpreterShell shell, GraphStageLogic logic, object @event, Action<object> handler)
            {
                Shell = shell;
                Logic = logic;
                Event = @event;
                Handler = handler;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct Resume : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            public Resume(GraphInterpreterShell shell) => Shell = shell;

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public struct Abort : IBoundaryEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shell">TBD</param>
            public Abort(GraphInterpreterShell shell) => Shell = shell;

            /// <summary>
            /// TBD
            /// </summary>
            public GraphInterpreterShell Shell { get; }
        }

        private class ShellRegistered
        {
            public static readonly ShellRegistered Instance = new ShellRegistered();
            private ShellRegistered()
            {
            }
        }
        #endregion

        #region internal classes

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        public sealed class BoundaryPublisher<T> : ActorPublisher<T>
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="parent">TBD</param>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public BoundaryPublisher(IActorRef parent, GraphInterpreterShell shell, int id) : base(parent)
            {
                _wakeUpMessage = new SubscribePending(shell, id);
            }

            private readonly SubscribePending _wakeUpMessage;
            /// <summary>
            /// TBD
            /// </summary>
            protected override object WakeUpMessage => _wakeUpMessage;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class BoundarySubscription : ISubscription
        {
            private readonly IActorRef _parent;
            private readonly GraphInterpreterShell _shell;
            private readonly int _id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="parent">TBD</param>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public BoundarySubscription(IActorRef parent, GraphInterpreterShell shell, int id)
            {
                _parent = parent;
                _shell = shell;
                _id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="elements">TBD</param>
            public void Request(long elements) => _parent.Tell(new RequestMore(_shell, _id, elements));

            /// <summary>
            /// TBD
            /// </summary>
            public void Cancel() => _parent.Tell(new Cancel(_shell, _id));

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString() => $"BoundarySubscription[{_parent}, {_id}]";
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        public sealed class BoundarySubscriber<T> : ISubscriber<T>
        {
            private readonly IActorRef _parent;
            private readonly GraphInterpreterShell _shell;
            private readonly int _id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="parent">TBD</param>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public BoundarySubscriber(IActorRef parent, GraphInterpreterShell shell, int id)
            {
                _parent = parent;
                _shell = shell;
                _id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="subscription">TBD</param>
            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                _parent.Tell(new OnSubscribe(_shell, _id, subscription));
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                _parent.Tell(new OnError(_shell, _id, cause));
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void OnComplete() => _parent.Tell(new OnComplete(_shell, _id));

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
            public void OnNext(T element)
            {
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                _parent.Tell(new OnNext(_shell, _id, element));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class BatchingActorInputBoundary : UpstreamBoundaryStageLogic
        {
            #region OutHandler
            private sealed class OutHandler : Stage.OutHandler
            {
                private readonly BatchingActorInputBoundary _that;

                public OutHandler(BatchingActorInputBoundary that) => _that = that;

                public override void OnPull()
                {
                    var elementsCount = _that._inputBufferElements;
                    var upstreamCompleted = _that._upstreamCompleted;
                    if (elementsCount > 1) _that.Push(_that._outlet, _that.Dequeue());
                    else if (elementsCount == 1)
                    {
                        if (upstreamCompleted)
                        {
                            _that.Push(_that._outlet, _that.Dequeue());
                            _that.Complete(_that._outlet);
                        }
                        else _that.Push(_that._outlet, _that.Dequeue());
                    }
                    else if (upstreamCompleted) _that.Complete(_that._outlet);
                }

                public override void OnDownstreamFinish() => _that.Cancel();

                public override string ToString() => _that.ToString();
            }
            #endregion

            private readonly int _size;
            private readonly int _id;

            private readonly object[] _inputBuffer;
            private readonly int _indexMask;

            private ISubscription _upstream;
            private int _inputBufferElements;
            private int _nextInputElementCursor;
            private bool _upstreamCompleted;
            private bool _downstreamCanceled;
            private readonly int _requestBatchSize;
            private int _batchRemaining;
            private readonly Outlet<object> _outlet;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="size">TBD</param>
            /// <param name="id">TBD</param>
            /// <exception cref="ArgumentException">TBD</exception>
            public BatchingActorInputBoundary(int size, int id)
            {
                if (size <= 0) throw new ArgumentException("Buffer size cannot be zero", nameof(size));
                if ((size & (size - 1)) != 0) throw new ArgumentException("Buffer size must be power of two", nameof(size));

                _size = size;
                _id = id;
                _inputBuffer = new object[size];
                _indexMask = size - 1;
                _requestBatchSize = Math.Max(1, _inputBuffer.Length/2);
                _batchRemaining = _requestBatchSize;
                _outlet = new Outlet<object>("UpstreamBoundary" + id) { Id = 0 };

                SetHandler(_outlet, new OutHandler(this));
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override Outlet Out => _outlet;

            // Call this when an error happens that does not come from the usual onError channel
            // (exceptions while calling RS interfaces, abrupt termination etc)
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="reason">TBD</param>
            public void OnInternalError(Exception reason)
            {
                if (!(_upstreamCompleted || _downstreamCanceled) && !ReferenceEquals(_upstream, null))
                    _upstream.Cancel();

                if (!IsClosed(_outlet))
                    OnError(reason);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="reason">TBD</param>
            public void OnError(Exception reason)
            {
                if (!_upstreamCompleted || !_downstreamCanceled)
                {
                    _upstreamCompleted = true;
                    Clear();
                    Fail(_outlet, reason);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void OnComplete()
            {
                if (!_upstreamCompleted)
                {
                    _upstreamCompleted = true;
                    if (_inputBufferElements == 0)
                        Complete(_outlet);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="subscription">TBD</param>
            /// <exception cref="ArgumentException">TBD</exception>
            public void OnSubscribe(ISubscription subscription)
            {
                if (subscription == null) throw new ArgumentException("Subscription cannot be null");
                if (_upstreamCompleted) ReactiveStreamsCompliance.TryCancel(subscription);
                else if (_downstreamCanceled)
                {
                    _upstreamCompleted = true;
                    ReactiveStreamsCompliance.TryCancel(subscription);
                }
                else
                {
                    _upstream = subscription;
                    // prefetch
                    ReactiveStreamsCompliance.TryRequest(_upstream, _inputBuffer.Length);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
            /// <exception cref="IllegalStateException">TBD</exception>
            public void OnNext(object element)
            {
                if (!_upstreamCompleted)
                {
                    if (_inputBufferElements == _size)
                        throw new IllegalStateException("Input buffer overrun");
                    _inputBuffer[(_nextInputElementCursor + _inputBufferElements) & _indexMask] = element;
                    _inputBufferElements++;
                    if (IsAvailable(_outlet))
                        Push(_outlet, Dequeue());
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void Cancel()
            {
                _downstreamCanceled = true;
                if (!_upstreamCompleted)
                {
                    _upstreamCompleted = true;
                    if (!ReferenceEquals(_upstream, null))
                        ReactiveStreamsCompliance.TryCancel(_upstream);
                    Clear();
                }
            }

            private object Dequeue()
            {
                var element = _inputBuffer[_nextInputElementCursor];
                if (element == null)
                    throw new IllegalStateException("Internal queue must never contain a null");
                _inputBuffer[_nextInputElementCursor] = null;

                _batchRemaining--;
                if (_batchRemaining == 0 && !_upstreamCompleted)
                {
                    ReactiveStreamsCompliance.TryRequest(_upstream, _requestBatchSize);
                    _batchRemaining = _requestBatchSize;
                }

                _inputBufferElements--;
                _nextInputElementCursor = (_nextInputElementCursor + 1) & _indexMask;
                return element;
            }

            private void Clear()
            {
                _inputBuffer.Initialize();
                _inputBufferElements = 0;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString() => $"BatchingActorInputBoundary(id={_id}, fill={_inputBufferElements}/{_size}, completed={_upstreamCompleted}, canceled={_downstreamCanceled})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IActorOutputBoundary
        {
            /// <summary>
            /// TBD
            /// </summary>
            void SubscribePending();
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="publisher">TBD</param>
            void ExposedPublisher(IActorPublisher publisher);
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="elements">TBD</param>
            void RequestMore(long elements);
            /// <summary>
            /// TBD
            /// </summary>
            void Cancel();
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="reason">TBD</param>
            void Fail(Exception reason);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        internal class ActorOutputBoundary<T> : DownstreamBoundaryStageLogic, IActorOutputBoundary
        {
            #region InHandler
            private sealed class InHandler : Stage.InHandler
            {
                private readonly ActorOutputBoundary<T> _that;

                public InHandler(ActorOutputBoundary<T> that) => _that = that;

                public override void OnPush()
                {
                    _that.OnNext(_that.Grab(_that._inlet));
                    if (_that._downstreamCompleted)
                        _that.Cancel(_that._inlet);
                    else if (_that._downstreamDemand > 0)
                        _that.Pull(_that._inlet);
                }

                public override void OnUpstreamFinish() => _that.Complete();

                public override void OnUpstreamFailure(Exception e) => _that.Fail(e);

                public override string ToString() => _that.ToString();
            }
            #endregion

            private readonly IActorRef _actor;
            private readonly GraphInterpreterShell _shell;
            private readonly int _id;

            private ActorPublisher<T> _exposedPublisher;
            private ISubscriber<T> _subscriber;
            private long _downstreamDemand;

            // This flag is only used if complete/fail is called externally since this op turns into a Finished one inside the
            // interpreter (i.e. inside this op this flag has no effects since if it is completed the op will not be invoked)
            private bool _downstreamCompleted;
            // when upstream failed before we got the exposed publisher
            private Exception _upstreamFailed;
            private bool _upstreamCompleted;
            private readonly Inlet<T> _inlet;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actor">TBD</param>
            /// <param name="shell">TBD</param>
            /// <param name="id">TBD</param>
            public ActorOutputBoundary(IActorRef actor, GraphInterpreterShell shell, int id)
            {
                _actor = actor;
                _shell = shell;
                _id = id;

                _inlet = new Inlet<T>("UpstreamBoundary" + id) { Id = 0 };
                SetHandler(_inlet, new InHandler(this));
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override Inlet In => _inlet;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="elements">TBD</param>
            public void RequestMore(long elements)
            {
                if (elements < 1)
                {
                    Cancel((Inlet<T>) In);
                    Fail(ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException);
                }
                else
                {
                    _downstreamDemand += elements;
                    if (_downstreamDemand < 0)
                        _downstreamDemand = long.MaxValue; // Long overflow, Reactive Streams Spec 3:17: effectively unbounded
                    if (!HasBeenPulled(_inlet) && !IsClosed(_inlet))
                        Pull(_inlet);
                }
            }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
            /// <summary>
            /// TBD
            /// </summary>
            public void SubscribePending()
            {
                foreach (var subscriber in _exposedPublisher.TakePendingSubscribers())
                {
                    if (ReferenceEquals(_subscriber, null))
                    {
                        _subscriber = subscriber;
                        ReactiveStreamsCompliance.TryOnSubscribe(_subscriber, new BoundarySubscription(_actor, _shell, _id));
                        if (IsDebug)
                            Console.WriteLine($"{Interpreter.Name} Subscribe subscriber={subscriber}");
                    }
                    else ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, GetType().FullName);
                }
            }
#pragma warning restore CS0162

            void IActorOutputBoundary.ExposedPublisher(IActorPublisher publisher) => ExposedPublisher((ActorPublisher<T>) publisher);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="publisher">TBD</param>
            public void ExposedPublisher(ActorPublisher<T> publisher)
            {
                _exposedPublisher = publisher;
                if (_upstreamFailed != null)
                    publisher.Shutdown(_upstreamFailed);
                else
                {
                    if (_upstreamCompleted)
                        publisher.Shutdown(null);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void Cancel()
            {
                _downstreamCompleted = true;
                _subscriber = null;
                _exposedPublisher.Shutdown(new NormalShutdownException("UpstreamBoundary"));
                Cancel(_inlet);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="reason">TBD</param>
            public void Fail(Exception reason)
            {
                // No need to fail if had already been cancelled, or we closed earlier
                if (!(_downstreamCompleted || _upstreamCompleted))
                {
                    _upstreamCompleted = true;
                    _upstreamFailed = reason;

                    if (!ReferenceEquals(_exposedPublisher, null))
                        _exposedPublisher.Shutdown(reason);
                    if (!ReferenceEquals(_subscriber, null) && !(reason is ISpecViolation))
                        ReactiveStreamsCompliance.TryOnError(_subscriber, reason);
                }
            }

            private void OnNext(T element)
            {
                _downstreamDemand--;
                ReactiveStreamsCompliance.TryOnNext(_subscriber, element);
            }

            private void Complete()
            {
                // No need to complete if had already been cancelled, or we closed earlier
                if (!(_upstreamCompleted || _downstreamCompleted))
                {
                    _upstreamCompleted = true;
                    if (!ReferenceEquals(_exposedPublisher, null))
                        _exposedPublisher.Shutdown(null);
                    if (!ReferenceEquals(_subscriber, null))
                        ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                }
            }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shell">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(GraphInterpreterShell shell) => Actor.Props.Create(() => new ActorGraphInterpreter(shell)).WithDeploy(Deploy.Local);

        private ISet<GraphInterpreterShell> _activeInterpreters = new HashSet<GraphInterpreterShell>();
        private readonly Queue<GraphInterpreterShell> _newShells = new Queue<GraphInterpreterShell>();
        private readonly SubFusingActorMaterializerImpl _subFusingMaterializerImpl;
        private readonly GraphInterpreterShell _initial;
        private ILoggingAdapter _log;
        //this limits number of messages that can be processed synchronously during one actor receive.
        private readonly int _eventLimit;
        private int _currentLimit;
        //this is a var in order to save the allocation when no short-circuiting actually happens
        private Queue<object> _shortCircuitBuffer;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shell">TBD</param>
        public ActorGraphInterpreter(GraphInterpreterShell shell)
        {
            _initial = shell;

            _subFusingMaterializerImpl = new SubFusingActorMaterializerImpl(shell.Materializer, RegisterShell);
            _eventLimit = _initial.Materializer.Settings.SyncProcessingLimit;
            _currentLimit = _eventLimit;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        private void EnqueueToShortCircuit(object input)
        {
            if(_shortCircuitBuffer == null)
                _shortCircuitBuffer = new Queue<object>();

            _shortCircuitBuffer.Enqueue(input);
        }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        private bool TryInit(GraphInterpreterShell shell)
        {
            try
            {
                _currentLimit = shell.Init(Self, _subFusingMaterializerImpl, EnqueueToShortCircuit, _currentLimit);
                if (IsDebug)
                    Console.WriteLine($"registering new shell in {_initial}\n  {shell.ToString().Replace("\n", "\n  ")}");
                if (shell.IsTerminated)
                    return false;
                _activeInterpreters.Add(shell);
                return true;
            }
            catch (Exception e)
            {
                if (Log.IsErrorEnabled)
                    Log.Error(e, "Initialization of GraphInterpreterShell failed for {0}", shell);
                return false;
            }
        }
#pragma warning restore CS0162

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shell">TBD</param>
        /// <returns>TBD</returns>
        public IActorRef RegisterShell(GraphInterpreterShell shell)
        {
            _newShells.Enqueue(shell);
            EnqueueToShortCircuit(ShellRegistered.Instance);
            return Self;
        }

        // Avoid performing the initialization (which starts the first RunBatch())
        // within RegisterShell in order to avoid unbounded recursion.
        private void FinishShellRegistration()
        {
            if (_newShells.Count == 0)
            {
                if (_activeInterpreters.Count == 0)
                    Context.Stop(Self);
            }
            else
            {
                var shell = _newShells.Dequeue();
                if (shell.IsInitialized)
                {
                    // yes, this steals another shell's Resume, but that's okay because extra ones will just not do anything
                    FinishShellRegistration();
                }
                else if (!TryInit(shell))
                {
                    if (_activeInterpreters.Count == 0)
                        FinishShellRegistration();
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            TryInit(_initial);
            if (_activeInterpreters.Count == 0)
                Context.Stop(Self);
            else if (_shortCircuitBuffer != null)
                ShortCircuitBatch();
        }

        private void ShortCircuitBatch()
        {
            while (_shortCircuitBuffer.Count != 0 && _currentLimit > 0 && _activeInterpreters.Count != 0)
            {
                var element = _shortCircuitBuffer.Dequeue();
                if (element is IBoundaryEvent boundary)
                    ProcessEvent(boundary);
                else if (element is ShellRegistered)
                    FinishShellRegistration();
            }

            if(_shortCircuitBuffer.Count != 0 && _currentLimit == 0)
                Self.Tell(ShellRegistered.Instance);
        }

        private void ProcessEvent(IBoundaryEvent b)
        {
            var shell = b.Shell;
            if (!shell.IsTerminated && (shell.IsInitialized || TryInit(shell)))
            {
                try
                {
                    _currentLimit = shell.Receive(b, _currentLimit);
                }
                catch (Exception ex)
                {
                    shell.TryAbort(ex);
                }

                if (shell.IsTerminated)
                {
                    _activeInterpreters.Remove(shell);
                    if(_activeInterpreters.Count == 0 && _newShells.Count == 0)
                        Context.Stop(Self);
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case IBoundaryEvent _:
                    _currentLimit = _eventLimit;
                    ProcessEvent((IBoundaryEvent)message);
                    if (_shortCircuitBuffer != null)
                        ShortCircuitBatch();
                    return true;
                case ShellRegistered _:
                    _currentLimit = _eventLimit;
                    if (_shortCircuitBuffer != null)
                        ShortCircuitBatch();
                    return true;
                case StreamSupervisor.PrintDebugDump _:
                    var builder = new StringBuilder($"activeShells (actor: {Self}):\n");

                    foreach (var shell in _activeInterpreters)
                    {
                        builder.Append("  " + shell.ToString().Replace("\n", "\n  "));
                        builder.Append(shell.Interpreter);
                    }

                    builder.AppendLine("NewShells:\n");

                    foreach (var shell in _newShells)
                    {
                        builder.Append("  " + shell.ToString().Replace("\n", "\n  "));
                        builder.Append(shell.Interpreter);
                    }

                    Console.WriteLine(builder);
                    return true;
                default: return false;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            var ex = new AbruptTerminationException(Self);
            foreach (var shell in _activeInterpreters)
                shell.TryAbort(ex);
            _activeInterpreters = new HashSet<GraphInterpreterShell>();
            foreach (var shell in _newShells)
            {
                if (TryInit(shell))
                    shell.TryAbort(ex);
            }
        }
    }
}
