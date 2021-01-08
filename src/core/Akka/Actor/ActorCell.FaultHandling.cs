//-----------------------------------------------------------------------
// <copyright file="ActorCell.FaultHandling.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public partial class ActorCell
    {
        private void SuspendNonRecursive()
        {
            Dispatcher.Suspend(this);
        }

        private void ResumeNonRecursive()
        {
            Dispatcher.Resume(this);
        }

        // ReSharper disable once InconsistentNaming
        private IActorRef _failed_DoNotUseMeDirectly;
        private bool IsFailed { get { return _failed_DoNotUseMeDirectly != null; } }
        private void SetFailed(IActorRef perpetrator)
        {
            _failed_DoNotUseMeDirectly = perpetrator;
        }
        private void ClearFailed()
        {
            _failed_DoNotUseMeDirectly = null;
        }
        private IActorRef Perpetrator { get { return _failed_DoNotUseMeDirectly; } }

        /// <summary>Re-create the actor in response to a failure.</summary>
        private void FaultRecreate(Exception cause)
        {
            if (_actor == null)
            {
                _systemImpl.EventStream.Publish(new Error(null, _self.Path.ToString(), GetType(), "Changing Recreate into Create after " + cause));
                FaultCreate();
            } 
            else if (IsNormal)
            {
                var failedActor = _actor;

                if (System.Settings.DebugLifecycle)
                    Publish(new Debug(_self.Path.ToString(), failedActor.GetType(), "Restarting"));

                if(!(failedActor is null))
                {
                    var optionalMessage = CurrentMessage;
                    try
                    {
                        // if the actor fails in preRestart, we can do nothing but log it: it’s best-effort
                        failedActor.AroundPreRestart(cause, optionalMessage);

                        // run actor pre-incarnation plugin pipeline
                        var pipeline = _systemImpl.ActorPipelineResolver.ResolvePipeline(failedActor.GetType());
                        pipeline.BeforeActorIncarnated(failedActor, this);
                    }
                    catch (Exception e)
                    {
                        HandleNonFatalOrInterruptedException(() =>
                        {
                            var ex = new PreRestartException(_self, e, cause, optionalMessage);
                            Publish(new Error(ex, _self.Path.ToString(), failedActor.GetType(), e.Message));
                        });
                    }
                    finally
                    {
                        ClearActor(_actor);
                    }
                }

                global::System.Diagnostics.Debug.Assert(Mailbox.IsSuspended(), "Mailbox must be suspended during restart, status=" + Mailbox.CurrentStatus());
                if (!SetChildrenTerminationReason(new SuspendReason.Recreation(cause)))
                {
                    FinishRecreate(cause, failedActor);
                }
            }
            else
            {
                // need to keep that suspend counter balanced
                FaultResume(null);
            }
        }

        /// <summary>
        /// Suspends the actor in response to a failure of a parent (i.e. the "recursive suspend" feature).
        /// </summary>
        private void FaultSuspend()
        {
            SuspendNonRecursive();
            SuspendChildren();
        }

        /// <summary>
        /// Resumes the actor in response to a failure
        /// </summary>
        /// <param name="causedByFailure">The exception that caused the failure. signifies if it was our own failure 
        /// which prompted this action.</param>
        private void FaultResume(Exception causedByFailure)
        {
            if (_actor == null)
            {
                _systemImpl.EventStream.Publish(new Error(null, _self.Path.ToString(), GetType(), "Changing Resume into Create after " + causedByFailure));
                FaultCreate();
            }
            //Akka Jvm does the following commented section as well, but we do not store the context inside the actor so it's not applicable
            //    else if (_actor.context == null && causedByFailure != null)
            //    {
            //        system.eventStream.publish(Error(self.path.toString, clazz(actor), "changing Resume into Restart after " + causedByFailure))
            //        faultRecreate(causedByFailure)
            //    }
            else
            {
                var perp = Perpetrator;
                // done always to keep that suspend counter balanced
                // must happen "atomically"
                try
                {
                    ResumeNonRecursive();
                }
                finally
                {
                    if (causedByFailure != null)
                        ClearFailed();
                }
                ResumeChildren(causedByFailure, perp);
            }
        }

        /// <summary>
        /// Create the actor in response to a failure
        /// </summary>
        private void FaultCreate()
        {
            global::System.Diagnostics.Debug.Assert(Mailbox.IsSuspended(), "Mailbox must be suspended during failed creation, status=" + Mailbox.CurrentStatus());
            global::System.Diagnostics.Debug.Assert(_self.Equals(Perpetrator), "Perpetrator should be self");

            SetReceiveTimeout(null);
            CancelReceiveTimeout();

            // stop all children, which will turn childrenRefs into TerminatingChildrenContainer (if there are children)
            StopChildren();

            if (!SetChildrenTerminationReason(new SuspendReason.Creation()))
                FinishCreate();
        }

        private void FinishCreate()
        {
            try
            {
                ResumeNonRecursive();
            }
            finally
            {
                ClearFailed();
            }
            try
            {
                Create(failure: null);
            }
            catch (Exception e)
            {
                HandleNonFatalOrInterruptedException(() => HandleInvokeFailure(e));
            }
        }      

        /// <summary>Terminates this instance.</summary>
        private void Terminate()
        {
            SetReceiveTimeout(null);
            CancelReceiveTimeout();

            // prevent Deadletter(Terminated) messages
            UnwatchWatchedActors(_actor);

            // stop all children, which will turn childrenRefs into TerminatingChildrenContainer (if there are children)
            StopChildren();

            if (SystemImpl.Aborting)
            {
                // separate iteration because this is a very rare case that should not penalize normal operation
                foreach (var child in Children)
                {
                    if(!child.AsInstanceOf<IActorRefScope>().IsLocal) // send ourselves a deathwatch notification preemptively for non-local children
                        Self.AsInstanceOf<IInternalActorRef>().SendSystemMessage(new DeathWatchNotification(child, true, false));
                }
            }
            var wasTerminating = IsTerminating;
            if (SetChildrenTerminationReason(SuspendReason.Termination.Instance))
            {
                if (!wasTerminating)
                {
                    // do not process normal messages while waiting for all children to terminate
                    SuspendNonRecursive();
                    // do not propagate failures during shutdown to the supervisor
                    SetFailed(_self);
                    if (System.Settings.DebugLifecycle)
                        Publish(new Debug(_self.Path.ToString(), ActorType, "Stopping"));
                }
            }
            else
            {
                SetTerminated();
                FinishTerminate();
            }
        }

        private void HandleInvokeFailure(Exception cause, IEnumerable<IActorRef> childrenNotToSuspend = null)
        {
            // prevent any further messages to be processed until the actor has been restarted
            if (!IsFailed)
            {
                try
                {
                    SuspendNonRecursive();

                    if (CurrentMessage is Failed)
                    {
                        var failedChild = Sender;
                        childrenNotToSuspend = childrenNotToSuspend.Concat(failedChild); //Function handles childrenNotToSuspend being null
                        SetFailed(failedChild);
                    }
                    else
                    {
                        SetFailed(_self);
                    }
                    SuspendChildren(childrenNotToSuspend?.ToList());

                    //Tell supervisor
                    // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
                    Parent.SendSystemMessage(new Failed(_self, cause, _self.Path.Uid));
                }
                catch (Exception e)
                {
                    HandleNonFatalOrInterruptedException(() =>
                    {
                        Publish(new Error(e, _self.Path.ToString(), _actor.GetType(), "Emergency stop: exception in failure handling for " + cause));
                        try
                        {
                            StopChildren();
                        }
                        finally
                        {
                            FinishTerminate();
                        }
                    });
                }

            }
        }

        private void StopChildren()
        {
            foreach (var child in ChildrenContainer.Children)
            {
                Stop(child); 
            }
        }


        private void FinishTerminate()
        {
            // The following order is crucial for things to work properly. Only change this if you're very confident and lucky.
            // 
            // Please note that if a parent is also a watcher then ChildTerminated and Terminated must be processed in this
            // specific order.
            var a = _actor;
            try
            {
                if (a != null)
                {
                    a.AroundPostStop();

                    // run actor pre-incarnation plugin pipeline
                    var pipeline = _systemImpl.ActorPipelineResolver.ResolvePipeline(a.GetType());
                    pipeline.BeforeActorIncarnated(a, this);
                }
            }
            catch (Exception x)
            {
                HandleNonFatalOrInterruptedException(
                    () => Publish(new Error(x, _self.Path.ToString(), ActorType, x.Message)));
            }
            finally
            {
                try { Dispatcher.Detach(this); }
                finally
                {
                    try { Parent.SendSystemMessage(new DeathWatchNotification(_self, existenceConfirmed: true, addressTerminated: false)); }
                    finally
                    {
                        try { StopFunctionRefs(); }
                        finally
                        {
                            try { TellWatchersWeDied(); }
                            finally
                            {
                                try { UnwatchWatchedActors(a); } // stay here as we expect an emergency stop from HandleInvokeFailure
                                finally
                                {
                                    if (System.Settings.DebugLifecycle)
                                        Publish(new Debug(_self.Path.ToString(), ActorType, "Stopped"));

                                    ClearActor(a);
                                    ClearActorCell();

                                    _actor = null;

                                }
                            }
                        }
                    }
                }
            }
        }

        private void FinishRecreate(Exception cause, ActorBase failedActor)
        {
            // need to keep a snapshot of the surviving children before the new actor instance creates new ones
            var survivors = ChildrenContainer.Children;
            try
            {
                try { ResumeNonRecursive(); }
                finally { ClearFailed(); }  // must happen in any case, so that failure is propagated

                var freshActor = NewActor();
                _actor = freshActor; // this must happen before postRestart has a chance to fail
                if (ReferenceEquals(freshActor, failedActor))
                    SetActorFields(freshActor); // If the creator returns the same instance, we need to restore our nulled out fields.

                UseThreadContext(() => freshActor.AroundPostRestart(cause, null));

                if (System.Settings.DebugLifecycle)
                    Publish(new Debug(_self.Path.ToString(), freshActor.GetType(), "Restarted (" + freshActor + ")"));

                // only after parent is up and running again do restart the children which were not stopped
                foreach (var survivingChild in survivors)
                {
                    try
                    {
                        survivingChild.Restart(cause);
                    }
                    catch (Exception e)
                    {
                        var child = survivingChild;    //Needed since otherwise it would be access to foreach variable in closure
                        HandleNonFatalOrInterruptedException(() => Publish(new Error(e, _self.Path.ToString(), freshActor.GetType(), "restarting (" + child + ")")));
                    }
                }
            }
            catch (Exception e)
            {
                ClearActor(_actor); // in order to prevent preRestart() from happening again
                HandleInvokeFailure(new PostRestartException(_self, e, cause), survivors);
            }

        }


        private void HandleFailed(Failed f) //Called handleFailure in Akka JVM
        {
            CurrentMessage = f;
            var failedChild = f.Child;
            var failedChildIsNobody = failedChild.IsNobody();
            Sender = failedChildIsNobody ? _systemImpl.DeadLetters : failedChild;
            //Only act upon the failure, if it comes from a currently known child;
            //the UID protects against reception of a Failed from a child which was
            //killed in preRestart and re-created in postRestart

            ChildRestartStats childStats;
            if (TryGetChildStatsByRef(failedChild, out childStats))
            {
                var statsUid = childStats.Child.Path.Uid;
                if (statsUid == f.Uid)
                {
                    var handled = _actor.SupervisorStrategyInternal.HandleFailure(this, failedChild, f.Cause, childStats, ChildrenContainer.Stats);
                    if (!handled)
                        ExceptionDispatchInfo.Capture(f.Cause).Throw();
                }
                else
                {
                    Publish(new Debug(_self.Path.ToString(), _actor.GetType(), "Dropping Failed(" + f.Cause + ") from old child " + f.Child + " (uid=" + statsUid + " != " + f.Uid + ")"));
                }
            }
            else
            {
                Publish(new Debug(_self.Path.ToString(), _actor.GetType(), "Dropping Failed(" + f.Cause + ") from unknown child " + failedChild));
            }
        }

        private void HandleChildTerminated(IActorRef child)
        {
            var status = RemoveChildAndGetStateChange(child);

            //If this fails, we do nothing in case of terminating/restarting state,
            //otherwise tell the supervisor etc. (in that second case, the match
            //below will hit the empty default case, too)
            if (_actor != null)
            {
                try
                {
                    _actor.SupervisorStrategyInternal.HandleChildTerminated(this, child, GetChildren());
                }
                catch (Exception e)
                {
                    HandleNonFatalOrInterruptedException(() =>
                    {
                        Publish(new Error(e, _self.Path.ToString(), _actor.GetType(), "HandleChildTerminated failed"));
                        HandleInvokeFailure(e);
                    });
                }
            }


            // if the removal changed the state of the (terminating) children container,
            // then we are continuing the previously suspended recreate/create/terminate action
            var recreation = status as SuspendReason.Recreation;
            if (recreation != null)
            {
                FinishRecreate(recreation.Cause, _actor);
            }
            else if (status is SuspendReason.Creation)
            {
                FinishCreate();
            }
            else if (status is SuspendReason.Termination)
            {
                FinishTerminate();
            }
        }

        /// <summary>
        ///     Handles the non fatal or interrupted exception.
        /// </summary>
        /// <param name="action">The action.</param>
        private void HandleNonFatalOrInterruptedException(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                //TODO: Hmmm?
            }
        }
    }
}

