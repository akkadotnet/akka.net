//-----------------------------------------------------------------------
// <copyright file="FanIn.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// TBD
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
        /// TBD
        /// </summary>
        public readonly TransferState AllOfMarkedInputs;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TransferState AnyOfMarkedInputs;
        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="inputCount">TBD</param>
        /// <param name="bufferSize">TBD</param>
        /// <param name="pump">TBD</param>
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
            SubReceive = new SubReceive(msg => msg.Match()
                .With<FanIn.OnSubscribe>(subscribe => _inputs[subscribe.Id].SubReceive.CurrentReceive(new Actors.OnSubscribe(subscribe.Subscription)))
                .With<FanIn.OnNext>(next =>
                {
                    var id = next.Id;
                    if (IsMarked(id) && !IsPending(id))
                        _markedPending++;
                    Pending(id, on: true);
                    _receivedInput = true;
                    _inputs[id].SubReceive.CurrentReceive(new Actors.OnNext(next.Element));
                })
                .With<FanIn.OnComplete>(complete =>
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
                })
                .With<FanIn.OnError>(error => OnError(error.Id, error.Cause))
                .WasHandled);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected int LastDequeuedId => _lastDequeuedId;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsAllCompleted => _inputCount == _completedCounter;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public TransferState InputsAvailableFor(int id)
        {
            return new LambdaTransferState(
                isCompleted: () => IsDepleted(id) || IsCancelled(id) || (!IsPending(id) && IsCompleted(id)),
                isReady: () => _markedPending > 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public TransferState InputsOrCompleteAvailableFor(int id)
        {
            return new LambdaTransferState(
                isCompleted: () => false,
                isReady: () => IsPending(id) || IsDepleted(id));
        }

        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="input">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="cause">TBD</param>
        public abstract void OnError(int id, Exception cause);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="input">TBD</param>
        public virtual void OnDepleted(int input) { }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual void OnCompleteWhenNoInput() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="input">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="input">TBD</param>
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
        /// TBD
        /// </summary>
        public void MarkAllInputs()
        {
            for (var i = 0; i < _inputCount; i++)
                MarkInput(i);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void UnmarkAllInputs()
        {
            for (var i = 0; i < _inputCount; i++)
                UnmarkInput(i);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when dequeuing with no input.
        /// TBD</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <exception cref="ArgumentException">TBD
        /// This exception is thrown when either dequeuing from an empty <paramref name="id"/> or there are no pending inputs.
        /// </exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public object DequeueAndYield() => DequeueAndYield(IdToDequeue());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public object DequeueAndYield(int id)
        {
            _preferredId = (id + 1) % _inputCount;
            return Dequeue(id);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="preferred">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <returns>TBD</returns>
        public bool IsCancelled(int index) => HasState(index, FanIn.Cancelled);

        private void Cancelled(int index, bool on) => SetState(index, FanIn.Cancelled, on);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <returns>TBD</returns>
        public bool IsCompleted(int index) => HasState(index, FanIn.Completed);

        private void RegisterCompleted(int index)
        {
            _completedCounter++;
            SetState(index, FanIn.Completed, true);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <returns>TBD</returns>
        public bool IsDepleted(int index) => HasState(index, FanIn.Depleted);

        private void Depleted(int index, bool on) => SetState(index, FanIn.Depleted, on);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="index">TBD</param>
        /// <returns>TBD</returns>
        public bool IsPending(int index) => HasState(index, FanIn.Pending);

        private void Pending(int index, bool on) => SetState(index, FanIn.Pending, on);

        private bool IsMarked(int index) => HasState(index, FanIn.Marked);

        private void Marked(int index, bool on) => SetState(index, FanIn.Marked, on);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class FanIn
    {
        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct OnError : INoSerializationVerificationNeeded, IDeadLetterSuppression
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
            /// <param name="id">TBD</param>
            /// <param name="cause">TBD</param>
            public OnError(int id, Exception cause)
            {
                Id = id;
                Cause = cause;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct OnComplete : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="id">TBD</param>
            public OnComplete(int id)
            {
                Id = id;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct OnNext : INoSerializationVerificationNeeded, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly object Element;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="id">TBD</param>
            /// <param name="element">TBD</param>
            public OnNext(int id, object element)
            {
                Id = id;
                Element = element;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public struct OnSubscribe : INoSerializationVerificationNeeded, IDeadLetterSuppression
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
            /// <param name="id">TBD</param>
            /// <param name="subscription">TBD</param>
            /// <returns>TBD</returns>
            public OnSubscribe(int id, ISubscription subscription)
            {
                Id = id;
                Subscription = subscription;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public const State Marked = 1;
        /// <summary>
        /// TBD
        /// </summary>
        public const State Pending = 2;
        /// <summary>
        /// TBD
        /// </summary>
        public const State Depleted = 4;
        /// <summary>
        /// TBD
        /// </summary>
        public const State Completed = 8;
        /// <summary>
        /// TBD
        /// </summary>
        public const State Cancelled = 16;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public abstract class FanIn<T> : ActorBase, IPump
    {
        #region Internal classes

        /// <summary>
        /// TBD
        /// </summary>
        public struct SubInput : ISubscriber<T>
        {
            private readonly IActorRef _impl;
            private readonly int _id;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="impl">TBD</param>
            /// <param name="id">TBD</param>
            public SubInput(IActorRef impl, int id)
            {
                _impl = impl;
                _id = id;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="subscription">TBD</param>
            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                _impl.Tell(new FanIn.OnSubscribe(_id, subscription));
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                _impl.Tell(new FanIn.OnError(_id, cause));
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void OnComplete() => _impl.Tell(new FanIn.OnComplete(_id));

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
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
        /// TBD
        /// </summary>
        protected readonly ActorMaterializerSettings Settings;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly int InputCount;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly SimpleOutputs PrimaryOutputs;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly InputBunch InputBunch;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="inputCount">TBD</param>
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
        /// TBD
        /// </summary>
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());
        private ILoggingAdapter _log;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        protected void Fail(Exception cause)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Fail due to {0}", cause.Message);

            NextPhase(Pumps.CompletedPhase);
            PrimaryOutputs.Error(cause);
            Pump();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            InputBunch.Cancel();
            PrimaryOutputs.Error(new AbruptTerminationException(Self));
            base.PostStop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <exception cref="IllegalStateException">TBD
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
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
            => InputBunch.SubReceive.CurrentReceive(message) || PrimaryOutputs.SubReceive.CurrentReceive(message);

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
        public bool IsPumpFinished => TransferState.IsCompleted;

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
            InputBunch.Cancel();
            PrimaryOutputs.Complete();
            Context.Stop(Self);
        }

        #endregion
    }
}
