//-----------------------------------------------------------------------
// <copyright file="Transfer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Pattern;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    public class SubReceive
    {
        private Receive _currentReceive;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initial">TBD</param>
        public SubReceive(Receive initial)
        {
            _currentReceive = initial;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Receive CurrentReceive => _currentReceive;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        public void Become(Receive receive) => _currentReceive = receive;
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal interface IInputs
    {
        /// <summary>
        /// TBD
        /// </summary>
        TransferState NeedsInput { get; }
        /// <summary>
        /// TBD
        /// </summary>
        TransferState NeedsInputOrComplete { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        object DequeueInputElement();

        /// <summary>
        /// TBD
        /// </summary>
        SubReceive SubReceive { get; }
        /// <summary>
        /// TBD
        /// </summary>
        void Cancel();

        /// <summary>
        /// TBD
        /// </summary>
        bool IsClosed { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// TBD
        /// </summary>
        bool AreInputsDepleted { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool AreInputsAvailable { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class DefaultInputTransferStates
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inputs">TBD</param>
        /// <returns>TBD</returns>
        public static TransferState NeedsInput(IInputs inputs)
            => new LambdaTransferState(() => inputs.AreInputsAvailable, () => inputs.AreInputsDepleted);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inputs">TBD</param>
        /// <returns>TBD</returns>
        public static TransferState NeedsInputOrComplete(IInputs inputs)
            => new LambdaTransferState(() => inputs.AreInputsAvailable || inputs.AreInputsDepleted, () => false);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal interface IOutputs
    {
        /// <summary>
        /// TBD
        /// </summary>
        SubReceive SubReceive { get; }
        /// <summary>
        /// TBD
        /// </summary>
        TransferState NeedsDemand { get; }
        /// <summary>
        /// TBD
        /// </summary>
        TransferState NeedsDemandOrCancel { get; }
        /// <summary>
        /// TBD
        /// </summary>
        long DemandCount { get; }

        /// <summary>
        /// TBD
        /// </summary>
        bool IsDemandAvailable { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        void EnqueueOutputElement(object element);

        /// <summary>
        /// TBD
        /// </summary>
        void Complete();
        /// <summary>
        /// TBD
        /// </summary>
        void Cancel();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        void Error(Exception e);

        /// <summary>
        /// TBD
        /// </summary>
        bool IsClosed { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsOpen { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class DefaultOutputTransferStates 
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outputs">TBD</param>
        /// <returns>TBD</returns>
        public static TransferState NeedsDemand(IOutputs outputs) => new LambdaTransferState(() 
            => outputs.IsDemandAvailable, () => outputs.IsClosed);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outputs">TBD</param>
        /// <returns>TBD</returns>
        public static TransferState NeedsDemandOrCancel(IOutputs outputs)
            => new LambdaTransferState(() => outputs.IsDemandAvailable || outputs.IsClosed, () => false);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class TransferState
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsReady { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsCompleted { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool IsExecutable => IsReady && !IsCompleted;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public TransferState Or(TransferState other)
            => new LambdaTransferState(() => IsReady || other.IsReady, () => IsCompleted && other.IsCompleted);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public TransferState And(TransferState other)
            => new LambdaTransferState(() => IsReady && other.IsReady, () => IsCompleted || other.IsCompleted);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class LambdaTransferState : TransferState
    {
        private readonly Func<bool> _isReady;
        private readonly Func<bool> _isCompleted;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsReady => _isReady();
        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsCompleted => _isCompleted();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="isReady">TBD</param>
        /// <param name="isCompleted">TBD</param>
        public LambdaTransferState(Func<bool> isReady, Func<bool> isCompleted)
        {
            _isReady = isReady;
            _isCompleted = isCompleted;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Completed : TransferState
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Completed Instance = new Completed();

        private Completed()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsReady => false;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsCompleted => true;
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class NotInitialized : TransferState
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly NotInitialized Instance = new NotInitialized();

        private NotInitialized()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsReady => false;
        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsCompleted => false;
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class WaitingForUpstreamSubscription : TransferState
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int Remaining;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TransferPhase AndThen;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remaining">TBD</param>
        /// <param name="andThen">TBD</param>
        public WaitingForUpstreamSubscription(int remaining, TransferPhase andThen)
        {
            Remaining = remaining;
            AndThen = andThen;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsReady => false;
        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsCompleted => false;
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Always : TransferState
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Always Instance = new Always();

        private Always()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsReady => true;
        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsCompleted => false;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public struct TransferPhase
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TransferState Precondition;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Action Action;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="precondition">TBD</param>
        /// <param name="action">TBD</param>
        public TransferPhase(TransferState precondition, Action action) : this()
        {
            Precondition = precondition;
            Action = action;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IPump
    {
        /// <summary>
        /// TBD
        /// </summary>
        TransferState TransferState { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        Action CurrentAction { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsPumpFinished { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="waitForUpstream">TBD</param>
        /// <param name="andThen">TBD</param>
        void InitialPhase(int waitForUpstream, TransferPhase andThen);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="waitForUpstream">TBD</param>
        void WaitForUpstream(int waitForUpstream);
        /// <summary>
        /// TBD
        /// </summary>
        void GotUpstreamSubscription();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="phase">TBD</param>
        void NextPhase(TransferPhase phase);

        // Exchange input buffer elements and output buffer "requests" until one of them becomes empty.
        // Generate upstream requestMore for every Nth consumed input element
        /// <summary>
        /// TBD
        /// </summary>
        void Pump();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        void PumpFailed(Exception e);
        /// <summary>
        /// TBD
        /// </summary>
        void PumpFinished();
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class PumpBase : IPump
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PumpBase" /> class.
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the pump has not been initialized with a phase.
        /// </exception>
        protected PumpBase()
        {
            TransferState = NotInitialized.Instance;
            CurrentAction = () => { throw new IllegalStateException("Pump has not been initialized with a phase"); };
        }

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
        public abstract void PumpFailed(Exception e);

        /// <summary>
        /// TBD
        /// </summary>
        public abstract void PumpFinished();
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class Pumps
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the pump has not been initialized with a phase.
        /// </exception>
        public static void Init(this IPump self)
        {
            self.TransferState = NotInitialized.Instance;
            self.CurrentAction = () => { throw new IllegalStateException("Pump has not been initialized with a phase"); };
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the action of the completed phase tried to execute.
        /// </exception>
        public static readonly TransferPhase CompletedPhase = new TransferPhase(Completed.Instance, () =>
        {
            throw new IllegalStateException("The action of completed phase must never be executed");
        });

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="waitForUpstream">TBD</param>
        /// <param name="andThen">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="waitForUpstream"/> is less than one.
        /// </exception>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the initial state is not <see cref="NotInitialized.Instance"/>.
        /// </exception>
        public static void InitialPhase(this IPump self, int waitForUpstream, TransferPhase andThen)
        {
            if (waitForUpstream < 1)
                throw new ArgumentException($"WaitForUpstream must be >= 1 (was {waitForUpstream})");
            
            if(self.TransferState != NotInitialized.Instance)
                throw new IllegalStateException($"Initial state expected NotInitialized, but got {self.TransferState}");

            self.TransferState = new WaitingForUpstreamSubscription(waitForUpstream, andThen);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="waitForUpstream">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="waitForUpstream"/> is less than one.
        /// </exception>
        public static void WaitForUpstream(this IPump self, int waitForUpstream)
        {
            if(waitForUpstream < 1) 
                throw new ArgumentException($"WaitForUpstream must be >= 1 (was {waitForUpstream})");

            self.TransferState = new WaitingForUpstreamSubscription(waitForUpstream, new TransferPhase(self.TransferState, self.CurrentAction));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        public static void GotUpstreamSubscription(this IPump self)
        {
            if (self.TransferState is WaitingForUpstreamSubscription)
            {
                var t = (WaitingForUpstreamSubscription) self.TransferState;
                if (t.Remaining == 1)
                {
                    self.TransferState = t.AndThen.Precondition;
                    self.CurrentAction = t.AndThen.Action;
                }
                else
                    self.TransferState = new WaitingForUpstreamSubscription(t.Remaining - 1, t.AndThen);
            }

            self.Pump();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="phase">TBD</param>
        public static void NextPhase(this IPump self, TransferPhase phase)
        {
            if (self.TransferState is WaitingForUpstreamSubscription)
            {
                var w = (WaitingForUpstreamSubscription) self.TransferState;
                self.TransferState = new WaitingForUpstreamSubscription(w.Remaining, phase);
            }
            else
            {
                self.TransferState = phase.Precondition;
                self.CurrentAction = phase.Action;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static bool IsPumpFinished(this IPump self) => self.TransferState.IsCompleted;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        public static void Pump(this IPump self)
        {
            try
            {
                while (self.TransferState.IsExecutable)
                    self.CurrentAction();
            }
            catch (Exception e)
            {
                self.PumpFailed(e);
            }

            if(self.IsPumpFinished)
                self.PumpFinished();
        }
    }
}
