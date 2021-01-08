//-----------------------------------------------------------------------
// <copyright file="FanOut.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Pattern;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class OutputBunch<T>
    {
        #region internal classes

        private sealed class FanoutOutputs : SimpleOutputs
        {
            private readonly int _id;

            public FanoutOutputs(int id, IActorRef actor, IPump pump) : base(actor, pump)
            {
                _id = id;
            }

            public new ISubscription CreateSubscription() => new FanOut.SubstreamSubscription(Actor, _id);
        }

        #endregion

        private readonly int _outputCount;
        private bool _bunchCancelled;
        private readonly FanoutOutputs[] _outputs;
        private readonly bool[] _marked;
        private int _markedCount;
        private readonly bool[] _pending;
        private int _markedPending;
        private readonly bool[] _cancelled;
        private int _markedCanceled;
        private readonly bool[] _completed;
        private readonly bool[] _errored;
        private bool _unmarkCancelled = true;
        private int _preferredId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outputCount">TBD</param>
        /// <param name="impl">TBD</param>
        /// <param name="pump">TBD</param>
        public OutputBunch(int outputCount, IActorRef impl, IPump pump)
        {
            _outputCount = outputCount;
            _outputs = new FanoutOutputs[outputCount];
            for (var i = 0; i < outputCount; i++)
                _outputs[i] = new FanoutOutputs(i, impl, pump);

            _marked = new bool[outputCount];
            _pending = new bool[outputCount];
            _cancelled = new bool[outputCount];
            _completed = new bool[outputCount];
            _errored = new bool[outputCount];

            AllOfMarkedOutputs = new LambdaTransferState(
                isCompleted: () => _markedCanceled > 0 || _markedCount == 0,
                isReady: () => _markedPending == _markedCount);

            AnyOfMarkedOutputs = new LambdaTransferState(
                isCompleted: () => _markedCanceled == _markedCount,
                isReady: () => _markedPending > 0);

            // FIXME: Eliminate re-wraps
            SubReceive = new SubReceive(message => message.Match()
                .With<FanOut.ExposedPublishers<T>>(exposed =>
                {
                    var publishers = exposed.Publishers.GetEnumerator();
                    var outputs = _outputs.AsEnumerable().GetEnumerator();

                    while (publishers.MoveNext() && outputs.MoveNext())
                        outputs.Current.SubReceive.CurrentReceive(new ExposedPublisher(publishers.Current));
                })
                .With<FanOut.SubstreamRequestMore>(more =>
                {
                    if (more.Demand < 1)
                        // According to Reactive Streams Spec 3.9, with non-positive demand must yield onError
                        Error(more.Id, ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException);
                    else
                    {
                        if (_marked[more.Id] && !_pending[more.Id])
                            _markedPending += 1;
                        _pending[more.Id] = true;
                        _outputs[more.Id].SubReceive.CurrentReceive(new RequestMore(null, more.Demand));
                    }
                })
                .With<FanOut.SubstreamCancel>(cancel =>
                {
                    if (_unmarkCancelled)
                        UnmarkOutput(cancel.Id);

                    if (_marked[cancel.Id] && !_cancelled[cancel.Id])
                        _markedCanceled += 1;

                    _cancelled[cancel.Id] = true;
                    OnCancel(cancel.Id);
                    _outputs[cancel.Id].SubReceive.CurrentReceive(new Cancel(null));
                })
                .With<FanOut.SubstreamSubscribePending>(pending => _outputs[pending.Id].SubReceive.CurrentReceive(SubscribePending.Instance))
                .WasHandled);
        }

        /// <summary>
        /// Will only transfer an element when all marked outputs
        /// have demand, and will complete as soon as any of the marked
        /// outputs have canceled.
        /// </summary>
        public readonly TransferState AllOfMarkedOutputs;

        /// <summary>
        /// Will transfer an element when any of the  marked outputs
        /// have demand, and will complete when all of the marked
        /// outputs have canceled.
        /// </summary>
        public readonly TransferState AnyOfMarkedOutputs;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SubReceive SubReceive;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        /// <returns>TBD</returns>
        public bool IsPending(int output) => _pending[output];

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        /// <returns>TBD</returns>
        public bool IsCompleted(int output) => _completed[output];

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        /// <returns>TBD</returns>
        public bool IsCancelled(int output) => _cancelled[output];

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        /// <returns>TBD</returns>
        public bool IsErrored(int output) => _errored[output];

        /// <summary>
        /// TBD
        /// </summary>
        public void Complete()
        {
            if (!_bunchCancelled)
            {
                _bunchCancelled = true;

                for (var i = 0; i < _outputs.Length; i++)
                    Complete(i);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        public void Complete(int output)
        {
            if (!_completed[output] && !_errored[output] && !_cancelled[output])
            {
                _outputs[output].Complete();
                _completed[output] = true;
                UnmarkOutput(output);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public void Cancel(Exception e)
        {
            if (!_bunchCancelled)
            {
                _bunchCancelled = true;
                for (var i = 0; i < _outputs.Length; i++)
                    Error(i, e);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        /// <param name="e">TBD</param>
        public void Error(int output, Exception e)
        {
            if (!_errored[output] && !_cancelled[output] && !_completed[output])
            {
                _outputs[output].Error(e);
                _errored[output] = true;
                UnmarkOutput(output);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        public void MarkOutput(int output)
        {
            if (!_marked[output])
            {
                if (_cancelled[output])
                    _markedCanceled += 1;
                if (_pending[output])
                    _markedPending += 1;

                _marked[output] = true;
                _markedCount += 1;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        public void UnmarkOutput(int output)
        {
            if (_marked[output])
            {
                if (_cancelled[output])
                    _markedCanceled -= 1;
                if (_pending[output])
                    _markedPending -= 1;

                _marked[output] = false;
                _markedCount -= 1;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void MarkAllOutputs()
        {
            for (var i = 0; i < _outputCount; i++)
                MarkOutput(i);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void UnmarkAllOutputs()
        {
            for (var i = 0; i < _outputCount; i++)
                UnmarkOutput(i);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enabled">TBD</param>
        public void UnmarkCancelledOutputs(bool enabled) => _unmarkCancelled = enabled;

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public int IdToEnqueue()
        {
            var id = _preferredId;

            while (!(_marked[id] && _pending[id]))
            {
                id += 1;
                if (id == _outputCount)
                    id = 0;

                if (id != _preferredId)
                    throw new ArgumentException("Tried to enqueue without waiting for any demand");
            }

            return id;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="element">TBD</param>
        public void Enqueue(int id, T element)
        {
            var output = _outputs[id];
            output.EnqueueOutputElement(element);

            if (!output.IsDemandAvailable)
            {
                if (_marked[id])
                    _markedPending -= 1;

                _pending[id] = false;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void EnqueueMarked(T element)
        {
            for (var id = 0; id < _outputCount; id++)
                if (_marked[id])
                    Enqueue(id, element);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public int IdToEnqueueAndYield()
        {
            var id = IdToEnqueue();
            _preferredId = id + 1;

            if (_preferredId == _outputCount)
                _preferredId = 0;

            return id;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void EnqueueAndYield(T element) => Enqueue(IdToEnqueueAndYield(), element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <param name="preferred">TBD</param>
        public void EnqueueAndPrefer(T element, int preferred)
        {
            var id = IdToEnqueue();
            _preferredId = preferred;
            Enqueue(id, element);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="output">TBD</param>
        public void OnCancel(int output)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public TransferState DemandAvailableFor(int id) =>
            new LambdaTransferState(isReady: () => _pending[id],
                isCompleted: () => _cancelled[id] || _completed[id] || _errored[id]);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public TransferState DemandOrCancelAvailableFor(int id)
            => new LambdaTransferState(isReady: () => _pending[id] || _cancelled[id], isCompleted: () => false);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public static class FanOut
    {
        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct SubstreamRequestMore : INoSerializationVerificationNeeded, IDeadLetterSuppression
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
            /// <param name="id">TBD</param>
            /// <param name="demand">TBD</param>
            public SubstreamRequestMore(int id, long demand)
            {
                Id = id;
                Demand = demand;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct SubstreamCancel : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="id">TBD</param>
            public SubstreamCancel(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct SubstreamSubscribePending : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="id">TBD</param>
            public SubstreamSubscribePending(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class SubstreamSubscription : ISubscription
        {
            private readonly IActorRef _parent;
            private readonly int _id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="parent">TBD</param>
            /// <param name="id">TBD</param>
            public SubstreamSubscription(IActorRef parent, int id)
            {
                _parent = parent;
                _id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="elements">TBD</param>
            public void Request(long elements) => _parent.Tell(new SubstreamRequestMore(_id, elements));

            /// <summary>
            /// TBD
            /// </summary>
            public void Cancel() => _parent.Tell(new SubstreamCancel(_id));

            /// <inheritdoc/>
            public override string ToString() => "SubstreamSubscription" + GetHashCode();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        [Serializable]
        public struct ExposedPublishers<T> : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ImmutableList<ActorPublisher<T>> Publishers;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="publishers">TBD</param>
            public ExposedPublishers(ImmutableList<ActorPublisher<T>> publishers)
            {
                Publishers = publishers;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public abstract class FanOut<T> : ActorBase, IPump
    {

        #region internal classes

        private sealed class AnonymousBatchingInputBuffer : BatchingInputBuffer
        {
            private readonly FanOut<T> _pump;

            public AnonymousBatchingInputBuffer(int count, FanOut<T> pump) : base(count, pump)
            {
                _pump = pump;
            }

            protected override void OnError(Exception e) => _pump.Fail(e);
        }

        #endregion

        private readonly ActorMaterializerSettings _settings;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly OutputBunch<T> OutputBunch;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly BatchingInputBuffer PrimaryInputs;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="outputCount">TBD</param>
        protected FanOut(ActorMaterializerSettings settings, int outputCount)
        {
            _log = Context.GetLogger();
            _settings = settings;
            OutputBunch = new OutputBunch<T>(outputCount, Self, this);
            PrimaryInputs = new AnonymousBatchingInputBuffer(settings.MaxInputBufferSize, this);
            this.Init();
        }

        #region Actor implementation

        /// <summary>
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());
        private ILoggingAdapter _log;

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            PrimaryInputs.Cancel();
            OutputBunch.Cancel(new AbruptTerminationException(Self));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown automatically since the actor cannot be restarted.
        /// </exception>
        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
       protected void Fail(Exception e)
        {
            if (_settings.IsDebugLogging)
                Log.Debug($"fail due to: {e.Message}");

            PrimaryInputs.Cancel();
            OutputBunch.Cancel(e);
            Pump();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            return PrimaryInputs.SubReceive.CurrentReceive(message) ||
                   OutputBunch.SubReceive.CurrentReceive(message);
        }

        #endregion

        #region Pump implementation

        /// <summary>
        /// TBD
        /// </summary>
        public TransferState TransferState { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Action CurrentAction { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsPumpFinished => this.IsPumpFinished();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="waitForUpstream">TBD</param>
        /// <param name="andThen">TBD</param>
        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
            => Pumps.InitialPhase(this, waitForUpstream, andThen);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="waitForUpstream">TBD</param>
        public void WaitForUpstream(int waitForUpstream) => Pumps.WaitForUpstream(this, waitForUpstream);

        /// <summary>
        /// TBD
        /// </summary>
        public void GotUpstreamSubscription() => Pumps.GotUpstreamSubscription(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="phase">TBD</param>
        public void NextPhase(TransferPhase phase) => Pumps.NextPhase(this, phase);

        /// <summary>
        /// TBD
        /// </summary>
        public void Pump() => Pumps.Pump(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public void PumpFailed(Exception e) => Fail(e);

        /// <summary>
        /// TBD
        /// </summary>
        public void PumpFinished()
        {
            PrimaryInputs.Cancel();
            OutputBunch.Complete();
            Context.Stop(Self);
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class Unzip
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="settings">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props<T>(ActorMaterializerSettings settings)
            => Actor.Props.Create(() => new Unzip<T>(settings, 2)).WithDeploy(Deploy.Local);
    }

    /// <summary>
    /// INTERNAL API
    /// TODO Find out where this class will be used and check if the type parameter fit
    /// since we need to cast messages into a tuple and therefore maybe need additional type parameters
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class Unzip<T> : FanOut<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="outputCount">TBD</param>
        /// <exception cref="ArgumentException">TBD
        /// This exception is thrown when the elements in <see cref="Akka.Streams.Implementation.FanOut{T}.PrimaryInputs"/>
        /// are of an unknown type.
        /// </exception>>
        public Unzip(ActorMaterializerSettings settings, int outputCount = 2) : base(settings, outputCount)
        {
            OutputBunch.MarkAllOutputs();

            InitialPhase(1, new TransferPhase(PrimaryInputs.NeedsInput.And(OutputBunch.AllOfMarkedOutputs), () =>
            {
                var message = PrimaryInputs.DequeueInputElement();

                if (!(message is ValueTuple<T, T> tuple))
                    throw new ArgumentException($"Unable to unzip elements of type {message.GetType().Name}");

                OutputBunch.Enqueue(0, tuple.Item1);
                OutputBunch.Enqueue(1, tuple.Item2);
            }));
        }
    }
}
