using System;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    public partial class ActorCell
    {
        /// <summary>
        ///     Suspends the non recursive.
        /// </summary>
        private void SuspendNonRecursive()
        {
            Mailbox.Suspend();
        }

        //TODO: resumeNonRecursive

				//TODO: _failed, isFailed, setFailed, clearFailed, perpetrator

        /// <summary>
        ///     Faults the recreate.
        /// </summary>
        /// <param name="m">The m.</param>
        private void FaultRecreate(Recreate m)
        {
            isTerminating = false;
            ActorBase failedActor = _actor;

            object optionalMessage = CurrentMessage;

            if (System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), failedActor.GetType(), "restarting"));

            try
            {
                failedActor.AroundPreRestart(m.Cause, optionalMessage);

                //Check if the actor uses a stash. If it does we must Unstash all messages. 
                //If the user do not want this behavior, the stash should be cleared in PreRestart
                //either by calling ClearStash or by calling UnstashAll.
                var actorStash = failedActor as IActorStash;
                if(actorStash != null)
                {
                    actorStash.Stash.UnstashAll();
                }
            }
            catch (Exception e)
            {
                HandleNonFatalOrInterruptedException(() =>
                {
                    var ex = new PreRestartException(_self, e, m.Cause, optionalMessage);
                    Publish(new Error(ex, Self.Path.ToString(), failedActor.GetType(), e.Message));
                });
            }

            var freshActor = NewActor();
            _actor = freshActor;
            UseThreadContext(() =>
            {
                Mailbox.Resume();
                freshActor.AroundPostRestart(m.Cause, null);
            });
            if(System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), freshActor.GetType(), "restarted (" + freshActor + ")"));
        }

        /// <summary>
        ///     Faults the suspend.
        /// </summary>
        /// <param name="obj">The object.</param>
        private void FaultSuspend(Suspend obj)
        {
            SuspendNonRecursive();
            SuspendChildren();
        }

        /// <summary>
        ///     Faults the resume.
        /// </summary>
        /// <param name="obj">The object.</param>
        private void FaultResume(Resume obj)
        {
            Mailbox.Resume();
        }


        //TODO: faultCreate

        //TODO: finishCreate


        /// <summary>
        ///     Terminates this instance.
        /// </summary>
        private void Terminate()
        {
            if (isTerminating)
                return;

            SetReceiveTimeout(null);
            CancelReceiveTimeout();

            isTerminating = true;
            _self.IsTerminated = true;

            UnwatchWatchedActors(_actor);
            foreach (var child in GetChildren())
            {
                child.Stop();
            }

            if (System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), ActorType, "stopping"));
            FinishTerminate();
        }

        //TODO: handleInvokeFailure

        /// <summary>
        ///     Finishes the terminate.
        /// </summary>
        private void FinishTerminate()
        {
            if (_actor == null)
            {
                //TODO: this is the root actor, do something....
                return;
            }

            if (_actor != null)
            {
                try
                {
                    _actor.AroundPostStop();

                    //Check if the actor uses a stash. If it does we must Unstash all messages. 
                    //If the user do not want this behavior, the stash should be cleared in PostStop
                    //either by calling ClearStash or by calling UnstashAll.
                    var actorStash = _actor as IActorStash;
                    if(actorStash != null)
                    {
                        actorStash.Stash.UnstashAll();
                    }
                }
                catch (Exception x)
                {
                    HandleNonFatalOrInterruptedException(
                        () => Publish(new Error(x, Self.Path.ToString(), ActorType, x.Message)));
                }
            }
            //TODO: Akka Jvm: this is done in a call to dispatcher.detach()
            {
                //TODO: Akka Jvm: this is done in a call to MessageDispatcher.detach()
                {
                    var mailbox = Mailbox;
                    var deadLetterMailbox = System.Mailboxes.DeadLetterMailbox;
                    SwapMailbox(deadLetterMailbox);
                    mailbox.BecomeClosed();
                    mailbox.CleanUp();
                }
            }
            Parent.Tell(new DeathWatchNotification(Self, true, false));
            TellWatchersWeDied();
            UnwatchWatchedActors(_actor);
            if(System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), ActorType, "stopped"));

            ClearActor();
            ClearActorCell();
            _actor = null;
        }

        //TODO: finishRecreate

        /// <summary>
        ///     Handles the failed.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleFailed(Failed m)	//Is called handleFailure in Akka JVM
        {
            bool handled = _actor.SupervisorStrategyLazy().HandleFailure(this, m.Child, m.Cause);
            if (!handled)
                throw m.Cause;
        }

        /// <summary>
        ///     Handles the child terminated.
        /// </summary>
        /// <param name="actor">The actor.</param>
        private void HandleChildTerminated(ActorRef actor)
        {
            RemoveChildAndGetStateChange(actor);
            //global::System.Diagnostics.Debug.WriteLine("removed child " + actor.Path.Name);
            //global::System.Diagnostics.Debug.WriteLine("count " + Children.Count());
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