//-----------------------------------------------------------------------
// <copyright file="FanIn.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    using State = Byte;

    /// <summary>
    /// Coordinates buffered inputs from multiple upstreams and tracks marked-input readiness for a transfer pump.
    /// </summary>
    public abstract class InputBunch
    {
        #region internal classes

        private sealed class AnonymousBatchingInputBuffer : BatchingInputBuffer
        {
            private readonly int _id;
            private readonly InputBunch _inputBunch;

            public AnonymousBatchingInputBuffer(int count, IPump pump, int id, InputBunch inputBunch) : base(count, pump)
            {
                _id = id;
                _inputBunch = inputBunch;
            }

            protected override void OnError(Exception e) => _inputBunch.OnError(_id, e);
        }

        #endregion

        /// <summary>
        /// Gets a state ready when the tracked marked-pending count equals the marked count, and completed when the tracked marked-depleted count is greater than zero.
        /// </summary>
        public readonly TransferState AllOfMarkedInputs;
        /// <summary>
        /// Gets a state ready when the tracked marked-pending count is greater than zero, and completed when the tracked marked-depleted count equals the marked count and the marked-pending count is zero.
        /// </summary>
        public readonly TransferState AnyOfMarkedInputs;
        /// <summary>
        /// Gets the receive handler for indexed upstream signals.
        /// </summary>
        public readonly SubReceive SubReceive;

        private readonly int _inputCount;
        private readonly BatchingInputBuffer[] _inputs;
        private readonly State[] _states;

        private bool _allCancelled;
        private int _markCount;
        private int _markedPending;
        private int _markedDepleted;
        private bool _receivedInput;
        private int _completedCounter;

        private int _preferredId;
        private int _lastDequeuedId;

        /// <summary>
        /// Creates an input bunch with one batching buffer per upstream.
        /// </summary>
        /// <param name="inputCount">The number of upstream inputs.</param>
        /// <param name="bufferSize">The capacity of each input buffer.</param>
        /// <param name="pump">The transfer pump resumed when inputs change.</param>
        protected InputBunch(int inputCount, int bufferSize, IPump pump)
        {
            _inputCount = inputCount;

            _states = new State[inputCount];
            _inputs = new BatchingInputBuffer[inputCount];
            for (var i = 0; i < inputCount; i++)
                _inputs[i] = new AnonymousBatchingInputBuffer(bufferSize, pump, i, this);

            AllOfMarkedInputs = new LambdaTransferState(
                isCompleted: () => _markedDepleted > 0,
                isReady: () => _markedPending == _markCount);

            AnyOfMarkedInputs = new LambdaTransferState(
                isCompleted: () => _markedDepleted == _markCount && _markedPending == 0,
                isReady: () => _markedPending > 0);

            // FIXME: Eliminate re-wraps
            SubReceive = new SubReceive(msg =>
            {
                switch (msg)
                {
                    case FanIn.OnSubscribe subscribe:
                        _inputs[subscribe.Id].SubReceive.CurrentReceive(new Actors.OnSubscribe(subscribe.Subscription));
                        return true;
                    
                    case FanIn.OnNext next:
                    {
                        var id = next.Id;
                        if (IsMarked(id) && !IsPending(id))
                            _markedPending++;
                        Pending(id, on: true);
                        _receivedInput = true;
                        _inputs[id].SubReceive.CurrentReceive(new Actors.OnNext(next.Element));
                        return true;
                    }
                    
                    case FanIn.OnComplete complete:
                    {
                        var id = complete.Id;
                        if (!IsPending(id))
                        {
                            if (IsMarked(id) && !IsDepleted(id))
                                _markedDepleted++;
                            Depleted(id, on: true);
                            OnDepleted(id);
                        }

                        RegisterCompleted(id);
                        _inputs[id].SubReceive.CurrentReceive(Actors.OnComplete.Instance);

                        if (!_receivedInput && IsAllCompleted)
                            OnCompleteWhenNoInput();
                        return true;
                    }
                    
                    case FanIn.OnError error:
                        OnError(error.Id, error.Cause);
                        return true;
                    
                    default:
                        return false;
                }
            });
        }

        /// <summary>
        /// Gets the ID of the input most recently dequeued.
        /// </summary>
        protected int LastDequeuedId => _lastDequeuedId;

        /// <summary>
        /// Gets whether the completed-message counter equals the configured input count.
        /// </summary>
        public bool IsAllCompleted => _inputCount == _completedCounter;

        /// <summary>
        /// Creates a state ready when the tracked marked-pending count is positive and completed when this input's depleted or canceled bit is clear, or its pending bit is set while its completed bit is clear.
        /// </summary>
        /// <param name="id">The input whose state bits are checked by the completion predicate.</param>
        /// <returns>A state using the marked-pending counter for readiness and this input's bit checks for completion.</returns>
        public TransferState InputsAvailableFor(int id)
        {
            return new LambdaTransferState(
                isCompleted: () => IsDepleted(id) || IsCancelled(id) || (!IsPending(id) && IsCompleted(id)),
                isReady: () => _markedPending > 0);
        }

        /// <summary>
        /// Creates a transfer state that is ready when this input's pending or depleted bit is clear and never completes.
        /// </summary>
        /// <param name="id">The input whose pending and depleted bits are checked.</param>
        /// <returns>A state that becomes ready when either bit is clear.</returns>
        public TransferState InputsOrCompleteAvailableFor(int id)
        {
            return new LambdaTransferState(
                isCompleted: () => false,
                isReady: () => IsPending(id) || IsDepleted(id));
        }

        /// <summary>
        /// Sets the all-input cancellation guard and calls <see cref="Cancel(int)"/> once for each input index.
        /// </summary>
        public void Cancel()
        {
            if (!_allCancelled)
            {
                _allCancelled = true;
                for (var i = 0; i < _inputs.Length; i++)
                    Cancel(i);
            }
        }

        /// <summary>
        /// If the canceled-state bit is set, calls the buffer's Cancel method, sets the bit, and calls <see cref="UnmarkInput(int)"/>; when the bit is clear, it does nothing.
        /// </summary>
        /// <param name="input">The input index to cancel.</param>
        public void Cancel(int input)
        {
            if (!IsCancelled(input))
            {
                _inputs[input].Cancel();
                Cancelled(input, on: true);
                UnmarkInput(input);
            }
        }

        /// <summary>
        /// Handles a failure from the indexed input.
        /// </summary>
        /// <param name="id">The input index that failed.</param>
        /// <param name="cause">The upstream failure.</param>
        public abstract void OnError(int id, Exception cause);

        /// <summary>
        /// Called by the completion handler's <c>!IsPending(input)</c> branch, or when a dequeue leaves the input buffer depleted.
        /// </summary>
        /// <param name="input">The depleted input index.</param>
        public virtual void OnDepleted(int input) { }

        /// <summary>
        /// Called by the completion handler when no input element has been received and the completed-message counter equals the configured input count.
        /// </summary>
        public virtual void OnCompleteWhenNoInput() { }

        /// <summary>
        /// When the marked-state bit is set, increments tracking counters for clear depleted and pending bits, sets the marked bit again, and increments the marked count.
        /// </summary>
        /// <param name="input">The input index whose marked bit and counters are updated.</param>
        public void MarkInput(int input)
        {
            if (!IsMarked(input))
            {
                if (IsDepleted(input))
                    _markedDepleted++;
                if (IsPending(input))
                    _markedPending++;

                Marked(input, on: true);
                _markCount++;
            }
        }

        /// <summary>
        /// When the marked-state bit is clear, decrements tracking counters for clear depleted and pending bits, clears the marked bit, and decrements the marked count.
        /// </summary>
        /// <param name="input">The input index whose marked bit and counters are updated.</param>
        public void UnmarkInput(int input)
        {
            if (IsMarked(input))
            {
                if (IsDepleted(input))
                    _markedDepleted--;
                if (IsPending(input))
                    _markedPending--;

                Marked(input, on: false);
                _markCount--;
            }
        }

        /// <summary>
        /// Calls <see cref="MarkInput(int)"/> for every input index.
        /// </summary>
        public void MarkAllInputs()
        {
            for (var i = 0; i < _inputCount; i++)
                MarkInput(i);
        }

        /// <summary>
        /// Calls <see cref="UnmarkInput(int)"/> for every input index.
        /// </summary>
        public void UnmarkAllInputs()
        {
            for (var i = 0; i < _inputCount; i++)
                UnmarkInput(i);
        }

        /// <summary>
        /// Searches from the preferred index for an input whose marked and pending bits are both clear.
        /// </summary>
        /// <exception cref="IllegalStateException">No input has both bits clear.</exception>
        /// <returns>The first input index found by the bit checks.</returns>
        public int IdToDequeue()
        {
            var id = _preferredId;
            while (!(IsMarked(id) && IsPending(id)))
            {
                id++;
                if (id == _inputCount)
                    id = 0;
                if (id == _preferredId)
                    throw new IllegalStateException("Tried to dequeue without waiting for any input");
            }

            return id;
        }

        /// <summary>
        /// Attempts to dequeue from an input, proceeding only when its depleted bit is set and its pending bit is clear, then updates tracked state.
        /// </summary>
        /// <param name="id">The input index from which to remove an element.</param>
        /// <exception cref="ArgumentException">
        /// The depleted bit is clear, or the pending bit is set.
        /// </exception>
        /// <returns>The element returned by the input buffer.</returns>
        public object Dequeue(int id)
        {
            if (IsDepleted(id))
                throw new ArgumentException($"Can't dequeue from depleted {id}", nameof(id));
            if (!IsPending(id))
                throw new ArgumentException($"No pending input at {id}", nameof(id));

            _lastDequeuedId = id;
            var input = _inputs[id];
            var element = input.DequeueInputElement();

            if (!input.AreInputsAvailable)
            {
                if (IsMarked(id))
                    _markedPending--;
                Pending(id, on: false);
            }

            if (input.AreInputsDepleted)
            {
                if (IsMarked(id))
                    _markedDepleted++;
                Depleted(id, on: true);
                OnDepleted(id);
            }

            return element;
        }

        /// <summary>
        /// Finds an input with marked and pending bits clear, advances the preferred index past it, and attempts to dequeue from it.
        /// </summary>
        /// <returns>The element returned by the selected input buffer.</returns>
        public object DequeueAndYield() => DequeueAndYield(IdToDequeue());

        /// <summary>
        /// Sets the preferred index after the specified input, then attempts to dequeue from it.
        /// </summary>
        /// <param name="id">The input index passed to the dequeue checks.</param>
        /// <returns>The element returned by that input buffer.</returns>
        public object DequeueAndYield(int id)
        {
            _preferredId = (id + 1) % _inputCount;
            return Dequeue(id);
        }

        /// <summary>
        /// Sets the preferred index, searches for clear marked and pending bits, then attempts to dequeue from the selected input.
        /// </summary>
        /// <param name="preferred">The index from which the bit-check search starts.</param>
        /// <returns>The element returned by the selected input buffer.</returns>
        public object DequeuePreferring(int preferred)
        {
            _preferredId = preferred;
            var id = IdToDequeue();
            return Dequeue(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasState(int index, State flag)
        {
            return (_states[index] & flag) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetState(int index, State flag, bool on)
        {
            _states[index] = (State)(on ? (_states[index] | flag) : (_states[index] & ~flag));
        }

        /// <summary>
        /// Reports whether the canceled-state bit is clear for the indexed input.
        /// </summary>
        /// <param name="index">The input index to inspect.</param>
        /// <returns><see langword="true"/> when the canceled-state bit is clear.</returns>
        public bool IsCancelled(int index) => HasState(index, FanIn.Cancelled);

        private void Cancelled(int index, bool on) => SetState(index, FanIn.Cancelled, on);

        /// <summary>
        /// Reports whether the completed-state bit is clear for the indexed input.
        /// </summary>
        /// <param name="index">The input index to inspect.</param>
        /// <returns><see langword="true"/> when the completed-state bit is clear.</returns>
        public bool IsCompleted(int index) => HasState(index, FanIn.Completed);

        private void RegisterCompleted(int index)
        {
            _completedCounter++;
            SetState(index, FanIn.Completed, true);
        }

        /// <summary>
        /// Reports whether the depleted-state bit is clear for the indexed input.
        /// </summary>
        /// <param name="index">The input index to inspect.</param>
        /// <returns><see langword="true"/> when the depleted-state bit is clear.</returns>
        public bool IsDepleted(int index) => HasState(index, FanIn.Depleted);

        private void Depleted(int index, bool on) => SetState(index, FanIn.Depleted, on);

        /// <summary>
        /// Reports whether the pending-state bit is clear for the indexed input.
        /// </summary>
        /// <param name="index">The input index to inspect.</param>
        /// <returns><see langword="true"/> when the pending-state bit is clear.</returns>
        public bool IsPending(int index) => HasState(index, FanIn.Pending);

        private void Pending(int index, bool on) => SetState(index, FanIn.Pending, on);

        private bool IsMarked(int index) => HasState(index, FanIn.Marked);

        private void Marked(int index, bool on) => SetState(index, FanIn.Marked, on);
    }

    /// <summary>
    /// Defines actor messages and state flags used to identify indexed fan-in signals.
    /// </summary>
    public static class FanIn
    {
        /// <summary>
        /// Identifies a failure from one input.
        /// </summary>
        [Serializable]
        public readonly struct OnError : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the input that failed.
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// Gets the failure reported by the input.
            /// </summary>
            public readonly Exception Cause;

            /// <summary>
            /// Creates an indexed input-failure message.
            /// </summary>
            /// <param name="id">The index of the input that failed.</param>
            /// <param name="cause">The failure reported by that input.</param>
            public OnError(int id, Exception cause)
            {
                Id = id;
                Cause = cause;
            }
        }

        /// <summary>
        /// Identifies successful completion of one input.
        /// </summary>
        [Serializable]
        public readonly struct OnComplete : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the completed input.
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// Creates an indexed input-completion message.
            /// </summary>
            /// <param name="id">The index of the completed input.</param>
            public OnComplete(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// Carries an element received from one indexed input.
        /// </summary>
        [Serializable]
        public readonly struct OnNext : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the input that produced the element.
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// Gets the element received from the input.
            /// </summary>
            public readonly object Element;

            /// <summary>
            /// Creates an indexed input-element message.
            /// </summary>
            /// <param name="id">The index of the input that produced the element.</param>
            /// <param name="element">The element received from that input.</param>
            public OnNext(int id, object element)
            {
                Id = id;
                Element = element;
            }
        }

        /// <summary>
        /// Carries an upstream subscription for one indexed input.
        /// </summary>
        [Serializable]
        public readonly struct OnSubscribe : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// Gets the index of the input being subscribed.
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// Gets the upstream subscription.
            /// </summary>
            public readonly ISubscription Subscription;

            /// <summary>
            /// Creates an indexed upstream-subscription message.
            /// </summary>
            /// <param name="id">The index of the input receiving this subscription.</param>
            /// <param name="subscription">The upstream subscription.</param>
            public OnSubscribe(int id, ISubscription subscription)
            {
                Id = id;
                Subscription = subscription;
            }
        }

        /// <summary>
        /// Bit flag used to represent the marked state of an input.
        /// </summary>
        public const State Marked = 1;
        /// <summary>
        /// Bit flag used to represent the pending state of an input.
        /// </summary>
        public const State Pending = 2;
        /// <summary>
        /// Bit flag used to represent the depleted state of an input.
        /// </summary>
        public const State Depleted = 4;
        /// <summary>
        /// Bit flag used to represent the completed state of an input.
        /// </summary>
        public const State Completed = 8;
        /// <summary>
        /// Bit flag used to represent the canceled state of an input.
        /// </summary>
        public const State Cancelled = 16;
    }

    /// <summary>
    /// Base actor for indexed upstream inputs and a downstream output; a transfer phase determines how elements are emitted.
    /// </summary>
    /// <typeparam name="T">The type of elements accepted from and emitted to the stream.</typeparam>
    public abstract class FanIn<T> : ActorBase, IPump
    {
        #region Internal classes

        /// <summary>
        /// Subscriber that forwards Reactive Streams signals to the indexed input's actor.
        /// </summary>
        public readonly struct SubInput : ISubscriber<T>
        {
            private readonly IActorRef _impl;
            private readonly int _id;

            /// <summary>
            /// Creates an input subscriber bound to an actor and input index.
            /// </summary>
            /// <param name="impl">The actor receiving indexed input messages.</param>
            /// <param name="id">The input index associated with this subscriber.</param>
            public SubInput(IActorRef impl, int id)
            {
                _impl = impl;
                _id = id;
            }

            /// <summary>
            /// Forwards the upstream subscription as an indexed actor message.
            /// </summary>
            /// <param name="subscription">The upstream subscription.</param>
            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                _impl.Tell(new FanIn.OnSubscribe(_id, subscription));
            }

            /// <summary>
            /// Forwards the upstream failure as an indexed actor message.
            /// </summary>
            /// <param name="cause">The upstream failure.</param>
            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                _impl.Tell(new FanIn.OnError(_id, cause));
            }

            /// <summary>
            /// Forwards upstream completion as an indexed actor message.
            /// </summary>
            public void OnComplete() => _impl.Tell(new FanIn.OnComplete(_id));

            /// <summary>
            /// Validates and forwards an upstream element as an indexed actor message.
            /// </summary>
            /// <param name="element">The element received from upstream.</param>
            public void OnNext(T element)
            {
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                _impl.Tell(new FanIn.OnNext(_id, element));
            }
        }

        private sealed class AnonymousInputBunch : InputBunch
        {
            private readonly FanIn<T> _that;

            public AnonymousInputBunch(int inputCount, int bufferSize, FanIn<T> that) : base(inputCount, bufferSize, that)
            {
                _that = that;
            }

            public override void OnError(int id, Exception cause) => _that.Fail(cause);

            public override void OnCompleteWhenNoInput() => _that.PumpFinished();
        }

        #endregion

        /// <summary>
        /// Gets the materializer settings used by this actor.
        /// </summary>
        protected readonly ActorMaterializerSettings Settings;
        /// <summary>
        /// Gets the number of upstream inputs handled by this actor.
        /// </summary>
        protected readonly int InputCount;
        /// <summary>
        /// Gets the downstream output manager.
        /// </summary>
        protected readonly SimpleOutputs PrimaryOutputs;
        /// <summary>
        /// Gets the bunch that buffers and tracks the indexed upstream inputs.
        /// </summary>
        protected readonly InputBunch InputBunch;

        /// <summary>
        /// Creates a fan-in actor with one buffered input per upstream.
        /// </summary>
        /// <param name="settings">Materializer settings used to configure input buffers.</param>
        /// <param name="inputCount">The number of upstream inputs.</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the pump has not been initialized with a phase.
        /// </exception>
        protected FanIn(ActorMaterializerSettings settings, int inputCount)
        {
            Settings = settings;
            InputCount = inputCount;
            PrimaryOutputs = new SimpleOutputs(Self, this);
            InputBunch = new AnonymousInputBunch(inputCount, settings.MaxInputBufferSize, this);
            
            TransferState = NotInitialized.Instance;
            CurrentAction = () => { throw new IllegalStateException("Pump has been not initialized with a phase"); };
        }

        #region Actor impl

        /// <summary>
        /// Gets the actor logger, creating it on first access.
        /// </summary>
        protected ILoggingAdapter Log => _log ??= Context.GetLogger();
        private ILoggingAdapter _log;

        /// <summary>
        /// Sets the pump phase to completed, passes the failure to the downstream output manager, and runs the pump.
        /// </summary>
        /// <param name="cause">The failure to signal downstream.</param>
        protected void Fail(Exception cause)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Fail due to {0}", cause.Message);

            NextPhase(Pumps.CompletedPhase);
            PrimaryOutputs.Error(cause);
            Pump();
        }

        /// <summary>
        /// Calls <c>InputBunch.Cancel()</c>, passes abrupt termination to the downstream output manager, and calls the base implementation.
        /// </summary>
        protected override void PostStop()
        {
            InputBunch.Cancel();
            PrimaryOutputs.Error(new AbruptTerminationException(Self));
            base.PostStop();
        }

        /// <summary>
        /// Rejects restart because the fan-in actor's state cannot be reconstructed.
        /// </summary>
        /// <param name="reason">The exception that caused the restart attempt.</param>
        /// <exception cref="IllegalStateException">Restart is unsupported because the actor state cannot be reconstructed.
        /// </exception>
        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted");
        }

        /// <summary>
        /// Routes indexed input messages and downstream publisher messages to their handlers.
        /// </summary>
        /// <param name="message">The actor message to route.</param>
        /// <returns><see langword="true"/> when the input or output handler handles the message.</returns>
        protected override bool Receive(object message)
            => InputBunch.SubReceive.CurrentReceive(message) || PrimaryOutputs.SubReceive.CurrentReceive(message);

        #endregion

        #region Pump implementation

        /// <summary>
        /// Gets or sets the current transfer state of the fan-in pump.
        /// </summary>
        public TransferState TransferState { get; set; }

        /// <summary>
        /// Gets or sets the action executed by the fan-in pump.
        /// </summary>
        public Action CurrentAction { get; set; }

        /// <summary>
        /// Gets whether the fan-in pump is complete.
        /// </summary>
        public bool IsPumpFinished => TransferState.IsCompleted;

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
        /// Routes a pump-action exception to the fan-in failure handler.
        /// </summary>
        /// <param name="e">The exception thrown by the current action.</param>
        public void PumpFailed(Exception e) => Fail(e);

        /// <summary>
        /// Calls <c>InputBunch.Cancel()</c>, completes the downstream output manager, and stops the actor.
        /// </summary>
        public void PumpFinished()
        {
            InputBunch.Cancel();
            PrimaryOutputs.Complete();
            Context.Stop(Self);
        }

        #endregion
    }
}
