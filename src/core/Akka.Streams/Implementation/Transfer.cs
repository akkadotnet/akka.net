using System;
using Akka.Actor;
using Akka.Pattern;

namespace Akka.Streams.Implementation
{
    internal class SubReceive
    {
        private Receive _currentReceive;

        public SubReceive(Receive initial)
        {
            _currentReceive = initial;
        }

        public Receive CurrentReceive => _currentReceive;

        public void Become(Receive receive)
        {
            _currentReceive = receive;
        }
    }

    internal interface IInputs
    {
        TransferState NeedsInput { get; }
        TransferState NeedsInputOrComplete { get; }
        
        object DequeueInputElement();

        SubReceive SubReceive { get; }
        void Cancel();

        bool IsClosed { get; }
        bool IsOpen { get; }

        bool AreInputsDepleted { get; }
        bool AreInputsAvailable { get; }
    }

    internal static class DefaultInputTransferStates 
    {
        public static TransferState NeedsInput(IInputs inputs)
        {
            return new AnonymousTransferState(inputs.AreInputsAvailable, inputs.AreInputsDepleted); 
        }

        public static TransferState NeedsInputOrComplete(IInputs inputs)
        {
            return new AnonymousTransferState(inputs.AreInputsAvailable || inputs.AreInputsDepleted, false);
        }
    }

    internal interface IOutputs
    {
        SubReceive SubReceive { get; }
        TransferState NeedsDemand { get; }
        TransferState NeedsDemandOrCancel { get; }
        long DemandCount { get; }

        bool IsDemandAvailable { get; }
        void EnqueueOutputElement(object element);

        void Complete();
        void Cancel();
        void Error(Exception e);

        bool IsClosed { get; }
        bool IsOpen { get; }
    }

    internal static class DefaultOutputTransferStates 
    {
        public static TransferState NeedsDemand(IOutputs outputs)
        {
            return new AnonymousTransferState(outputs.IsDemandAvailable, outputs.IsClosed);
        }

        public static TransferState NeedsDemandOrCancel(IOutputs outputs)
        {
            return new AnonymousTransferState(outputs.IsDemandAvailable || outputs.IsClosed, false);
        }
    }

    internal abstract class TransferState
    {
        public abstract bool IsReady { get; }
        public abstract bool IsCompleted { get; }
        public bool IsExecutable { get { return IsReady && !IsCompleted; } }

        public TransferState Or(TransferState other)
        {
            return new AnonymousTransferState(IsReady || other.IsReady, IsCompleted && other.IsCompleted);
        }

        public TransferState And(TransferState other)
        {
            return new AnonymousTransferState(IsReady && other.IsReady, IsCompleted || other.IsCompleted);
        }
    }

    internal sealed class AnonymousTransferState : TransferState
    {
        private readonly bool _isReady;
        private readonly bool _isCompleted;

        public override bool IsReady { get { return _isReady; } }
        public override bool IsCompleted { get { return _isCompleted; } }

        public AnonymousTransferState(bool isReady, bool isCompleted)
        {
            _isReady = isReady;
            _isCompleted = isCompleted;
        }
    }

    internal sealed class LambdaTransferState : TransferState
    {
        private readonly Func<bool> _isReady;
        private readonly Func<bool> _isCompleted;

        public override bool IsReady { get { return _isReady(); } }
        public override bool IsCompleted { get { return _isCompleted(); } }

        public LambdaTransferState(Func<bool> isReady, Func<bool> isCompleted)
        {
            _isReady = isReady;
            _isCompleted = isCompleted;
        }
    }

    internal sealed class Completed : TransferState
    {
        public static readonly Completed Instance = new Completed();

        private Completed()
        {
        }

        public override bool IsReady
        {
            get { return false; }
        }

        public override bool IsCompleted
        {
            get { return true; }
        }
    }

    internal sealed class NotInitialized : TransferState
    {
        public static readonly NotInitialized Instance = new NotInitialized();

        private NotInitialized()
        {
        }

        public override bool IsReady { get { return false; } }
        public override bool IsCompleted { get { return false; } }
    }

    internal class WaitingForUpstreamSubscription : TransferState
    {
        public readonly int Remaining;
        public readonly TransferPhase AndThen;

        public WaitingForUpstreamSubscription(int remaining, TransferPhase andThen)
        {
            Remaining = remaining;
            AndThen = andThen;
        }

        public override bool IsReady { get { return false; } }
        public override bool IsCompleted { get { return false; } }
    }

    internal sealed class Always : TransferState
    {
        public static readonly Always Instance = new Always();

        private Always()
        {
        }

