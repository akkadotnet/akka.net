//-----------------------------------------------------------------------
// <copyright file="Futures.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static Task<object> Ask(this ICanTell self, object message, TimeSpan? timeout = null)
        {
            return self.Ask<object>(message, timeout, CancellationToken.None);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        public static Task<object> Ask(this ICanTell self, object message, CancellationToken cancellationToken)
        {
            return self.Ask<object>(message, null, cancellationToken);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        public static Task<object> Ask(this ICanTell self, object message, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return self.Ask<object>(message, timeout, cancellationToken);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static Task<T> Ask<T>(this ICanTell self, object message, TimeSpan? timeout = null)
        {
            return self.Ask<T>(message, timeout, CancellationToken.None);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        public static Task<T> Ask<T>(this ICanTell self, object message, CancellationToken cancellationToken)
        {
            return self.Ask<T>(message, null, cancellationToken);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the system can't resolve the target provider.
        /// </exception>
        /// <returns>TBD</returns>
        public static Task<T> Ask<T>(this ICanTell self, object message, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return Ask<T>(self, _ => message, timeout, cancellationToken);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <param name="messageFactory">Factory method that creates a message that can encapsulate the 'Sender' IActorRef</param>
        /// <param name="timeout">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the system can't resolve the target provider.
        /// </exception>
        /// <returns>TBD</returns>
        public static Task<T> Ask<T>(this ICanTell self, Func<IActorRef, object> messageFactory, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            IActorRefProvider provider = ResolveProvider(self);
            if (provider == null)
                throw new ArgumentException("Unable to resolve the target Provider", nameof(self));

            var result = TaskEx.NonBlockingTaskCompletionSource<T>();

            CancellationTokenSource timeoutCancellation = null;
            timeout ??= provider.Settings.AskTimeout;

            CancellationTokenRegistration? ctr1 = null;
            CancellationTokenRegistration? ctr2 = null;

            if (timeout != Timeout.InfiniteTimeSpan && timeout.Value > default(TimeSpan))
            {
                timeoutCancellation = new CancellationTokenSource();

                ctr1 = timeoutCancellation.Token.Register(() =>
                {
                    result.TrySetException(new AskTimeoutException($"Timeout after {timeout} seconds"));
                });

                timeoutCancellation.CancelAfter(timeout.Value);
            }

            if (cancellationToken.CanBeCanceled)
            {
                ctr2 = cancellationToken.Register(() => result.TrySetCanceled());
            }

            var future = provider.CreateFutureRef(result);
            var path = future.Path;

            //The future actor needs to be unregistered in the temp container
            _ = result.Task.ContinueWith(t =>
            {
                provider.UnregisterTempActor(path);

                ctr1?.Dispose();
                ctr2?.Dispose();
                timeoutCancellation?.Dispose();
            }, TaskContinuationOptions.ExecuteSynchronously);

            //The future actor needs to be registered in the temp container
            provider.RegisterTempActor(future, path);
            var message = messageFactory(future);
            future.DeliverAsk(message, self);

            return result.Task;
        }

        /// <summary>
        /// Resolves <see cref="IActorRefProvider"/> for Ask pattern
        /// </summary>
        /// <param name="self">Reference to someone we are sending Ask request to</param>
        /// <returns>Provider used for Ask pattern implementation</returns>
        internal static IActorRefProvider ResolveProvider(ICanTell self)
        {
            while (true)
            {
                switch (self)
                {
                    case ActorSelection selection:
                        self = selection.Anchor;
                        continue;
                    case IInternalActorRef actorRef:
                        return actorRef.Provider;
                }

                if (ActorCell.Current is { } cell) return cell.SystemImpl.Provider;

                return null;
            }
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
        /// <summary>
        /// Can't access constructor directly - use PromiseActorRef.Apply instead.
        /// </summary>
        private PromiseActorRef(IActorRefProvider provider, TaskCompletionSource<object> promise, string mcn)
        {
            _provider = provider;
            _promise = promise;
            _mcn = mcn;
        }

        private readonly IActorRefProvider _provider;
        private readonly TaskCompletionSource<object> _promise;

        /// <summary>
        /// The result of the promise being completed.
        /// </summary>
        public Task<object> Result => _promise.Task;

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

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Registering
        {
            private Registering() { }
            // ReSharper disable once InconsistentNaming

            /// <summary>
            /// TBD
            /// </summary>
            public static Registering Instance { get; } = new Registering();
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Stopped
        {
            private Stopped() { }
            // ReSharper disable once InconsistentNaming

            /// <summary>
            /// Singleton instance.
            /// </summary>
            public static Stopped Instance { get; } = new Stopped();
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class StoppedWithPath : IEquatable<StoppedWithPath>
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="path">TBD</param>
            public StoppedWithPath(ActorPath path)
            {
                Path = path;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ActorPath Path { get; }

            #region Equality

            /// <inheritdoc/>
            public bool Equals(StoppedWithPath other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Path, other.Path);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is StoppedWithPath path && Equals(path);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return (Path != null ? Path.GetHashCode() : 0);
            }

            #endregion
        }

        #endregion

        #region Static methods

        private static readonly Status.Failure ActorStopResult = new Status.Failure(new ActorKilledException("Stopped"));

        // use a static delegate to avoid allocations
        private static readonly Action<object> CancelAction = o => ((TaskCompletionSource<object>)o).TrySetCanceled();

        /// <summary>
        /// Creates a new <see cref="PromiseActorRef"/>
        /// </summary>
        /// <param name="provider">The current actor ref provider.</param>
        /// <param name="messageClassName">The name of the message class.</param>
        /// <param name="cancellationToken">An external cancellation token.</param>
        /// <returns>A new <see cref="PromiseActorRef"/></returns>
        /// <remarks>
        /// API is used by WatchAsync.
        /// </remarks>
        public static PromiseActorRef Apply(IActorRefProvider provider,
            string messageClassName, CancellationToken cancellationToken = default)
        {
            var result = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var a = new PromiseActorRef(provider, result, messageClassName);

            if (cancellationToken != default)
            {
                cancellationToken.Register(CancelAction, result);
            }
            
            async Task ExecPromise()
            {
                try
                {
                    await result.Task;
                }
                finally
                {
                    a.Stop();
                }
            }

#pragma warning disable CS4014
            ExecPromise(); // need this to run as a detached task
#pragma warning restore CS4014

            return a;
        }

        /// <summary>
        /// Creates a new <see cref="PromiseActorRef"/>
        /// </summary>
        /// <param name="provider">The current actor ref provider.</param>
        /// <param name="timeout">The timeout on the promise.</param>
        /// <param name="targetName">The target of the object / actor</param>
        /// <param name="messageClassName">The name of the message class.</param>
        /// <param name="sender">The actor sending the message via promise.</param>
        /// <returns>A new <see cref="PromiseActorRef"/></returns>
        public static PromiseActorRef Apply(IActorRefProvider provider, TimeSpan timeout, object targetName,
            string messageClassName, IActorRef sender = null)
        {
            CancellationTokenSource cancellationSource = default;

            if (timeout != TimeSpan.Zero)
            {
                // avoid CTS + delegate allocation if timeouts aren't needed
                cancellationSource = new CancellationTokenSource();
                cancellationSource.CancelAfter(timeout);
            }

            var p = Apply(provider, messageClassName, cancellationSource?.Token ?? default);
            
            // need to dispose CTS afterwards
            async Task ExecPromise()
            {
                try
                {
                    await p._promise.Task;
                }
                finally
                {
                    cancellationSource?.Dispose();
                }
            }

#pragma warning disable CS4014
            ExecPromise();
#pragma warning restore CS4014

            return p;
        }

        #endregion
        
        private readonly AtomicReference<ImmutableHashSet<IActorRef>> _watchedByDoNotCallMeDirectly = new AtomicReference<ImmutableHashSet<IActorRef>>(ImmutableHashSet<IActorRef>.Empty);

        private ImmutableHashSet<IActorRef> WatchedBy
        {
            get { return _watchedByDoNotCallMeDirectly.Value; }
        }

        private bool UpdateWatchedBy(ImmutableHashSet<IActorRef> oldWatchedBy, ImmutableHashSet<IActorRef> newWatchedBy)
        {
            return _watchedByDoNotCallMeDirectly.CompareAndSet(oldWatchedBy, newWatchedBy);
        }

        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }

        /// <summary>
        /// Returns false if the <see cref="_promise"/> is already completed.
        /// </summary>
        private bool AddWatcher(IActorRef watcher)
        {
            if (WatchedBy.Contains(watcher))
            {
                return false;
            }
            return UpdateWatchedBy(WatchedBy, WatchedBy.Add(watcher)) || AddWatcher(watcher);
        }

        private void RemoveWatcher(IActorRef watcher)
        {
            while (true)
            {
                if (!WatchedBy.Contains(watcher))
                {
                    return;
                }

                if (!UpdateWatchedBy(WatchedBy, WatchedBy.Remove(watcher))) continue;
                break;
            }
        }

        private ImmutableHashSet<IActorRef> ClearWatchers()
        {
            while (true)
            {
                if (WatchedBy == null || WatchedBy.IsEmpty) return ImmutableHashSet<IActorRef>.Empty;
                if (!UpdateWatchedBy(WatchedBy, null)) continue;
                return WatchedBy;
            }
        }

        private object State
        {
            get { return _stateDoNotCallMeDirectly.Value; }
            set { _stateDoNotCallMeDirectly.Value = value; }
        }

        private bool UpdateState(object oldState, object newState)
        {
            return _stateDoNotCallMeDirectly.CompareAndSet(oldState, newState);
        }

        /// <inheritdoc cref="InternalActorRefBase.Parent"/>
        public override IInternalActorRef Parent
        {
            get { return Provider.TempContainer; }
        }


        /// <inheritdoc cref="InternalActorRefBase"/>
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
                switch (State)
                {
                    case null when UpdateState(null, Registering.Instance):
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
                    case null:
                        continue;
                    case ActorPath _:
                        return State as ActorPath;
                    case StoppedWithPath stoppedWithPath:
                        return stoppedWithPath.Path;
                    case Stopped _:
                        //even if we are already stopped we still need to produce a proper path
                        UpdateState(Stopped.Instance, new StoppedWithPath(Provider.TempPath()));
                        continue;
                    case Registering _:
                        continue;
                }
            }
        }

        /// <inheritdoc cref="ActorRefBase.TellInternal">InternalActorRefBase.TellInternal</inheritdoc>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if (State is Stopped or StoppedWithPath) Provider.DeadLetters.Tell(message);
            else
            {
                if (message == null) throw new InvalidMessageException("Message is null");
                // @Aaronontheweb note: not using any of the Status stuff here. Seems like it's extraneous in CLR
                //var wrappedMessage = message;
                //if (!(message is Status.Success || message is Status.Failure))
                //{
                //    wrappedMessage = new Status.Success(message);
                //}
                if (!(_promise.TrySetResult(message)))
                    Provider.DeadLetters.Tell(message);
            }
        }

        /// <inheritdoc cref="InternalActorRefBase.SendSystemMessage(ISystemMessage)"/>
        public override void SendSystemMessage(ISystemMessage message)
        {
            switch (message)
            {
                case Terminate _:
                    Stop();
                    break;
                case DeathWatchNotification dw:
                    Tell(new Terminated(dw.Actor, dw.ExistenceConfirmed, dw.AddressTerminated), this);
                    break;
                case Watch watch:
                {
                    if (Equals(watch.Watchee, this))
                    {
                        if (!AddWatcher(watch.Watcher))
                        {
                            // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅
                            watch.Watcher.SendSystemMessage(new DeathWatchNotification(watch.Watchee, existenceConfirmed: true,
                                addressTerminated: false));
                        }
                        else
                        {
                            //TODO: find a way to get access to logger?
                            Console.WriteLine("BUG: illegal Watch({0},{1}) for {2}", watch.Watchee, watch.Watcher, this);
                        }
                    }

                    break;
                }
                case Unwatch unwatch when Equals(unwatch.Watchee, this) && !Equals(unwatch.Watcher, this):
                    RemoveWatcher(unwatch.Watcher);
                    break;
                case Unwatch unwatch:
                    Console.WriteLine("BUG: illegal Unwatch({0},{1}) for {2}", unwatch.Watchee, unwatch.Watcher, this);
                    break;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Stop()
        {
            while (true)
            {
                var state = State;
                // if path was never queried nobody can possibly be watching us, so we don't have to publish termination either
                if (state == null)
                {
                    if (UpdateState(null, Stopped.Instance)) StopEnsureCompleted();
                    else continue;
                }
                else if (state is ActorPath p)
                {
                    if (UpdateState(p, new StoppedWithPath(p)))
                    {
                        try
                        {
                            StopEnsureCompleted();
                        }
                        finally
                        {
                            Provider.UnregisterTempActor(p);
                        }
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (state is Stopped or StoppedWithPath)
                {
                    //already stopped
                }
                else if (state is Registering)
                {
                    //spin until registration is completed before stopping
                    continue;
                }
                break;
            }
        }

        private void StopEnsureCompleted()
        {
            _promise.TrySetResult(ActorStopResult);
            var watchers = ClearWatchers();
            if (watchers.Any())
            {
                foreach (var watcher in watchers)
                {
                    watcher.AsInstanceOf<IInternalActorRef>()
                        .SendSystemMessage(new DeathWatchNotification(watcher, existenceConfirmed: true,
                            addressTerminated: false));
                }
            }
        }
    }
}
