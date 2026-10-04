//-----------------------------------------------------------------------
// <copyright file="FanOut.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Tracks output subscriptions, demand, and terminal states for a fan-out actor.
    /// </summary>
    /// <typeparam name="T">The type of elements sent to the outputs.</typeparam>
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
        /// Creates one output manager for each configured output.
        /// </summary>
        /// <param name="outputCount">The number of outputs.</param>
        /// <param name="impl">The actor that receives output subscription messages.</param>
        /// <param name="pump">The transfer pump resumed when output signals arrive.</param>
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
            SubReceive = new SubReceive(message =>
            {
                switch (message)
                {
                    case FanOut.ExposedPublishers<T> exposed:
                        using (var publishers = exposed.Publishers.GetEnumerator())
                        {
                            using (var outputs = _outputs.AsEnumerable().GetEnumerator())
                            {
                                while (publishers.MoveNext() && outputs.MoveNext())
                                    outputs.Current?.SubReceive.CurrentReceive(new ExposedPublisher(publishers.Current));
                            }
                        }
                        return true;

                    case FanOut.SubstreamRequestMore more:
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
                        return true;

                    case FanOut.SubstreamCancel cancel:
                        if (_unmarkCancelled)
                            UnmarkOutput(cancel.Id);

                        if (_marked[cancel.Id] && !_cancelled[cancel.Id])
                            _markedCanceled += 1;

                        _cancelled[cancel.Id] = true;
                        OnCancel(cancel.Id);
                        _outputs[cancel.Id].SubReceive.CurrentReceive(new Cancel(null));
                        return true;
                    
                    case FanOut.SubstreamSubscribePending pending:
                        _outputs[pending.Id].SubReceive.CurrentReceive(SubscribePending.Instance);
                        return true;
                    
                    default:
                        return false;
                }
            });
        }

        /// <summary>
        /// Is ready when the marked-pending count equals the marked-output count, and is complete when at least one marked output is canceled or no outputs are marked.
        /// </summary>
        public readonly TransferState AllOfMarkedOutputs;

        /// <summary>
        /// Is ready when the marked-pending count is positive, and is complete when the marked-canceled count equals the marked-output count.
        /// </summary>
        public readonly TransferState AnyOfMarkedOutputs;

        /// <summary>
        /// Gets the receive handler for downstream demand, cancellation, and subscription signals.
        /// </summary>
        public readonly SubReceive SubReceive;

        /// <summary>
        /// Gets whether the indexed output has demand available.
        /// </summary>
        /// <param name="output">The output index to inspect.</param>
        /// <returns><see langword="true"/> if demand is available.</returns>
        public bool IsPending(int output) => _pending[output];

        /// <summary>
        /// Gets whether the indexed output has completed normally.
        /// </summary>
        /// <param name="output">The output index to inspect.</param>
        /// <returns><see langword="true"/> if the output completed.</returns>
        public bool IsCompleted(int output) => _completed[output];

        /// <summary>
        /// Gets whether the indexed output was canceled by its subscriber.
        /// </summary>
        /// <param name="output">The output index to inspect.</param>
        /// <returns><see langword="true"/> if the output was canceled.</returns>
        public bool IsCancelled(int output) => _cancelled[output];

        /// <summary>
        /// Gets whether the indexed output was marked errored by <see cref="Error(int, Exception)"/>.
        /// </summary>
        /// <param name="output">The output index to inspect.</param>
        /// <returns><see langword="true"/> if the output was marked errored.</returns>
        public bool IsErrored(int output) => _errored[output];

        /// <summary>
        /// Completes every output that has not already terminated.
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
        /// Completes one output unless it has already completed, failed, or been canceled.
        /// </summary>
        /// <param name="output">The output index to complete.</param>
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
        /// If this bunch has not already been canceled, calls <see cref="Error(int, Exception)"/> for every configured output index.
        /// </summary>
        /// <param name="e">The failure passed to <see cref="Error(int, Exception)"/> for each configured output index.</param>
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
        /// For an output that is not completed, canceled, or errored, calls its manager's <c>Error</c> method, then marks and unmarks the output if that call returns.
        /// </summary>
        /// <param name="output">The output index to fail.</param>
        /// <param name="e">The failure passed to the output manager.</param>
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
        /// Includes an output in marked-output demand calculations.
        /// </summary>
        /// <param name="output">The output index to include.</param>
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
        /// Excludes an output from marked-output demand calculations.
        /// </summary>
        /// <param name="output">The output index to exclude.</param>
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
        /// Includes every output in marked-output demand calculations.
        /// </summary>
        public void MarkAllOutputs()
        {
            for (var i = 0; i < _outputCount; i++)
                MarkOutput(i);
        }

        /// <summary>
        /// Excludes every output from marked-output demand calculations.
        /// </summary>
        public void UnmarkAllOutputs()
        {
            for (var i = 0; i < _outputCount; i++)
                UnmarkOutput(i);
        }

        /// <summary>
        /// Sets whether a canceled output is removed from the marked-output set.
        /// </summary>
        /// <param name="enabled"><see langword="true"/> to unmark canceled outputs when they cancel.</param>
        public void UnmarkCancelledOutputs(bool enabled) => _unmarkCancelled = enabled;

        /// <summary>
        /// Checks the preferred output for marked demand; if it is ineligible, advances once and throws when the new index differs from the preferred index. With one output, an ineligible index wraps to itself and the search repeats.
        /// </summary>
        /// <exception cref="ArgumentException">The preferred output is ineligible and advancing the index changes it, regardless of whether the new index is eligible.</exception>
        /// <returns>The preferred index when that output is marked and has demand.</returns>
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
        /// Enqueues an element to one output and clears its pending-demand state when demand is exhausted.
        /// </summary>
        /// <param name="id">The output index that receives the element.</param>
        /// <param name="element">The element to enqueue.</param>
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
        /// Enqueues an element to every marked output.
        /// </summary>
        /// <param name="element">The element to enqueue.</param>
        public void EnqueueMarked(T element)
        {
            for (var id = 0; id < _outputCount; id++)
                if (_marked[id])
                    Enqueue(id, element);
        }

        /// <summary>
        /// Calls <see cref="IdToEnqueue"/>, advances the preferred index past the selected output, and returns that index if selection succeeds.
        /// </summary>
        /// <returns>The selected output index.</returns>
        public int IdToEnqueueAndYield()
        {
            var id = IdToEnqueue();
            _preferredId = id + 1;

            if (_preferredId == _outputCount)
                _preferredId = 0;

            return id;
        }

        /// <summary>
        /// Calls <see cref="IdToEnqueueAndYield"/> and enqueues the element to its selected output if selection succeeds.
        /// </summary>
        /// <param name="element">The element to enqueue.</param>
        public void EnqueueAndYield(T element) => Enqueue(IdToEnqueueAndYield(), element);

        /// <summary>
        /// Calls <see cref="IdToEnqueue"/> and, if selection succeeds, sets the next preferred index and enqueues the element to the selected output.
        /// </summary>
        /// <param name="element">The element to enqueue.</param>
        /// <param name="preferred">The output index to prefer on the next selection.</param>
        public void EnqueueAndPrefer(T element, int preferred)
        {
            var id = IdToEnqueue();
            _preferredId = preferred;
            Enqueue(id, element);
        }

        /// <summary>
        /// No-op method invoked when a downstream output cancels.
        /// </summary>
        /// <param name="output">The output index that canceled.</param>
        public void OnCancel(int output)
        {
        }

        /// <summary>
        /// Creates a transfer state that is ready on demand and completes when the output terminates.
        /// </summary>
        /// <param name="id">The output index to observe.</param>
        /// <returns>A state that observes demand and output termination.</returns>
        public TransferState DemandAvailableFor(int id) =>
            new LambdaTransferState(isReady: () => _pending[id],
                isCompleted: () => _cancelled[id] || _completed[id] || _errored[id]);

        /// <summary>
        /// Creates a transfer state that is ready on demand or cancellation and never completes.
        /// </summary>
        /// <param name="id">The output index to observe.</param>
        /// <returns>A state that becomes ready when demand or cancellation is available.</returns>
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
        /// Carries a downstream request for demand on one output.
        /// </summary>
        [Serializable]
        public readonly struct SubstreamRequestMore : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the output requesting elements.
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// Gets the requested element count.
            /// </summary>
            public readonly long Demand;

            /// <summary>
            /// Creates an indexed output-demand message.
            /// </summary>
            /// <param name="id">The output index requesting elements.</param>
            /// <param name="demand">The requested element count.</param>
            public SubstreamRequestMore(int id, long demand)
            {
                Id = id;
                Demand = demand;
            }
        }

        /// <summary>
        /// Signals cancellation of one downstream output.
        /// </summary>
        [Serializable]
        public readonly struct SubstreamCancel : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the canceled output.
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// Creates a cancellation message for one output.
            /// </summary>
            /// <param name="id">The output index that canceled.</param>
            public SubstreamCancel(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// Signals that a downstream output subscription is pending.
        /// </summary>
        [Serializable]
        public readonly struct SubstreamSubscribePending : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the output whose subscription is pending.
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// Creates a pending-subscription message for one output.
            /// </summary>
            /// <param name="id">The output index with a pending subscription.</param>
            public SubstreamSubscribePending(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// Reactive Streams subscription that forwards requests and cancellation to the parent actor.
        /// </summary>
        public class SubstreamSubscription : ISubscription
        {
            private readonly IActorRef _parent;
            private readonly int _id;

            /// <summary>
            /// Creates a subscription bound to the parent actor and output index.
            /// </summary>
            /// <param name="parent">The actor that handles output signals.</param>
            /// <param name="id">The output index associated with this subscription.</param>
            public SubstreamSubscription(IActorRef parent, int id)
            {
                _parent = parent;
                _id = id;
            }

            /// <summary>
            /// Forwards a demand request to the parent actor.
            /// </summary>
            /// <param name="elements">The requested number of elements.</param>
            public void Request(long elements) => _parent.Tell(new SubstreamRequestMore(_id, elements));

            /// <summary>
            /// Forwards cancellation to the parent actor.
            /// </summary>
            public void Cancel() => _parent.Tell(new SubstreamCancel(_id));

            /// <inheritdoc/>
            public override string ToString() => "SubstreamSubscription" + GetHashCode();
        }

        /// <summary>
        /// Carries the actor publishers exposed by a fan-out stage.
        /// </summary>
        /// <typeparam name="T">The type of elements published by each output.</typeparam>
        [Serializable]
        public readonly struct ExposedPublishers<T> : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the publishers exposed by the stage.
            /// </summary>
            public readonly ImmutableList<ActorPublisher<T>> Publishers;

            /// <summary>
            /// Creates a message containing the exposed publishers.
            /// </summary>
            /// <param name="publishers">The publishers exposed by the stage.</param>
            public ExposedPublishers(ImmutableList<ActorPublisher<T>> publishers)
            {
                Publishers = publishers;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements accepted from upstream.</typeparam>
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
        /// Gets the output manager that tracks downstream demand and terminal signals.
        /// </summary>
        protected readonly OutputBunch<T> OutputBunch;
        /// <summary>
        /// Gets the buffer for the stage's single upstream input.
        /// </summary>
        protected readonly BatchingInputBuffer PrimaryInputs;

        /// <summary>
        /// Creates a fan-out actor with one buffered upstream input and the requested number of outputs.
        /// </summary>
        /// <param name="settings">Materializer settings used to configure the input buffer.</param>
        /// <param name="outputCount">The number of downstream outputs.</param>
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
        /// Gets the actor logger, creating it on first access.
        /// </summary>
        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        private ILoggingAdapter _log;

        /// <summary>
        /// Cancels the upstream input and fails any output that has not already terminated when the actor stops.
        /// </summary>
        protected override void PostStop()
        {
            PrimaryInputs.Cancel();
            OutputBunch.Cancel(new AbruptTerminationException(Self));
        }

        /// <summary>
        /// Rejects restart because the fan-out actor's state cannot be reconstructed.
        /// </summary>
        /// <param name="reason">The exception that caused the restart attempt.</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown automatically since the actor cannot be restarted.
        /// </exception>
        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted");
        }

        /// <summary>
        /// Calls input cancellation and the output bunch's <c>Cancel</c> method, then pumps the current phase.
        /// </summary>
        /// <param name="e">The failure passed to the output managers.</param>
       protected void Fail(Exception e)
        {
            if (_settings.IsDebugLogging)
                Log.Debug($"fail due to: {e.Message}");

            PrimaryInputs.Cancel();
            OutputBunch.Cancel(e);
            Pump();
        }

        /// <summary>
        /// Routes input-buffer and output-subscription messages to their handlers.
        /// </summary>
        /// <param name="message">The actor message to route.</param>
        /// <returns><see langword="true"/> if either handler consumes the message.</returns>
        protected override bool Receive(object message)
        {
            return PrimaryInputs.SubReceive.CurrentReceive(message) ||
                   OutputBunch.SubReceive.CurrentReceive(message);
        }

        #endregion

        #region Pump implementation

        /// <summary>
        /// Gets or sets the current transfer state of the fan-out pump.
        /// </summary>
        public TransferState TransferState { get; set; }

        /// <summary>
        /// Gets or sets the action executed by the fan-out pump.
        /// </summary>
        public Action CurrentAction { get; set; }

        /// <summary>
        /// Gets whether the fan-out pump has completed.
        /// </summary>
        public bool IsPumpFinished => this.IsPumpFinished();

        /// <summary>
        /// Configures the initial pump phase to wait for the specified positive number of upstream subscriptions.
        /// </summary>
        /// <param name="waitForUpstream">The number of subscriptions required before running the phase.</param>
        /// <param name="andThen">The phase to run after subscriptions arrive.</param>
        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
            => Pumps.InitialPhase(this, waitForUpstream, andThen);

        /// <summary>
        /// Pauses the current phase until the specified number of upstream subscriptions arrive.
        /// </summary>
        /// <param name="waitForUpstream">The number of subscriptions to wait for.</param>
        public void WaitForUpstream(int waitForUpstream) => Pumps.WaitForUpstream(this, waitForUpstream);

        /// <summary>
        /// Notifies the pump that an upstream subscription arrived.
        /// </summary>
        public void GotUpstreamSubscription() => Pumps.GotUpstreamSubscription(this);

        /// <summary>
        /// Installs the next transfer phase.
        /// </summary>
        /// <param name="phase">The phase to install.</param>
        public void NextPhase(TransferPhase phase) => Pumps.NextPhase(this, phase);

        /// <summary>
        /// Runs the current transfer action while its state is executable.
        /// </summary>
        public void Pump() => Pumps.Pump(this);

        /// <summary>
        /// Routes a pump-action exception to the fan-out failure handler.
        /// </summary>
        /// <param name="e">The exception thrown by the current action.</param>
        public void PumpFailed(Exception e) => Fail(e);

        /// <summary>
        /// Cancels the input, completes every output, and stops the actor when transfer ends.
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
        /// Creates local actor properties for an unzip actor with two outputs.
        /// </summary>
        /// <typeparam name="T">The element type of the tuple inputs.</typeparam>
        /// <param name="settings">Materializer settings for the actor.</param>
        /// <returns>Properties for creating an <see cref="Unzip{T}"/> actor.</returns>
        public static Props Props<T>(ActorMaterializerSettings settings)
            => Actor.Props.Create<Unzip<T>>(settings, 2).WithDeploy(Deploy.Local);
    }

    /// <summary>
    /// INTERNAL API
    /// TODO Find out where this class will be used and check if the type parameter fit
    /// since we need to cast messages into a tuple and therefore maybe need additional type parameters
    /// </summary>
    /// <typeparam name="T">The type of each item in the two-element tuple accepted from upstream.</typeparam>
    internal sealed class Unzip<T> : FanOut<T>
    {
        /// <summary>
        /// Creates an unzip actor that splits two-element tuples across two outputs. Its transfer action throws <see cref="ArgumentException"/> if an input is not a <see cref="ValueTuple{T1,T2}"/>.
        /// </summary>
        /// <param name="settings">Materializer settings used to configure the input buffer.</param>
        /// <param name="outputCount">The number of outputs; this actor requires two.</param>
        /// If this gets changed you must change <see cref="Unzip{T}"/> as well!
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