        public override bool IsReady { get { return true; } }
        public override bool IsCompleted { get { return false; } }
    }

    internal struct TransferPhase
    {
        public readonly TransferState Precondintion;
        public readonly Action Action;

        public TransferPhase(TransferState precondintion, Action action) : this()
        {
            Precondintion = precondintion;
            Action = action;
        }
    }

    internal interface IPump
    {
        TransferState TransferState { get; set; }
        Action CurrentAction { get; set; }
        bool IsPumpFinished { get; }

        void InitialPhase(int waitForUpstream, TransferPhase andThen);
        void WaitForUpstream(int waitForUpstream);
        void GotUpstreamSubscription();
        void NextPhase(TransferPhase phase);

        // Exchange input buffer elements and output buffer "requests" until one of them becomes empty.
        // Generate upstream requestMore for every Nth consumed input element
        void Pump();
        void PumpFailed(Exception e);
        void PumpFinished();
    }

    internal abstract class PumpBase : IPump
    {
        protected PumpBase()
        {
            TransferState = NotInitialized.Instance;
            CurrentAction = () => { throw new IllegalStateException("Pump has not been initialized with a phase");  };
        }

        public TransferState TransferState { get; set; }
        public Action CurrentAction { get; set; }
        public bool IsPumpFinished { get { return TransferState.IsCompleted; } }
        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
        {
            Pumps.InitialPhase(this, waitForUpstream, andThen);
        }

        public void WaitForUpstream(int waitForUpstream)
        {
            Pumps.WaitForUpstream(this, waitForUpstream);
        }

        public void GotUpstreamSubscription()
        {
            Pumps.GotUpstreamSubscription(this);
        }

        public void NextPhase(TransferPhase phase)
        {
            Pumps.NextPhase(this, phase);
        }

        public void Pump()
        {
            Pumps.Pump(this);
        }

        public abstract void PumpFailed(Exception e);
        public abstract void PumpFinished();
    }

    internal static class Pumps
    {
        public static void Init(this IPump self)
        {
            self.TransferState = NotInitialized.Instance;
            self.CurrentAction = () => { throw new IllegalStateException("Pump has not been initialized with a phase"); };
        }

        public static readonly TransferPhase CompletedPhase = new TransferPhase(Completed.Instance, () =>
        {
            throw new IllegalStateException("The action of completed phase must never be executed");
        });

        public static void InitialPhase(this IPump self, int waitForUpstream, TransferPhase andThen)
        {
            if (waitForUpstream < 1)
                throw new ArgumentException(string.Format("WaitForUpstream must be >= 1 (was {0})", waitForUpstream));
            
            if(self.TransferState != NotInitialized.Instance)
                throw new IllegalStateException(string.Format("Initial state expected NotInitialized, but got {0}", self.TransferState));

            self.TransferState = new WaitingForUpstreamSubscription(waitForUpstream, andThen);
        }

        public static void WaitForUpstream(this IPump self, int waitForUpstream)
        {
            if(waitForUpstream < 1) 
                throw new ArgumentException(string.Format("WaitForUpstream must be >= 1 (was {0})", waitForUpstream));

            self.TransferState = new WaitingForUpstreamSubscription(waitForUpstream, new TransferPhase(self.TransferState, self.CurrentAction));
        }

        public static void GotUpstreamSubscription(this IPump self)
        {
            if (self.TransferState is WaitingForUpstreamSubscription)
            {
                var t = (WaitingForUpstreamSubscription) self.TransferState;
                if (t.Remaining == 1)
                {
                    self.TransferState = t.AndThen.Precondintion;
                    self.CurrentAction = t.AndThen.Action;
                }
                else
                {
                    self.TransferState = new WaitingForUpstreamSubscription(t.Remaining-1, t.AndThen);
                }
            }

            self.Pump();
        }

        public static void NextPhase(this IPump self, TransferPhase phase)
        {
            if (self.TransferState is WaitingForUpstreamSubscription)
            {
                var w = (WaitingForUpstreamSubscription) self.TransferState;
                self.TransferState = new WaitingForUpstreamSubscription(w.Remaining, phase);
            }
            else
            {
                self.TransferState = phase.Precondintion;
                self.CurrentAction = phase.Action;
            }
        }

        public static bool IsPumpFinished(this IPump self)
        {
            return self.TransferState.IsCompleted;
        }

        public static void Pump(this IPump self)
        {
            try
            {
                while (self.TransferState.IsExecutable)
                {
                    self.CurrentAction();
                }
            }
            catch (Exception e)
            {
                self.PumpFailed(e);
            }

            if(self.IsPumpFinished) self.PumpFinished();
        }
    }
}