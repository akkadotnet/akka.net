using System;
using System.Collections.Immutable;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Streams.Implementation
{
    internal class OutputBunch<T>
    {
        #region internal classes

        private sealed class FanoutOutputs : SimpleOutputs<T>
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
                    var publishers = exposed.Publishers;
                    for (var i = 0; i < publishers.Count; i++)
                        _outputs[i].SubReceive.CurrentReceive(new ExposedPublisher<T>(publishers[i]));
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
                        _outputs[more.Id].SubReceive.CurrentReceive(new RequestMore<T>(null, more.Demand));
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
                    _outputs[cancel.Id].SubReceive.CurrentReceive(new Cancel<T>(null));
                })
                .With<FanOut.SubstreamSubscribePending>(pending =>
                {
                    _outputs[pending.Id].SubReceive.CurrentReceive(SubscribePending.Instance);
                })
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

        public readonly SubReceive SubReceive;

        public bool IsPending(int output) => _pending[output];

        public bool IsCompleted(int output) => _completed[output];

        public bool IsCancelled(int output) => _cancelled[output];

        public bool IsErrored(int output) => _errored[output];

        public void Complete()
        {
            if (!_bunchCancelled)
            {
                _bunchCancelled = true;

                for (var i = 0; i < _outputs.Length; i++)
                    Complete(i);
            }
        }

        public void Complete(int output)
        {
            if (!_completed[output] && !_errored[output] && !_cancelled[output])
            {
                _outputs[output].Complete();
                _completed[output] = true;
                UnmarkOutput(output);
            }
        }

        public void Cancel(Exception e)
        {
            if (!_bunchCancelled)
            {
                _bunchCancelled = true;
                for (int i = 0; i < _outputs.Length; i++)
                    Error(i, e);
            }
        }

        public void Error(int output, Exception e)
        {
            if (!_errored[output] && !_cancelled[output] && !_completed[output])
            {
                _outputs[output].Error(e);
                _errored[output] = true;
                UnmarkOutput(output);
            }
        }

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

        public void MarkAllOutputs()
        {
            for (var i = 0; i < _outputCount; i++)
                MarkOutput(i);
        }

        public void UnmarkAllOutputs()
        {
            for (int i = 0; i < _outputCount; i++)
                UnmarkOutput(i);
        }

        public void UnmarkCancelledOutputs(bool enabled) => _unmarkCancelled = enabled;

        public int IdToEnqueue()
        {
            var id = _preferredId;

            while (!(_marked[id] && _pending[id]))
            {
                id += 1;
                if (id == _outputCount)
                    id = 0;

                if (id != _preferredId)
                    throw new ArgumentException("Tried to equeue without waiting for any demand");
            }

            return id;
        }

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

        public void EnqueueMarked(T element)
        {
            for (var id = 0; id < _outputCount; id++)
            {
                if (_marked[id])
                {
                    Enqueue(id, element);
                }
            }
        }

        public int IdToEnqueueAndYield()
        {
            var id = IdToEnqueue();
            _preferredId = id + 1;

            if (_preferredId == _outputCount)
                _preferredId = 0;

            return id;
        }

        public void EnqueueAndYield(T element) => Enqueue(IdToEnqueueAndYield(), element);

        public void EnqueueAndPrefer(T element, int preferred)
        {
            var id = IdToEnqueue();
            _preferredId = preferred;
            Enqueue(id, element);
        }

        public void OnCancel(int output)
        {
        }

        public TransferState DemandAvailableFor(int id) =>
            new LambdaTransferState(isReady: () => _pending[id],
                isCompleted: () => _cancelled[id] || _completed[id] || _errored[id]);

        public TransferState DemandOrCancelAvailableFor(int id)
            => new LambdaTransferState(isReady: () => _pending[id] || _cancelled[id], isCompleted: () => false);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class FanOut
    {
        [Serializable]
        public struct SubstreamRequestMore : INoSerializationVerificationNeeded
        {
            public readonly int Id;
            public readonly long Demand;

            public SubstreamRequestMore(int id, long demand)
            {
                Id = id;
                Demand = demand;
            }
        }

        [Serializable]
        public struct SubstreamCancel : INoSerializationVerificationNeeded
        {
            public readonly int Id;

            public SubstreamCancel(int id)
            {
                Id = id;
            }
        }

        [Serializable]
        public struct SubstreamSubscribePending : INoSerializationVerificationNeeded
        {
            public readonly int Id;

            public SubstreamSubscribePending(int id)
            {
                Id = id;
            }
        }

        public class SubstreamSubscription : ISubscription
        {
            private readonly IActorRef _parent;
            private readonly int _id;

            public SubstreamSubscription(IActorRef parent, int id)
            {
                _parent = parent;
                _id = id;
            }

            public void Request(long elements) => _parent.Tell(new SubstreamRequestMore(_id, elements));

            public void Cancel() => _parent.Tell(new SubstreamCancel(_id));

            public override string ToString() => "SubstreamSubscription" + GetHashCode();
        }

        [Serializable]
        public struct ExposedPublishers<T> : INoSerializationVerificationNeeded
        {
            public readonly ImmutableList<ActorPublisher<T>> Publishers;

            public ExposedPublishers(ImmutableList<ActorPublisher<T>> publishers)
            {
                Publishers = publishers;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal abstract class FanOut<T> : ActorBase, IPump
    {

        #region internal classes

        private sealed class AnonymousBatchingInputBuffer : BatchingInputBuffer
        {
            private readonly FanOut<T> _pump;

            public AnonymousBatchingInputBuffer(int count, FanOut<T> pump) : base(count, pump)
            {
                _pump = pump;
            }

            protected override void OnError(Exception e)
            {
                _pump.Fail(e);
            }
        }

        #endregion

        private readonly ActorMaterializerSettings _settings;
        protected readonly OutputBunch<T> OutputBunch;
        protected readonly BatchingInputBuffer PrimaryInputs;

        protected FanOut(ActorMaterializerSettings settings, int outputCount)
        {
            _log = Context.GetLogger();
            _settings = settings;
            OutputBunch = new OutputBunch<T>(outputCount, Self, this);
            PrimaryInputs = new AnonymousBatchingInputBuffer(settings.MaxInputBufferSize, this);
        }

        #region Actor implementation

        private ILoggingAdapter _log;
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected override void PostStop()
        {
            PrimaryInputs.Cancel();
            OutputBunch.Cancel(new AbruptTerminationException(Self));
        }

        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted");
        }

        protected void Fail(Exception e)
        {
            if (_settings.IsDebugLogging)
                Log.Debug($"fail due to: {e.Message}");

            PrimaryInputs.Cancel();
            OutputBunch.Cancel(e);
            Pump();
        }

        protected override bool Receive(object message)
        {
            return PrimaryInputs.SubReceive.CurrentReceive(message) ||
                   OutputBunch.SubReceive.CurrentReceive(message);
        }

        #endregion

        #region Pump implementation

        public TransferState TransferState { get; set; }

        public Action CurrentAction { get; set; }

        public bool IsPumpFinished => TransferState.IsCompleted;

        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
            => Pumps.InitialPhase(this, waitForUpstream, andThen);

        public void WaitForUpstream(int waitForUpstream) => Pumps.WaitForUpstream(this, waitForUpstream);

        public void GotUpstreamSubscription() => Pumps.GotUpstreamSubscription(this);

        public void NextPhase(TransferPhase phase) => Pumps.NextPhase(this, phase);

        public void Pump() => Pumps.Pump(this);

        public void PumpFailed(Exception e) => Fail(e);

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
        public static Props Props<T>(ActorMaterializerSettings settings)
        {
            return Akka.Actor.Props.Create(() => new Unzip<T>(settings, 2)).WithDeploy(Deploy.Local);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Unzip<T> : FanOut<T>
    {
        public Unzip(ActorMaterializerSettings settings, int outputCount = 2) : base(settings, outputCount)
        {
            OutputBunch.MarkAllOutputs();

            InitialPhase(1, new TransferPhase(PrimaryInputs.NeedsInput.And(OutputBunch.AllOfMarkedOutputs), () =>
            {
                var message = PrimaryInputs.DequeueInputElement();
                var tuple = message as Tuple<T, T>;

                if (tuple == null)
                    throw new ArgumentException($"Unable to unzip elements of type {message.GetType().Name}");

                OutputBunch.Enqueue(0, tuple.Item1);
                OutputBunch.Enqueue(1, tuple.Item2);
            }));
        }
    }
}
