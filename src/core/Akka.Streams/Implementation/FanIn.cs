//-----------------------------------------------------------------------
// <copyright file="FanIn.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

    internal abstract class InputBunch
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

        public readonly TransferState AllOfMarkedInputs;
        public readonly TransferState AnyOfMarkedInputs;
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

        protected int LastDequeuedId => _lastDequeuedId;

        public bool IsAllCompleted => _inputCount == _completedCounter;

        public TransferState InputsAvailableFor(int id)
        {
            return new LambdaTransferState(
                isCompleted: () => IsDepleted(id) || IsCancelled(id) || (!IsPending(id) && IsCompleted(id)),
                isReady: () => _markedPending > 0);
        }

        public TransferState InputsOrCompleteAvailableFor(int id)
        {
            return new LambdaTransferState(
                isCompleted: () => false,
                isReady: () => IsPending(id) || IsDepleted(id));
        }

        public void Cancel()
        {
            if (!_allCancelled)
            {
                _allCancelled = true;
                for (var i = 0; i < _inputs.Length; i++)
                    Cancel(i);
            }
        }

        public void Cancel(int input)
        {
            if (!IsCancelled(input))
            {
                _inputs[input].Cancel();
                Cancelled(input, on: true);
                UnmarkInput(input);
            }
        }

        public abstract void OnError(int id, Exception cause);

        public virtual void OnDepleted(int input) { }

        public virtual void OnCompleteWhenNoInput() { }

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

        public void MarkAllInputs()
        {
            for (var i = 0; i < _inputCount; i++)
                MarkInput(i);
        }

        public void UnmarkAllInputs()
        {
            for (var i = 0; i < _inputCount; i++)
                UnmarkInput(i);
        }

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

        public object Dequeue(int id)
        {
            if (IsDepleted(id))
                throw new ArgumentException("Can't dequeue from depleted " + id, nameof(id));
            if (!IsPending(id))
                throw new ArgumentException("No pending input at " + id, nameof(id));

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

        public object DequeueAndYield() => DequeueAndYield(IdToDequeue());

        public object DequeueAndYield(int id)
        {
            _preferredId = (id + 1) % _inputCount;
            return Dequeue(id);
        }

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

        public bool IsCancelled(int index) => HasState(index, FanIn.Cancelled);

        private void Cancelled(int index, bool on) => SetState(index, FanIn.Cancelled, @on);

        public bool IsCompleted(int index) => HasState(index, FanIn.Completed);

        private void RegisterCompleted(int index)
        {
            _completedCounter++;
            SetState(index, FanIn.Completed, true);
        }

        public bool IsDepleted(int index) => HasState(index, FanIn.Depleted);

        private void Depleted(int index, bool on) => SetState(index, FanIn.Depleted, @on);

        public bool IsPending(int index) => HasState(index, FanIn.Pending);

        private void Pending(int index, bool on) => SetState(index, FanIn.Pending, @on);

        private bool IsMarked(int index) => HasState(index, FanIn.Marked);

        private void Marked(int index, bool on) => SetState(index, FanIn.Marked, @on);
    }

    internal static class FanIn
    {
        [Serializable]
        public struct OnError : INoSerializationVerificationNeeded
        {
            public readonly int Id;
            public readonly Exception Cause;

            public OnError(int id, Exception cause)
            {
                Id = id;
                Cause = cause;
            }
        }

        [Serializable]
        public struct OnComplete : INoSerializationVerificationNeeded
        {
            public readonly int Id;

            public OnComplete(int id)
            {
                Id = id;
            }
        }

        [Serializable]
        public struct OnNext : INoSerializationVerificationNeeded
        {
            public readonly int Id;
            public readonly object Element;

            public OnNext(int id, object element)
            {
                Id = id;
                Element = element;
            }
        }

        [Serializable]
        public struct OnSubscribe : INoSerializationVerificationNeeded
        {
            public readonly int Id;
            public readonly ISubscription Subscription;

            public OnSubscribe(int id, ISubscription subscription)
            {
                Id = id;
                Subscription = subscription;
            }
        }

        public const State Marked = 1;
        public const State Pending = 2;
        public const State Depleted = 4;
        public const State Completed = 8;
        public const State Cancelled = 16;
    }

    internal abstract class FanIn<T> : ActorBase, IPump
    {
        #region Internal classes

        public struct SubInput : ISubscriber<T>
        {
            private readonly IActorRef _impl;
            private readonly int _id;

            public SubInput(IActorRef impl, int id)
            {
                _impl = impl;
                _id = id;
            }

            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                _impl.Tell(new FanIn.OnSubscribe(_id, subscription));
            }

            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                _impl.Tell(new FanIn.OnError(_id, cause));
            }

            public void OnComplete() => _impl.Tell(new FanIn.OnComplete(_id));

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

        protected readonly ActorMaterializerSettings Settings;
        protected readonly int InputCount;
        protected readonly SimpleOutputs PrimaryOutputs;
        protected readonly InputBunch InputBunch;

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

        private ILoggingAdapter _log;
        protected ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        protected void Fail(Exception cause)
        {
            if (Settings.IsDebugLogging)
                Log.Debug("Fail due to {0}", cause.Message);

            NextPhase(Pumps.CompletedPhase);
            PrimaryOutputs.Error(cause);
            Pump();
        }

        protected override void PostStop()
        {
            InputBunch.Cancel();
            PrimaryOutputs.Error(new AbruptTerminationException(Self));
            base.PostStop();
        }

        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            throw new IllegalStateException("This actor cannot be restarted");
        }

        protected override bool Receive(object message)
            => InputBunch.SubReceive.CurrentReceive(message) || PrimaryOutputs.SubReceive.CurrentReceive(message);

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
            InputBunch.Cancel();
            PrimaryOutputs.Complete();
            Context.Stop(Self);
        }

        #endregion
    }
}