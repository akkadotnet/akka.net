//-----------------------------------------------------------------------
// <copyright file="Futures.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    ///     Extension method class designed to create Ask support for
    ///     non-ActorRef objects such as <see cref="ActorSelection" />.
    /// </summary>
    public static class Futures
    {
        //when asking from outside of an actor, we need to pass a system, so the FutureActor can register itself there and be resolvable for local and remote calls
        public static Task<object> Ask(this ICanTell self, object message, TimeSpan? timeout = null)
        {
            return self.Ask<object>(message, timeout);
        }

        public static Task<T> Ask<T>(this ICanTell self, object message, TimeSpan? timeout = null)
        {
            IActorRefProvider provider = ResolveProvider(self);
            if (provider == null)
                throw new NotSupportedException("Unable to resolve the target Provider");

            ResolveReplyTo();
            return Ask(self, message, provider, timeout).CastTask<object, T>();
        }

        internal static IActorRef ResolveReplyTo()
        {
            if (ActorCell.Current != null)
                return ActorCell.Current.Self;

            return null;
        }

        internal static IActorRefProvider ResolveProvider(ICanTell self)
        {
            if (ActorCell.Current != null)
                return InternalCurrentActorCellKeeper.Current.SystemImpl.Provider;

            if (self is IInternalActorRef)
                return self.AsInstanceOf<IInternalActorRef>().Provider;

            if (self is ActorSelection)
                return ResolveProvider(self.AsInstanceOf<ActorSelection>().Anchor);

            return null;
        }

        private static Task<object> Ask(ICanTell self, object message, IActorRefProvider provider,
            TimeSpan? timeout)
        {
            var result = new TaskCompletionSource<object>();

            timeout = timeout ?? provider.Settings.AskTimeout;

            if (timeout != Timeout.InfiniteTimeSpan && timeout.Value > default(TimeSpan))
            {
                var cancellationSource = new CancellationTokenSource();
                cancellationSource.Token.Register(() => result.TrySetCanceled());
                cancellationSource.CancelAfter(timeout.Value);
            }

            //create a new tempcontainer path
            ActorPath path = provider.TempPath();
            //callback to unregister from tempcontainer
            Action unregister = () => provider.UnregisterTempActor(path);
            var future = new FutureActorRef(result, unregister, path);
            //The future actor needs to be registered in the temp container
            provider.RegisterTempActor(future, path);
            self.Tell(message, future);
            return result.Task;
        }
    }

    /// <summary>
    /// Akka private optimized representation of the temporary actor spawned to
    /// receive the reply to an "ask" operation.
    /// 
    /// INTERNAL API
    /// </summary>
    internal sealed class PromiseActorRef : MinimalActorRef
    {
        public PromiseActorRef(IActorRefProvider provider, TaskCompletionSource<object> result, string mcn)
        {
            _provider = provider;
            Result = result;
            _mcn = mcn;
        }

        private readonly IActorRefProvider _provider;
        public readonly TaskCompletionSource<object> Result;

        /// <summary>
        /// This is necessary for weaving the PromiseActorRef into the asked message, i.e. the replyTo pattern.
        /// </summary>
        private volatile string _mcn;

        #region Internal states


        /**
           * As an optimization for the common (local) case we only register this PromiseActorRef
           * with the provider when the `path` member is actually queried, which happens during
           * serialization (but also during a simple call to `ToString`, `Equals` or `GetHashCode`!).
           *
           * Defined states:
           * null                  => started, path not yet created
           * Registering           => currently creating temp path and registering it
           * path: ActorPath       => path is available and was registered
           * StoppedWithPath(path) => stopped, path available
           * Stopped               => stopped, path not yet created
           */
        private AtomicReference<object> _stateDoNotCallMeDirectly = new AtomicReference<object>(null);

        internal sealed class Registering
        {
            private Registering() { }
            // ReSharper disable once InconsistentNaming
            private static readonly Registering _instance = new Registering();

            public static Registering Instance { get { return _instance; } }
        }

        internal sealed class Stopped
        {
            private Stopped() { }
            // ReSharper disable once InconsistentNaming
            private static readonly Stopped _instance = new Stopped();

            public static Stopped Instance { get { return _instance; } }
        }

        internal sealed class StoppedWithPath : IEquatable<StoppedWithPath>
        {
            public StoppedWithPath(ActorPath path)
            {
                Path = path;
            }

            public ActorPath Path { get; private set; }

            #region Equality

            public bool Equals(StoppedWithPath other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Path, other.Path);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is StoppedWithPath && Equals((StoppedWithPath)obj);
            }

            public override int GetHashCode()
            {
                return (Path != null ? Path.GetHashCode() : 0);
            }

            #endregion
        }

        #endregion

        #region Static methods

        private static readonly Status.Failure ActorStopResult = new Status.Failure(new ActorKilledException("Stopped"));

        public static PromiseActorRef Apply(IActorRefProvider provider, TimeSpan timeout, object targetName,
            string messageClassName, IActorRef sender = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            var result = new TaskCompletionSource<object>();
            var a = new PromiseActorRef(provider, result, messageClassName);
            var scheduler = provider.Guardian.Underlying.System.Scheduler.Advanced;
            var c = new Cancelable(scheduler, timeout);
            scheduler.ScheduleOnce(timeout, () => result.TrySetResult(new Status.Failure(new AskTimeoutException(
                string.Format("Ask timed out on [{0}] after [{1} ms]. Sender[{2}] sent message of type {3}.", targetName, timeout.TotalMilliseconds, sender, messageClassName)))),
                c);

            result.Task.ContinueWith(r =>
            {
                try
                {
                    a.Stop();
                }
                finally
                {
                    c.Cancel(false);
                }
            }, TaskContinuationOptions.ExecuteSynchronously);

            return a;
        }

        #endregion

        //TODO: ActorCell.emptyActorRefSet ?
        private readonly AtomicReference<HashSet<IActorRef>> _watchedByDoNotCallMeDirectly = new AtomicReference<HashSet<IActorRef>>();

        private HashSet<IActorRef> WatchedBy
        {
            get { return _watchedByDoNotCallMeDirectly; }
        }

        private bool UpdateWatchedBy(HashSet<IActorRef> oldWatchedBy, HashSet<IActorRef> newWatchedBy)
        {
            return _watchedByDoNotCallMeDirectly.CompareAndSet(oldWatchedBy, newWatchedBy);
        }

        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }

        /// <summary>
        /// Returns false if the <see cref="Result"/> is already completed.
        /// </summary>
        private bool AddWatcher(IActorRef watcher)
        {
            if (WatchedBy.Contains(watcher))
            {
                return false;
            }
            return UpdateWatchedBy(WatchedBy, WatchedBy.CopyAndAdd(watcher)) || AddWatcher(watcher);
        }

        private void RemoveWatcher(IActorRef watcher)
        {
            if (!WatchedBy.Contains(watcher))
            {
                return;
            }
            if (!UpdateWatchedBy(WatchedBy, WatchedBy.CopyAndRemove(watcher))) RemoveWatcher(watcher);
        }

        private HashSet<IActorRef> ClearWatchers()
        {
            //TODO: ActorCell.emptyActorRefSet ?
            if (WatchedBy == null) return new HashSet<IActorRef>();
            if (!UpdateWatchedBy(WatchedBy, null)) return ClearWatchers();
            else return WatchedBy;
        }

        private object State
        {
            get { return _stateDoNotCallMeDirectly; }
            set { _stateDoNotCallMeDirectly = value; }
        }

        private bool UpdateState(object oldState, object newState)
        {
            return _stateDoNotCallMeDirectly.CompareAndSet(oldState, newState);
        }

        public override IInternalActorRef Parent
        {
            get { return Provider.TempContainer; }
        }


        public override ActorPath Path
        {
            get { return GetPath(); }
        }

        /// <summary>
        ///  Contract of this method:
        ///  Must always return the same ActorPath, which must have
        ///  been registered if we haven't been stopped yet.
        /// </summary>
        private ActorPath GetPath()
        {
            while (true)
            {
                if (State == null)
                {
                    if (UpdateState(null, Registering.Instance))
                    {
                        ActorPath p = null;
                        try
                        {
                            p = Provider.TempPath();
                            Provider.RegisterTempActor(this, p);
                            return p;
                        }
                        finally
                        {
                            State = p;
                        }
                    }
                    continue;
                }

                if (State is ActorPath)
                    return State as ActorPath;
                if (State is StoppedWithPath)
                    return State.AsInstanceOf<StoppedWithPath>().Path;
                if (State is Stopped)
                {
                    //even if we are already stopped we still need to produce a proper path
                    UpdateState(Stopped.Instance, new StoppedWithPath(Provider.TempPath()));
                    continue;
                }
                if (State is Registering)
                    continue;
            }
        }

        protected override void TellInternal(object message, IActorRef sender)
        {
            if (message is ISystemMessage)
            {
                SendSystemMessage(message as ISystemMessage); 
                return;
            }

            if (State is Stopped || State is StoppedWithPath) Provider.DeadLetters.Tell(message);
            else
            {
                if(message == null) throw new InvalidMessageException();
                var wrappedMessage = message;
                if (!(message is Status.Success || message is Status.Failure))
                {
                    wrappedMessage = new Status.Success(message);
                }
                if (!(Result.TrySetResult(wrappedMessage)))
                    Provider.DeadLetters.Tell(message);
            }
        }

        //TODO: isn't SendSystemMessage supposed to be a part of ActorRef? Why isn't it overridable?
        private void SendSystemMessage(ISystemMessage message)
        {
            if(message is Terminate) Stop();
            else if (message is DeathWatchNotification)
            {
                var dw = message as DeathWatchNotification;
                Tell(new Terminated(dw.Actor, dw.ExistenceConfirmed, dw.AddressTerminated), this);
            }
            else if (message is Watch)
            {
                var watch = message as Watch;
                if (watch.Watchee == this)
                {
                    if (!AddWatcher(watch.Watcher))
                    {
                        // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
                        watch.Watcher.Tell(new DeathWatchNotification(watch.Watchee, existenceConfirmed: true,
                            addressTerminated: false));
                    }
                    else
                    {
                        //TODO: find a way to get access to logger?
                        Console.WriteLine("BUG: illegal Watch({0},{1}) for {2}", watch.Watchee, watch.Watcher, this);
                    }
                }
            }
            else if (message is Unwatch)
            {
                var unwatch = message as Unwatch;
                if(unwatch.Watchee == this && unwatch.Watcher != this) RemoveWatcher(unwatch.Watcher);
                else Console.WriteLine("BUG: illegal Unwatch({0},{1}) for {2}", unwatch.Watchee, unwatch.Watcher, this);
            }
            else { }
        }

        public override void Stop()
        {
            Action ensureCompleted = () =>
            {
                Result.TrySetResult(ActorStopResult);
                var watchers = ClearWatchers();
                if (watchers.Any())
                {
                    foreach (var watcher in watchers)
                    {
                        watcher.Tell(new DeathWatchNotification(watcher, existenceConfirmed: true,
                            addressTerminated: false));
                    }
                }
            };

            var state = State;
            // if path was never queried nobody can possibly be watching us, so we don't have to publish termination either
            if (state == null)
            {
                if (UpdateState(null, Stopped.Instance)) ensureCompleted();
                else Stop();
            }
            else if (state is ActorPath)
            {
                var p = state as ActorPath;
                if (UpdateState(p, new StoppedWithPath(p)))
                {
                    try
                    {
                        ensureCompleted();
                    }
                    finally
                    {
                        Provider.UnregisterTempActor(p);
                    }
                }
                else
                {
                    Stop();
                }
            }
            else if (state is Stopped || state is StoppedWithPath)
            {
                //already stopped
            }
            else if (state is Registering)
            {
                //spin until registration is completed before stopping
                Stop();
            }
        }
    }
}

