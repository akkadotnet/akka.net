//-----------------------------------------------------------------------
// <copyright file="Transfer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Pattern;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Mutable holder for the receive handler used by an actor transfer endpoint.
    /// </summary>
    public class SubReceive
    {
        private Receive _currentReceive;

        /// <summary>
        /// Creates a receive holder with the given initial handler.
        /// </summary>
        /// <param name="initial">The handler to use until another is installed.</param>
        public SubReceive(Receive initial)
        {
            _currentReceive = initial;
        }

        /// <summary>
        /// Gets the currently installed receive handler.
        /// </summary>
        public Receive CurrentReceive => _currentReceive;

        /// <summary>
        /// Replaces the currently installed receive handler.
        /// </summary>
        /// <param name="receive">The handler to install.</param>
        public void Become(Receive receive) => _currentReceive = receive;
    }

    /// <summary>
    /// Exposes the input-side state and operations used by a transfer pump.
    /// </summary>
    internal interface IInputs
    {
        /// <summary>
        /// Gets the state that is ready when input is available and completes when input is depleted.
        /// </summary>
        TransferState NeedsInput { get; }
        /// <summary>
        /// Gets the state that is ready when input is available or depleted and never completes.
        /// </summary>
        TransferState NeedsInputOrComplete { get; }

        /// <summary>
        /// Removes and returns the next available input element.
        /// </summary>
        /// <returns>The next queued input element.</returns>
        object DequeueInputElement();

        /// <summary>
        /// Gets the actor receive handler for input-side messages.
        /// </summary>
        SubReceive SubReceive { get; }
        /// <summary>
        /// Cancels the input side.
        /// </summary>
        void Cancel();

        /// <summary>
        /// Gets whether the input side has closed.
        /// </summary>
        bool IsClosed { get; }
        /// <summary>
        /// Gets whether the input side remains open.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Gets whether all input sources have been depleted.
        /// </summary>
        bool AreInputsDepleted { get; }
        /// <summary>
        /// Gets whether at least one input element is available.
        /// </summary>
        bool AreInputsAvailable { get; }
    }

    /// <summary>
    /// Creates the default transfer states used to wait for input.
    /// </summary>
    internal static class DefaultInputTransferStates
    {
        /// <summary>
        /// Creates a state that waits for input and completes when all input is depleted.
        /// </summary>
        /// <param name="inputs">The input endpoint being monitored.</param>
        /// <returns>A state backed by input availability and depletion checks.</returns>
        public static TransferState NeedsInput(IInputs inputs)
            => new LambdaTransferState(() => inputs.AreInputsAvailable, () => inputs.AreInputsDepleted);

        /// <summary>
        /// Creates a state that is ready for input or completion and does not complete by itself.
        /// </summary>
        /// <param name="inputs">The input endpoint being monitored.</param>
        /// <returns>A state backed by input availability or depletion.</returns>
        public static TransferState NeedsInputOrComplete(IInputs inputs)
            => new LambdaTransferState(() => inputs.AreInputsAvailable || inputs.AreInputsDepleted, () => false);
    }

    /// <summary>
    /// Exposes output demand, lifecycle, and enqueue operations to a transfer pump.
    /// </summary>
    internal interface IOutputs
    {
        /// <summary>
        /// Gets the actor receive handler for output-side messages.
        /// </summary>
        SubReceive SubReceive { get; }
        /// <summary>
        /// Gets the state that waits for output demand and completes when output closes.
        /// </summary>
        TransferState NeedsDemand { get; }
        /// <summary>
        /// Gets the state that is ready for demand or closure and does not complete by itself.
        /// </summary>
        TransferState NeedsDemandOrCancel { get; }
        /// <summary>
        /// Gets the number of elements currently requested by downstream.
        /// </summary>
        long DemandCount { get; }

        /// <summary>
        /// Gets whether downstream demand is available.
        /// </summary>
        bool IsDemandAvailable { get; }
        /// <summary>
        /// Enqueues an element for downstream delivery.
        /// </summary>
        /// <param name="element">The element to enqueue.</param>
        void EnqueueOutputElement(object element);

        /// <summary>
        /// Completes the output side.
        /// </summary>
        void Complete();
        /// <summary>
        /// Cancels the output side.
        /// </summary>
        void Cancel();
        /// <summary>
        /// Fails the output side.
        /// </summary>
        /// <param name="e">The failure to signal.</param>
        void Error(Exception e);

        /// <summary>
        /// Gets whether the output side has closed.
        /// </summary>
        bool IsClosed { get; }
        /// <summary>
        /// Gets whether the output side remains open.
        /// </summary>
        bool IsOpen { get; }
    }

    /// <summary>
    /// Creates the default transfer states used to wait for output demand.
    /// </summary>
    internal static class DefaultOutputTransferStates 
    {
        /// <summary>
        /// Creates a state that waits for demand and completes when the output closes.
        /// </summary>
        /// <param name="outputs">The output endpoint being monitored.</param>
        /// <returns>A state backed by demand availability and output closure.</returns>
        public static TransferState NeedsDemand(IOutputs outputs) => new LambdaTransferState(() 
            => outputs.IsDemandAvailable, () => outputs.IsClosed);

        /// <summary>
        /// Creates a state that is ready for demand or output closure and does not complete by itself.
        /// </summary>
        /// <param name="outputs">The output endpoint being monitored.</param>
        /// <returns>A state backed by demand availability or output closure.</returns>
        public static TransferState NeedsDemandOrCancel(IOutputs outputs)
            => new LambdaTransferState(() => outputs.IsDemandAvailable || outputs.IsClosed, () => false);
    }

    /// <summary>
    /// Describes whether a transfer action is ready to run or has completed.
    /// </summary>
    public abstract class TransferState
    {
        /// <summary>
        /// Gets whether the transfer condition is currently ready.
        /// </summary>
        public abstract bool IsReady { get; }
        /// <summary>
        /// Gets whether the transfer condition has completed.
        /// </summary>
        public abstract bool IsCompleted { get; }
        /// <summary>
        /// Gets whether the condition is ready and not completed.
        /// </summary>
        public bool IsExecutable => IsReady && !IsCompleted;

        /// <summary>
        /// Combines this state with another using logical OR for readiness and AND for completion.
        /// </summary>
        /// <param name="other">The state combined with this one.</param>
        /// <returns>A state ready when either input is ready and completed when both are completed.</returns>
        public TransferState Or(TransferState other)
            => new LambdaTransferState(() => IsReady || other.IsReady, () => IsCompleted && other.IsCompleted);

        /// <summary>
        /// Combines this state with another using logical AND for readiness and OR for completion.
        /// </summary>
        /// <param name="other">The state combined with this one.</param>
        /// <returns>A state ready when both inputs are ready and completed when either is completed.</returns>
        public TransferState And(TransferState other)
            => new LambdaTransferState(() => IsReady && other.IsReady, () => IsCompleted || other.IsCompleted);
    }

    /// <summary>
    /// Transfer state whose readiness and completion are evaluated by supplied functions.
    /// </summary>
    internal sealed class LambdaTransferState : TransferState
    {
        private readonly Func<bool> _isReady;
        private readonly Func<bool> _isCompleted;

        /// <summary>
        /// Evaluates whether the transfer condition is ready.
        /// </summary>
        public override bool IsReady => _isReady();
        /// <summary>
        /// Evaluates whether the transfer condition has completed.
        /// </summary>
        public override bool IsCompleted => _isCompleted();

        /// <summary>
        /// Creates a state backed by readiness and completion predicates.
        /// </summary>
        /// <param name="isReady">Returns whether the action can run.</param>
        /// <param name="isCompleted">Returns whether this condition has completed.</param>
        public LambdaTransferState(Func<bool> isReady, Func<bool> isCompleted)
        {
            _isReady = isReady;
            _isCompleted = isCompleted;
        }
    }

    /// <summary>
    /// Terminal transfer state that is neither ready nor executable.
    /// </summary>
    internal sealed class Completed : TransferState
    {
        /// <summary>
        /// Gets the shared completed state.
        /// </summary>
        public static readonly Completed Instance = new();

        private Completed()
        {
        }

        /// <summary>
        /// A completed state is not ready for further actions.
        /// </summary>
        public override bool IsReady => false;

        /// <summary>
        /// Indicates that the transfer condition has completed.
        /// </summary>
        public override bool IsCompleted => true;
    }

    /// <summary>
    /// Initial transfer state before the pump has been assigned a phase.
    /// </summary>
    internal sealed class NotInitialized : TransferState
    {
        /// <summary>
        /// Gets the shared uninitialized state.
        /// </summary>
        public static readonly NotInitialized Instance = new();

        private NotInitialized()
        {
        }

        /// <summary>
        /// An uninitialized pump is not ready to execute an action.
        /// </summary>
        public override bool IsReady => false;
        /// <summary>
        /// An uninitialized pump is not considered completed.
        /// </summary>
        public override bool IsCompleted => false;
    }

    /// <summary>
    /// Transfer state that waits for a specified number of upstream subscriptions.
    /// </summary>
    internal sealed class WaitingForUpstreamSubscription : TransferState
    {
        /// <summary>
        /// Gets the number of upstream subscriptions still required.
        /// </summary>
        public readonly int Remaining;
        /// <summary>
        /// Gets the phase to enter after all required subscriptions arrive.
        /// </summary>
        public readonly TransferPhase AndThen;

        /// <summary>
        /// Creates a state waiting for upstream subscriptions before continuing with a phase.
        /// </summary>
        /// <param name="remaining">The number of subscriptions still required.</param>
        /// <param name="andThen">The phase to enter when the count reaches zero.</param>
        public WaitingForUpstreamSubscription(int remaining, TransferPhase andThen)
        {
            Remaining = remaining;
            AndThen = andThen;
        }

        /// <summary>
        /// A pump cannot execute its next action while waiting for upstream subscriptions.
        /// </summary>
        public override bool IsReady => false;
        /// <summary>
        /// Waiting for subscriptions is not a completed state.
        /// </summary>
        public override bool IsCompleted => false;
    }

    /// <summary>
    /// Transfer state that is always ready and never completed.
    /// </summary>
    internal sealed class Always : TransferState
    {
        /// <summary>
        /// Gets the shared always-ready state.
        /// </summary>
        public static readonly Always Instance = new();

        private Always()
        {
        }

        /// <summary>
        /// Indicates that an action can run immediately.
        /// </summary>
        public override bool IsReady => true;
        /// <summary>
        /// Indicates that this state does not complete.
        /// </summary>
        public override bool IsCompleted => false;
    }

    /// <summary>
    /// A transfer phase pairs the readiness condition for an action with that action.
    /// </summary>
    public readonly struct TransferPhase
    {
        /// <summary>
        /// Gets the condition that must be ready before the action runs.
        /// </summary>
        public readonly TransferState Precondition;
        /// <summary>
        /// Gets the action run when the precondition is ready.
        /// </summary>
        public readonly Action Action;

        /// <summary>
        /// Creates a transfer phase from its precondition and action.
        /// </summary>
        /// <param name="precondition">The readiness condition for the phase.</param>
        /// <param name="action">The action run when that condition is ready.</param>
        public TransferPhase(TransferState precondition, Action action) : this()
        {
            Precondition = precondition;
            Action = action;
        }
    }

    /// <summary>
    /// Controls the phases and execution state of an input/output transfer pump.
    /// </summary>
    public interface IPump
    {
        /// <summary>
        /// Gets or sets the pump's current readiness state.
        /// </summary>
        TransferState TransferState { get; set; }
        /// <summary>
        /// Gets or sets the action executed by the pump.
        /// </summary>
        Action CurrentAction { get; set; }
        /// <summary>
        /// Gets whether the pump has entered a completed state.
        /// </summary>
        bool IsPumpFinished { get; }

        /// <summary>
        /// Configures the first phase to wait for at least one upstream subscription.
        /// </summary>
        /// <param name="waitForUpstream">A positive number of upstream subscriptions required before the phase starts.</param>
        /// <param name="andThen">The phase to run after the required subscriptions arrive.</param>
        void InitialPhase(int waitForUpstream, TransferPhase andThen);
        /// <summary>
        /// Pauses the current phase until the specified number of upstream subscriptions arrive.
        /// </summary>
        /// <param name="waitForUpstream">The number of upstream subscriptions to wait for.</param>
        void WaitForUpstream(int waitForUpstream);
        /// <summary>
        /// Notifies the pump that an upstream subscription has arrived.
        /// </summary>
        void GotUpstreamSubscription();
        /// <summary>
        /// Installs the next transfer phase.
        /// </summary>
        /// <param name="phase">The phase to install.</param>
        void NextPhase(TransferPhase phase);

        // Exchange input buffer elements and output buffer "requests" until one of them becomes empty.
        // Generate upstream requestMore for every Nth consumed input element
        /// <summary>
        /// Runs transfer actions while the current state is executable.
        /// </summary>
        void Pump();
        /// <summary>
        /// Notifies the pump that an action failed.
        /// </summary>
        /// <param name="e">The exception thrown by the current action.</param>
        void PumpFailed(Exception e);
        /// <summary>
        /// Notifies the pump that transfer is complete.
        /// </summary>
        void PumpFinished();
    }

    /// <summary>
    /// Implements transfer-pump initialization, phase changes, and action execution.
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
        /// Gets or sets the pump's current readiness state.
        /// </summary>
        public TransferState TransferState { get; set; }

        /// <summary>
        /// Gets or sets the action executed by the pump.
        /// </summary>
        public Action CurrentAction { get; set; }

        /// <summary>
        /// Gets whether the pump has entered a completed state.
        /// </summary>
        public bool IsPumpFinished => TransferState.IsCompleted;

        /// <summary>
        /// Configures the first phase to wait for the specified positive number of upstream subscriptions.
        /// </summary>
        /// <param name="waitForUpstream">A positive number of subscriptions required before the phase starts.</param>
        /// <param name="andThen">The phase to run after the required subscriptions arrive.</param>
        public void InitialPhase(int waitForUpstream, TransferPhase andThen)
            => Pumps.InitialPhase(this, waitForUpstream, andThen);

        /// <summary>
        /// Pauses the current phase until the specified number of upstream subscriptions arrive.
        /// </summary>
        /// <param name="waitForUpstream">The number of subscriptions to wait for.</param>
        public void WaitForUpstream(int waitForUpstream) => Pumps.WaitForUpstream(this, waitForUpstream);

        /// <summary>
        /// Notifies the pump that an upstream subscription has arrived.
        /// </summary>
        public void GotUpstreamSubscription() => Pumps.GotUpstreamSubscription(this);

        /// <summary>
        /// Installs the next transfer phase.
        /// </summary>
        /// <param name="phase">The phase to install.</param>
        public void NextPhase(TransferPhase phase) => Pumps.NextPhase(this, phase);

        /// <summary>
        /// Runs transfer actions while the current state is executable.
        /// </summary>
        public void Pump() => Pumps.Pump(this);

        /// <summary>
        /// Notifies the pump that an action failed.
        /// </summary>
        /// <param name="e">The exception thrown by the current action.</param>
        public abstract void PumpFailed(Exception e);

        /// <summary>
        /// Notifies the pump that transfer is complete.
        /// </summary>
        public abstract void PumpFinished();
    }

    /// <summary>
    /// Implements transfer-pump initialization, phase changes, and action execution.
    /// </summary>
    internal static class Pumps
    {
        /// <summary>
        /// Resets the pump to its uninitialized state.
        /// </summary>
        /// <param name="self">The pump to reset.</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the pump has not been initialized with a phase.
        /// </exception>
        public static void Init(this IPump self)
        {
            self.TransferState = NotInitialized.Instance;
            self.CurrentAction = () => { throw new IllegalStateException("Pump has not been initialized with a phase"); };
        }

        /// <summary>
        /// Terminal phase whose action must never run.
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the action of the completed phase tried to execute.
        /// </exception>
        public static readonly TransferPhase CompletedPhase = new(Completed.Instance, () =>
        {
            throw new IllegalStateException("The action of completed phase must never be executed");
        });

        /// <summary>
        /// Configures the initial phase to wait for at least one upstream subscription before continuing.
        /// </summary>
        /// <param name="self">The pump to initialize.</param>
        /// <param name="waitForUpstream">A positive number of upstream subscriptions required before continuing.</param>
        /// <param name="andThen">The phase to install after the subscriptions arrive.</param>
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
        /// Suspends the current phase until the requested number of upstream subscriptions arrive.
        /// </summary>
        /// <param name="self">The pump to suspend.</param>
        /// <param name="waitForUpstream">The number of subscriptions to wait for.</param>
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
        /// Decrements the pending subscription count and resumes the saved phase when it reaches zero.
        /// </summary>
        /// <param name="self">The pump whose upstream subscription arrived.</param>
        public static void GotUpstreamSubscription(this IPump self)
        {
            if (self.TransferState is WaitingForUpstreamSubscription state)
            {
                if (state.Remaining == 1)
                {
                    self.TransferState = state.AndThen.Precondition;
                    self.CurrentAction = state.AndThen.Action;
                }
                else
                    self.TransferState = new WaitingForUpstreamSubscription(state.Remaining - 1, state.AndThen);
            }

            self.Pump();
        }

        /// <summary>
        /// Installs a new phase, preserving it if upstream subscriptions are still pending.
        /// </summary>
        /// <param name="self">The pump changing phases.</param>
        /// <param name="phase">The phase to install or defer.</param>
        public static void NextPhase(this IPump self, TransferPhase phase)
        {
            if (self.TransferState is WaitingForUpstreamSubscription state)
            {
                self.TransferState = new WaitingForUpstreamSubscription(state.Remaining, phase);
            }
            else
            {
                self.TransferState = phase.Precondition;
                self.CurrentAction = phase.Action;
            }
        }

        /// <summary>
        /// Gets whether the pump has completed.
        /// </summary>
        /// <param name="self">The pump to inspect.</param>
        /// <returns><see langword="true"/> when its transfer state is completed.</returns>
        public static bool IsPumpFinished(this IPump self) => self.TransferState.IsCompleted;

        /// <summary>
        /// Executes the current action while its transfer state is ready, then reports failures or completion.
        /// </summary>
        /// <param name="self">The pump to execute.</param>
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
