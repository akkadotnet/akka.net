using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;
using Microsoft.Extensions.Hosting;

namespace Akka.Hosting
{
    /// <summary>
    /// A strongly typed actor reference that can be used to send messages to an actor.
    /// </summary>
    /// <typeparam name="TActor">The type key of the actor - corresponds to a matching entry inside the <see cref="IActorRegistry"/>.</typeparam>
    /// <remarks>
    /// Designed to be used in combination with dependency injection to get references to specific actors inside your application.
    /// </remarks>
    public interface IRequiredActor<TActor>
    {
        /// <summary>
        /// The underlying actor resolved via <see cref="ActorRegistry"/> using the given <see cref="TActor"/> key.
        /// </summary>
        IActorRef ActorRef { get; }

        /// <summary>
        /// When calling from inside another <see cref="IHostedService"/>, actor registrations may not be
        /// available at startup (due to Akka.NET itself being started asynchronously in another hosted service).
        ///
        /// Instead - you should call the GetAsync method to wait for that actor to be populated into the <see cref="ActorRegistry"/>
        /// by the AkkaService at startup.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A Task that will return the <see cref="IActorRef"/> using the given key.</returns>
        Task<IActorRef> GetAsync(CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TActor">The type key of the actor - corresponds to a matching entry inside the <see cref="IActorRegistry"/>.</typeparam>
    public sealed class RequiredActor<TActor> : IRequiredActor<TActor>
    {
        private readonly IReadOnlyActorRegistry _registry;

        public RequiredActor(IReadOnlyActorRegistry registry)
        {
            _registry = registry;
        }

        private IActorRef? _internalRef = null;

        /// <inheritdoc cref="IRequiredActor{TActor}.ActorRef"/>
        public IActorRef ActorRef
        {
            get
            {
                // attempt 1 - used cached value
                if (_internalRef != null)
                    return _internalRef;

                // attempt 2 - synchronously check the registry (fast path)
                if (_registry.TryGet<TActor>(out var internalRef))
                {
                    return _internalRef = internalRef;
                }


                throw new MissingActorRegistryEntryException(
                    $"Unable to resolve actor type [{typeof(TActor)})] - if you're using IRequiredActor<T> inside the constructor" +
                    $"of an IHostedService, consider using the GetAsync method instead so you can wait for the actor to be populated by the AkkaService (which runs in parallel.)");
            }
        }
        
        /// <inheritdoc cref="IRequiredActor{TActor}.GetAsync"/>
        public async Task<IActorRef> GetAsync(CancellationToken cancellationToken = default)
        {
            // attempt 1 - used cached value
            if (_internalRef != null)
                return _internalRef;

            // attempt 2 - synchronously check the registry (fast path)
            if (_registry.TryGet<TActor>(out var internalRef))
            {
                return _internalRef = internalRef;
            }

            // attempt 3 - wait for the actor to be registered
            return _internalRef = await _registry.GetAsync<TActor>(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class ActorRegistryExtension : ExtensionIdProvider<ActorRegistry>
    {
        public override ActorRegistry CreateExtension(ExtendedActorSystem system)
        {
            return new ActorRegistry();
        }
    }

    /// <summary>
    /// Generic <see cref="ActorRegistry"/> exception.
    /// </summary>
    public class ActorRegistryException : Exception
    {
        public ActorRegistryException(string message) : base(message)
        {
        }

        public ActorRegistryException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when the same key is used twice in the registry and overwriting is not allowed.
    /// </summary>
    public sealed class DuplicateActorRegistryException : ActorRegistryException
    {
        public DuplicateActorRegistryException(string message) : base(message)
        {
        }

        public DuplicateActorRegistryException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a user attempts to retrieve a non-existent key from the <see cref="ActorRegistry"/>.
    /// </summary>
    public sealed class MissingActorRegistryEntryException : ActorRegistryException
    {
        public MissingActorRegistryEntryException(string message) : base(message)
        {
        }

        public MissingActorRegistryEntryException(string message, Exception innerException) : base(message,
            innerException)
        {
        }
    }

    /// <summary>
    /// Used to implement "wait for actor" mechanics
    /// </summary>
    internal sealed class WaitForActorRegistration : IEquatable<WaitForActorRegistration>
    {
        public WaitForActorRegistration(Type key, TaskCompletionSource<IActorRef> waiter)
        {
            Key = key;
            Waiter = waiter;
        }

        public Type Key { get; }

        public TaskCompletionSource<IActorRef> Waiter { get; }

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public bool Equals(WaitForActorRegistration? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Key == other.Key;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is WaitForActorRegistration other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Key.GetHashCode() * 397) ^ Waiter.GetHashCode();
            }
        }

        public static bool operator ==(WaitForActorRegistration? left, WaitForActorRegistration? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(WaitForActorRegistration? left, WaitForActorRegistration? right)
        {
            return !Equals(left, right);
        }
    }

    /// <summary>
    /// Mutable, but thread-safe <see cref="ActorRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Should only be used for top-level actors that need to be accessed from inside or outside the <see cref="ActorSystem"/>.
    ///
    /// If you are adding every single actor in your <see cref="ActorSystem"/> to the registry you are definitely using it wrong.
    /// </remarks>
    public class ActorRegistry : IActorRegistry, IExtension
    {
        private readonly ConcurrentDictionary<Type, IActorRef> _actorRegistrations = new();

        /// <inheritdoc cref="IActorRegistry.Register{TKey}"/>
        /// <exception cref="DuplicateActorRegistryException">Thrown when the same value is inserted twice and overwriting is not allowed.</exception>
        /// <exception cref="ArgumentNullException">Thrown when a <c>null</c> <see cref="IActorRef"/> is registered.</exception>
        public void Register<TKey>(IActorRef actor, bool overwrite = false)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor), "Cannot register null actors");

            if (!TryRegister<TKey>(actor, overwrite))
            {
                throw new DuplicateActorRegistryException(
                    $"An actor for type {typeof(TKey)} has already been registered. Call `Register(IActorRef, bool overwrite=true)` to avoid this error or use a different key.");
            }
        }

        /// <summary>
        /// In the event that an actor is not available yet, typically during the very beginning of ActorSystem startup,
        /// we can wait on that actor becoming available.
        /// </summary>
        /// <remarks>
        /// Have to store a collection of <see cref="WaitForActorRegistration"/>s here so each waiter gets its own cancellation token.
        /// </remarks>
        private readonly ConcurrentDictionary<Type, ImmutableHashSet<WaitForActorRegistration>> _actorWaiters = new();

        /// <summary>
        /// Attempts to register an actor with the registry.
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to register.</param>
        /// <param name="overwrite">If <c>true</c>, allows overwriting of a previous actor with the same key. Defaults to <c>false</c>.</param>
        /// <returns><c>true</c> if the actor was set to this key in the registry, <c>false</c> otherwise.</returns>
        public bool TryRegister<TKey>(IActorRef actor, bool overwrite = false)
        {
            return TryRegister(typeof(TKey), actor, overwrite);
        }

        /// <summary>
        /// Attempts to register an actor with the registry.
        /// </summary>
        /// <param name="key">The key for a particular actor.</param>
        /// <param name="actor">The <see cref="IActorRef"/> to register.</param>
        /// <param name="overwrite">If <c>true</c>, allows overwriting of a previous actor with the same key. Defaults to <c>false</c>.</param>
        /// <returns><c>true</c> if the actor was set to this key in the registry, <c>false</c> otherwise.</returns>
        public bool TryRegister(Type key, IActorRef actor, bool overwrite = false)
        {
            if (actor == null)
                return false;

            if (!overwrite)
            {
                if (!_actorRegistrations.TryAdd(key, actor))
                    return false;
            }
            else
                _actorRegistrations[key] = actor;

            NotifyWaiters(key, _actorRegistrations[key]);
            return true;
        }

        /// <summary>
        /// Try to retrieve an <see cref="IActorRef"/> with the given <see cref="TKey"/>.
        /// </summary>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <returns><c>true</c> if an actor with this key exists, <c>false</c> otherwise.</returns>
        public bool TryGet<TKey>(out IActorRef actor)
        {
            return TryGet(typeof(TKey), out actor);
        }

        /// <summary>
        /// Try to retrieve an <see cref="IActorRef"/> with the given type.
        /// </summary>
        /// <param name="key">The key for a particular actor.</param>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <returns><c>true</c> if an actor with this key exists, <c>false</c> otherwise.</returns>
        public bool TryGet(Type key, out IActorRef actor)
        {
            if (_actorRegistrations.ContainsKey(key))
            {
                actor = _actorRegistrations[key];
                return true;
            }

            actor = ActorRefs.Nobody;
            return false;
        }

        /// <inheritdoc cref="IReadOnlyActorRegistry.GetAsync{TKey}"/>
        public async Task<IActorRef> GetAsync<TKey>(CancellationToken ct = default)
        {
            return await GetAsync(typeof(TKey), ct).ConfigureAwait(false);
        }

        /// <inheritdoc cref="IReadOnlyActorRegistry.GetAsync"/>
        public async Task<IActorRef> GetAsync(Type key, CancellationToken ct = default)
        {
            // try to get the populated actor first, if available
            if (TryGet(key, out var storedActor))
            {
                return storedActor;
            }

            var tcs = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waitingRegistration = new WaitForActorRegistration(key, tcs);
            var registration = ct.Register(CancelWaiter(key, ct, waitingRegistration), _actorWaiters);
            waitingRegistration.CancellationRegistration = registration;

            var r = _actorWaiters.AddOrUpdate(key,
                type => { return ImmutableHashSet<WaitForActorRegistration>.Empty.Add(waitingRegistration); },
                (type, set) => { return set.Add(waitingRegistration); });

            var b = r;


            return await tcs.Task.ConfigureAwait(false);
        }

        private void NotifyWaiters(Type key, IActorRef value)
        {
            // remove the registrations and then iterate over them
            if (_actorWaiters.TryRemove(key, out var registrations))
            {
                foreach (var r in registrations)
                {
                    r.Waiter.TrySetResult(value);
                    r.CancellationRegistration.Dispose();
                }
            }
        }

        private static Action<object?> CancelWaiter(Type key, CancellationToken ct,
            WaitForActorRegistration waitingRegistration)
        {
            return dict =>
            {
                if (dict is null)
                    return;
                
                // first step during timeout is to remove our registration
                var d = (ConcurrentDictionary<Type, ImmutableHashSet<WaitForActorRegistration>>)dict;
                d.AddOrUpdate(key, type => ImmutableHashSet<WaitForActorRegistration>.Empty,
                    (type, set) => set.Remove(waitingRegistration));

                // next, cancel the task
                waitingRegistration.Waiter.TrySetCanceled(ct);
            };
        }

        /// <summary>
        /// Fetches the <see cref="IActorRef"/> by key.
        /// </summary>
        /// <typeparam name="TKey">The key type to retrieve this actor.</typeparam>
        /// <returns>If found, the underlying <see cref="IActorRef"/>.
        /// If not found, returns <see cref="ActorRefs.Nobody"/>.</returns>
        public IActorRef Get<TKey>()
        {
            if (TryGet<TKey>(out var actor))
                return actor;
            throw new MissingActorRegistryEntryException("No actor registered for key " + typeof(TKey));
        }

        /// <summary>
        /// Allows enumerated access to the collection of all registered actors.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<Type, IActorRef>> GetEnumerator()
        {
            return _actorRegistrations.GetEnumerator();
        }

        /// <summary>
        /// Allows enumerated access to the collection of all registered actors.
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public static ActorRegistry For(ActorSystem actorSystem)
        {
            return actorSystem.WithExtension<ActorRegistry, ActorRegistryExtension>();
        }
    }

    /// <summary>
    /// Represents a read-only collection of <see cref="IActorRef"/> instances keyed by the actor name.
    /// </summary>
    public interface IReadOnlyActorRegistry : IEnumerable<KeyValuePair<Type, IActorRef>>
    {
        /// <summary>
        /// Try to retrieve an <see cref="IActorRef"/> with the given <see cref="TKey"/>.
        /// </summary>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <returns><c>true</c> if an actor with this key exists, <c>false</c> otherwise.</returns>
        bool TryGet<TKey>(out IActorRef actor);

        /// <summary>
        /// Try to retrieve an <see cref="IActorRef"/> with the given <see cref="TKey"/>.
        /// </summary>
        /// <param name="key">The key for a particular actor.</param>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <returns><c>true</c> if an actor with this key exists, <c>false</c> otherwise.</returns>
        bool TryGet(Type key, out IActorRef actor);

        /// <summary>
        /// Fetches the <see cref="IActorRef"/> by key.
        /// </summary>
        /// <typeparam name="TKey">The key type to retrieve this actor.</typeparam>
        /// <returns>If found, the underlying <see cref="IActorRef"/>.
        /// If not found, returns <see cref="ActorRefs.Nobody"/>.</returns>
        IActorRef Get<TKey>();

        /// <summary>
        /// Asynchronously fetches the <see cref="IActorRef"/> by key. Task will complete when the actor is registered.
        /// </summary>
        /// <param name="ct">The CancellationToken that can be used to cancel the GetAsync operation.</param>
        /// <typeparam name="TKey">The key type to retrieve this actor.</typeparam>
        /// <returns>A <see cref="Task{IActorRef}"/> that will complete when the actor is registered or will throw
        /// a <see cref="TaskCanceledException"/> in the event that the <see cref="CancellationToken"/> is invoked.</returns>
        public Task<IActorRef> GetAsync<TKey>(CancellationToken ct = default);

        /// <summary>
        /// Asynchronously fetches the <see cref="IActorRef"/> by key. Task will complete when the actor is registered.
        /// </summary>
        /// <param name="ct">The CancellationToken that can be used to cancel the GetAsync operation.</param>
        /// <param name="key">The key type to retrieve this actor.</param>
        /// <returns>A <see cref="Task{IActorRef}"/> that will complete when the actor is registered or will throw
        /// a <see cref="TaskCanceledException"/> in the event that the <see cref="CancellationToken"/> is invoked.</returns>
        public Task<IActorRef> GetAsync(Type key, CancellationToken ct = default);
    }

    /// <summary>
    /// An abstraction to allow <see cref="IActorRef"/> instances to be injected to non-Akka classes (such as controllers and SignalR Hubs).
    /// </summary>
    /// <remarks>
    /// Should only be used for top-level actors that need to be accessed from inside or outside the <see cref="ActorSystem"/>.
    ///
    /// If you are adding every single actor in your <see cref="ActorSystem"/> to the registry you are definitely using it wrong.
    /// </remarks>
    public interface IActorRegistry : IReadOnlyActorRegistry
    {
        /// <summary>
        /// Registers an actor into the registry. Throws an exception upon failure.
        /// </summary>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <param name="overwrite">If <c>true</c>, allows overwriting of a previous actor with the same key. Defaults to <c>false</c>.</param>
        void Register<TKey>(IActorRef actor, bool overwrite = false);

        /// <summary>
        /// Attempts to register an actor with the registry.
        /// </summary>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <param name="overwrite">If <c>true</c>, allows overwriting of a previous actor with the same key. Defaults to <c>false</c>.</param>
        /// <returns><c>true</c> if the actor was set to this key in the registry, <c>false</c> otherwise.</returns>
        bool TryRegister<TKey>(IActorRef actor, bool overwrite = false);

        /// <summary>
        /// Attempts to register an actor with the registry.
        /// </summary>
        /// <param name="key">The key for a particular actor.</param>
        /// <param name="actor">The bound <see cref="IActorRef"/>, if any. Is set to <see cref="ActorRefs.Nobody"/> if key is not found.</param>
        /// <param name="overwrite">If <c>true</c>, allows overwriting of a previous actor with the same key. Defaults to <c>false</c>.</param>
        /// <returns><c>true</c> if the actor was set to this key in the registry, <c>false</c> otherwise.</returns>
        bool TryRegister(Type key, IActorRef actor, bool overwrite = false);
    }
}