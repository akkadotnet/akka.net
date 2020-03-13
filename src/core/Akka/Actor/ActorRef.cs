//-----------------------------------------------------------------------
// <copyright file="ActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Annotations;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{

    /// <summary>
    /// INTERNAL API
    /// 
    /// All ActorRefs have a scope which describes where they live. Since it is often
    /// necessary to distinguish between local and non-local references, this is the only
    /// method provided on the scope. 
    /// </summary>
    [InternalApi]
    public interface IActorRefScope
    {
        /// <summary>
        /// Returns <c>true</c> if the actor is local to this <see cref="ActorSystem"/>.
        /// Returns <c>false</c> if the actor is remote.
        /// </summary>
        bool IsLocal { get; }
    }

    /// <summary>
    /// Marker interface for Actors that are deployed within local scope, 
    /// i.e. <see cref="IActorRefScope.IsLocal"/> always returns <c>true</c>.
    /// </summary>
    internal interface ILocalRef : IActorRefScope { }

    /// <summary>
    /// INTERNAL API
    /// 
    /// RepointableActorRef (and potentially others) may change their locality at
    /// runtime, meaning that isLocal might not be stable. RepointableActorRef has
    /// the feature that it starts out "not fully started" (but you can send to it),
    /// which is why <see cref="IsStarted"/> features here; it is not improbable that cluster
    /// actor refs will have the same behavior. 
    /// </summary>
    public interface IRepointableRef : IActorRefScope
    {
        /// <summary>
        /// Returns <c>true</c> if this actor has started yet. <c>false</c> otherwise.
        /// </summary>
        bool IsStarted { get; }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// ActorRef implementation used for one-off tasks.
    /// </summary>
    public class FutureActorRef : MinimalActorRef
    {
        private readonly TaskCompletionSource<object> _result;
        private readonly Action _unregister;
        private readonly ActorPath _path;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="result">TBD</param>
        /// <param name="unregister">TBD</param>
        /// <param name="path">TBD</param>
        public FutureActorRef(TaskCompletionSource<object> result, Action unregister, ActorPath path)
        {
            if (ActorCell.Current != null)
            {
                _actorAwaitingResultSender = ActorCell.Current.Sender;
            }
            _result = result;
            _unregister = unregister;
            _path = path;
            _result.Task.ContinueWith(_ => _unregister());
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="System.NotImplementedException">TBD</exception>
        public override IActorRefProvider Provider
        {
            get { throw new NotImplementedException(); }
        }


        private const int INITIATED = 0;
        private const int COMPLETED = 1;
        private int status = INITIATED;
        private readonly IActorRef _actorAwaitingResultSender;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        protected override void TellInternal(object message, IActorRef sender)
        {

            if (message is ISystemMessage sysM) //we have special handling for system messages
            {
                SendSystemMessage(sysM);
            }
            else
            {
                if (Interlocked.Exchange(ref status, COMPLETED) == INITIATED)
                {
                    _result.TrySetResult(message);
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            base.SendSystemMessage(message);
        }
    }


    /// <summary>
    /// INTERNAL API.
    /// </summary>
    internal static class ActorRefSender
    {
        /// <summary>
        /// Gets the current actor, if any. Otherwise <see cref="ActorRefs.NoSender"/>.
        /// </summary>
        /// <returns>The current <see cref="IActorRef"/>, if applicable. If not, <see cref="ActorRefs.NoSender"/>.</returns>
        public static IActorRef GetSelfOrNoSender()
        {
            var actorCell = ActorCell.Current;
            return actorCell != null ? actorCell.Self : ActorRefs.NoSender;
        }
    }

    /// <summary>
    /// An actor reference. Acts as a handle to an actor. Used to send messages to an actor, whether an actor is local or remote.
    /// If you receive a reference to an actor, that actor is guaranteed to have existed at some point
    /// in the past. However, an actor can always be terminated in the future.
    /// 
    /// If you want to be notified about an actor terminating, call <see cref="ICanWatch.Watch(IActorRef)">IActorContext.Watch</see>
    /// on this actor and you'll receive a <see cref="Terminated"/> message when the actor dies or if it
    /// is already dead.
    /// </summary>
    /// <remarks>Actor references can be serialized and passed over the network.</remarks>
    public interface IActorRef : ICanTell, IEquatable<IActorRef>, IComparable<IActorRef>, ISurrogated, IComparable
    {
        /// <summary>
        /// The path of this actor. Can be used to extract information about whether or not this actor is local or remote.
        /// </summary>
        ActorPath Path { get; }
    }

    /// <summary>
    /// Extension method class. Used to deliver messages to <see cref="IActorRef"/> instances
    /// via <see cref="Tell"/> and <see cref="Forward"/> and pass along information about the current sender.
    /// </summary>
    public static class ActorRefImplicitSenderExtensions
    {
        /// <summary>
        /// Asynchronously tells a message to an <see cref="IActorRef"/>.
        /// </summary>
        /// <param name="receiver">The actor who will receive the message.</param>
        /// <param name="message">The message.</param>
        /// <remarks>Will automatically resolve the current sender using the current <see cref="ActorCell"/>, if any.</remarks>
        public static void Tell(this IActorRef receiver, object message)
        {
            var sender = ActorCell.GetCurrentSelfOrNoSender();
            receiver.Tell(message, sender);
        }


        /// <summary>
        /// Forwards the message using the current Sender
        /// </summary>
        /// <param name="receiver">The actor that receives the forward</param>
        /// <param name="message">The message to forward</param>
        public static void Forward(this IActorRef receiver, object message)
        {
            var sender = ActorCell.GetCurrentSenderOrNoSender();
            receiver.Tell(message, sender);
        }

    }

    /// <summary>
    /// Utility class for working with built-in actor references
    /// </summary>
    public static class ActorRefs
    {
        /// <summary>
        /// Use this value to represent a non-existent actor.
        /// </summary>
        public static readonly Nobody Nobody = Nobody.Instance;
        /// <summary>
        /// Use this value as an argument to <see cref="ICanTell.Tell"/> if there is not actor to
        /// reply to (e.g. when sending from non-actor code).
        /// </summary>
        public static readonly IActorRef NoSender = null;
    }

    /// <summary>
    /// Base implementation for <see cref="IActorRef"/> implementations.
    /// </summary>
    public abstract class ActorRefBase : IActorRef
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="ActorRefBase"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class Surrogate : ISurrogate
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="path">TBD</param>
            public Surrogate(string path)
            {
                Path = path;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public string Path { get; }

            /// <summary>
            /// Creates an <see cref="ActorRefBase"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that contains this <see cref="ActorRefBase"/>.</param>
            /// <returns>The <see cref="ActorRefBase"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return ((ActorSystemImpl)system).Provider.ResolveActorRef(Path);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract ActorPath Path { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public void Tell(object message, IActorRef sender)
        {
            if (sender == null)
            {
                sender = ActorRefs.NoSender;
            }

            TellInternal(message, sender);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        protected abstract void TellInternal(object message, IActorRef sender);

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Path.Uid == ActorCell.UndefinedUid) return $"[{Path}]";
            return $"[{Path}#{Path.Uid}]";
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is IActorRef other)
                return Equals(other);

            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + Path.Uid.GetHashCode();
                hash = hash * 23 + Path.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="obj"/> isn't an <see cref="IActorRef"/>.
        /// </exception>
        public int CompareTo(object obj)
        {
            if (obj is null) return 1;
            if(!(obj is IActorRef other))
                throw new ArgumentException($"Object must be of type IActorRef, found {obj.GetType()} instead.", nameof(obj));

            return CompareTo(other);
        }

        /// <summary>
        /// Checks equality between this instance and another object.
        /// </summary>
        /// <param name="other"></param>
        /// <returns>
        /// <c>true</c> if this <see cref="IActorRef"/> instance have the same reference 
        /// as the <paramref name="other"/> instance, if this <see cref="ActorPath"/> of 
        /// this <see cref="IActorRef"/> instance is equal to the <paramref name="other"/> instance, 
        /// and <paramref name="other"/> is not <c>null</c>; otherwise <c>false</c>.
        /// </returns>
        public bool Equals(IActorRef other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return Path.Uid == other.Path.Uid
                && Path.Equals(other.Path);
        }

        /// <inheritdoc/>
        public int CompareTo(IActorRef other)
        {
            if (other is null) return 1;

            var pathComparisonResult = Path.CompareTo(other.Path);
            if (pathComparisonResult != 0) return pathComparisonResult;
            if (Path.Uid < other.Path.Uid) return -1;
            return Path.Uid == other.Path.Uid ? 0 : 1;
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="ActorRefBase"/>.
        /// </summary>
        /// <param name="system">The actor system that references this <see cref="ActorRefBase"/>.</param>
        /// <returns>The surrogate representation of the current <see cref="ActorRefBase"/>.</returns>
        public virtual ISurrogate ToSurrogate(ActorSystem system)
        {
            return new Surrogate(Serialization.Serialization.SerializedActorPath(this));
        }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Used by built-in <see cref="IActorRef"/> implementations for handling
    /// internal operations that are not exposed directly to end-users.
    /// </summary>
    [InternalApi]
    public interface IInternalActorRef : IActorRef, IActorRefScope
    {
        /// <summary>
        /// The parent of this actor.
        /// </summary>
        IInternalActorRef Parent { get; }
        /// <summary>
        /// The <see cref="IActorRefProvider"/> used by the <see cref="ActorSystem"/>
        /// to which this actor belongs.
        /// </summary>
        IActorRefProvider Provider { get; }

        /// <summary>
        /// Obsolete. Use <see cref="Watch"/> or <see cref="ReceiveActor.Receive{T}(Action{T}, Predicate{T})">Receive&lt;<see cref="Akka.Actor.Terminated"/>&gt;</see>
        /// </summary>
        [Obsolete("Use Context.Watch and Receive<Terminated> [1.1.0]")]
        bool IsTerminated { get; }

        /// <summary>
        /// Obtain a child given the paths element to that actor, by possibly traversing the actor tree or 
        /// looking it up at some provider-specific location. 
        /// A path element of ".." signifies the parent, a trailing "" element must be disregarded. 
        /// If the requested path does not exist, returns <see cref="Nobody"/>.
        /// </summary>
        /// <param name="name">The path elements.</param>
        /// <returns>The <see cref="IActorRef"/>, or if the requested path does not exist, returns <see cref="Nobody"/>.</returns>
        IActorRef GetChild(IEnumerable<string> name);

        /// <summary>
        /// Resumes an actor if it has been suspended.
        /// </summary>
        /// <param name="causedByFailure">Optional. Passed in if the actor is resuming as a result of recovering from failure.</param>
        void Resume(Exception causedByFailure = null);

        /// <summary>
        /// Start a newly created actor.
        /// </summary>
        void Start();

        /// <summary>
        /// Stop the actor. Terminates it permanently.
        /// </summary>
        void Stop();

        /// <summary>
        /// Restart the actor.
        /// </summary>
        /// <param name="cause">The exception that caused the actor to fail in the first place.</param>
        void Restart(Exception cause);

        /// <summary>
        /// Suspend the actor. Actor will not process any more messages until <see cref="Resume"/> is called.
        /// </summary>
        void Suspend();

        /// <summary>
        /// Obsolete. Use <see cref="SendSystemMessage(ISystemMessage)"/> instead.
        /// </summary>
        /// <param name="message">N/A</param>
        /// <param name="sender">N/A</param>
        [Obsolete("Use SendSystemMessage(message) [1.1.0]")]
        void SendSystemMessage(ISystemMessage message, IActorRef sender);

        /// <summary>
        /// Sends an <see cref="ISystemMessage"/> to the underlying actor.
        /// </summary>
        /// <param name="message">The system message we're sending.</param>
        void SendSystemMessage(ISystemMessage message);
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Abstract implementation of <see cref="IInternalActorRef"/>.
    /// </summary>
    [InternalApi]
    public abstract class InternalActorRefBase : ActorRefBase, IInternalActorRef
    {
        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract IInternalActorRef Parent { get; }

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract IActorRefProvider Provider { get; }

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract IActorRef GetChild(IEnumerable<string> name);    //TODO: Refactor this to use an IEnumerator instead as this will be faster instead of enumerating multiple times over name, as the implementations currently do.		

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract void Resume(Exception causedByFailure = null);

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract void Start();

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract void Stop();

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract void Restart(Exception cause);

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract void Suspend();

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract bool IsTerminated { get; }

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract bool IsLocal { get; }

        /// <inheritdoc cref="IInternalActorRef"/>
        [Obsolete("Use SendSystemMessage(message) instead [1.1.0]")]
        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            SendSystemMessage(message);
        }

        /// <inheritdoc cref="IInternalActorRef"/>
        public abstract void SendSystemMessage(ISystemMessage message);
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Barebones <see cref="IActorRef"/> with no backing actor or <see cref="ActorCell"/>.
    /// </summary>
    [InternalApi]
    public abstract class MinimalActorRef : InternalActorRefBase, ILocalRef
    {
        /// <inheritdoc cref="InternalActorRefBase"/>
        public override IInternalActorRef Parent
        {
            get { return ActorRefs.Nobody; }
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            if (name.All(string.IsNullOrEmpty))
                return this;
            return ActorRefs.Nobody;
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override void Resume(Exception causedByFailure = null)
        {
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override void Start()
        {
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override void Stop()
        {
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override void Restart(Exception cause)
        {
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override void Suspend()
        {
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        protected override void TellInternal(object message, IActorRef sender)
        {
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override void SendSystemMessage(ISystemMessage message)
        {

        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override bool IsLocal
        {
            get { return true; }
        }

        /// <inheritdoc cref="InternalActorRefBase"/>
        [Obsolete("Use Context.Watch and Receive<Terminated> [1.1.0]")]
        public override bool IsTerminated { get { return false; } }
    }

    /// <summary> This is an internal look-up failure token, not useful for anything else.</summary>
    public sealed class Nobody : MinimalActorRef
    {
        /// <summary>
        /// A surrogate for serializing <see cref="Nobody"/>.
        /// </summary>
        public class NobodySurrogate : ISurrogate
        {
            /// <summary>
            /// Converts the <see cref="ISurrogate"/> into a <see cref="IActorRef"/>.
            /// </summary>
            /// <param name="system">The actor system.</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return Nobody.Instance;
            }
        }

        /// <summary>
        /// Singleton instance of <see cref="Nobody"/>.
        /// </summary>
        public static Nobody Instance = new Nobody();

        private static readonly NobodySurrogate SurrogateInstance = new NobodySurrogate();
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/Nobody");

        private Nobody() { }

        /// <inheritdoc cref="InternalActorRefBase"/>
        public override ActorPath Path { get { return _path; } }

        /// <summary>N/A</summary>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since this actor doesn't have a provider.
        /// </exception>
        public override IActorRefProvider Provider
        {
            get { throw new NotSupportedException("Nobody does not provide"); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return SurrogateInstance;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Used to power actors that use an <see cref="ActorCell"/>, which is the majority of them.
    /// </summary>
    [InternalApi]
    public abstract class ActorRefWithCell : InternalActorRefBase
    {
        /// <summary>
        /// The <see cref="ActorCell"/>.
        /// </summary>
        public abstract ICell Underlying { get; }

        /// <summary>
        /// An iterable collection of the actor's children. Empty if there are none.
        /// </summary>
        public abstract IEnumerable<IActorRef> Children { get; }

        /// <summary>
        /// Fetches a reference to a single child actor.
        /// </summary>
        /// <param name="name">The name of the child we're trying to fetch.</param>
        /// <returns>If the child exists, it returns the child actor. Otherwise, we return <see cref="ActorRefs.Nobody"/>.</returns>
        public abstract IInternalActorRef GetSingleChild(string name);

    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class VirtualPathContainer : MinimalActorRef
    {
        private readonly IInternalActorRef _parent;
        private readonly ILoggingAdapter _log;
        private readonly IActorRefProvider _provider;
        private readonly ActorPath _path;

        private readonly ConcurrentDictionary<string, IInternalActorRef> _children = new ConcurrentDictionary<string, IInternalActorRef>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="provider">TBD</param>
        /// <param name="path">TBD</param>
        /// <param name="parent">TBD</param>
        /// <param name="log">TBD</param>
        public VirtualPathContainer(IActorRefProvider provider, ActorPath path, IInternalActorRef parent, ILoggingAdapter log)
        {
            _parent = parent;
            _log = log;
            _provider = provider;
            _path = path;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef Parent
        {
            get { return _parent; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ILoggingAdapter Log
        {
            get { return _log; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        protected bool TryGetChild(string name, out IInternalActorRef child)
        {
            return _children.TryGetValue(name, out child);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="actor">TBD</param>
        public void AddChild(string name, IInternalActorRef actor)
        {
            _children.AddOrUpdate(name, actor, (k, v) =>
            {
                Log.Warning("{0} replacing child {1} ({2} -> {3})", name, actor, v, actor);
                return v;
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public void RemoveChild(string name)
        {
            IInternalActorRef tmp;
            if (!_children.TryRemove(name, out tmp))
            {
                Log.Warning("{0} trying to remove non-child {1}", Path, name);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="child">TBD</param>
        public void RemoveChild(string name, IActorRef child)
        {
            IInternalActorRef tmp;
            if (!_children.TryRemove(name, out tmp))
            {
                Log.Warning("{0} trying to remove non-child {1}", Path, name);
            }
        }

        /*
override def getChild(name: Iterator[String]): InternalActorRef = {
    if (name.isEmpty) this
    else {
      val n = name.next()
      if (n.isEmpty) this
      else children.get(n) match {
        case null ⇒ Nobody
        case some ⇒
          if (name.isEmpty) some
          else some.getChild(name)
      }
    }
  }
*/

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            //Using enumerator to avoid multiple enumerations of name.
            var enumerator = name.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                //name was empty
                return this;
            }
            var firstName = enumerator.Current;
            if (string.IsNullOrEmpty(firstName))
                return this;
            if (_children.TryGetValue(firstName, out var child))
                return child.GetChild(new Enumerable<string>(enumerator));
            return ActorRefs.Nobody;
        }

        /// <summary>
        /// Returns <c>true</c> if the <see cref="VirtualPathContainer"/> contains any children, 
        /// <c>false</c> otherwise.
        /// </summary>
        public bool HasChildren
        {
            get
            {
                return !_children.IsEmpty;
            }
        }

        /// <summary>
        /// Executes an action for each child in the current collection.
        /// </summary>
        /// <param name="action">A lambda which takes a reference to the internal child actor as an argument.</param>
        public void ForEachChild(Action<IInternalActorRef> action)
        {
            foreach (IInternalActorRef child in _children.Values)
            {
                action(child);
            }
        }

        /// <summary>
        /// An enumerable that continues where the supplied enumerator is positioned
        /// </summary>
        private class Enumerable<T> : IEnumerable<T>
        {
            private readonly IEnumerator<T> _enumerator;

            public Enumerable(IEnumerator<T> enumerator)
            {
                _enumerator = enumerator;
            }

            /// <inheritdoc/>
            public IEnumerator<T> GetEnumerator()
            {
                return _enumerator;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// This kind of ActorRef passes all received messages to the given function for
    /// performing a non-blocking side-effect. The intended use is to transform the
    /// message before sending to the real target actor. Such references can be created
    /// by calling <see cref="ActorCell.AddFunctionRef(Action{IActorRef, object}, string)"/> and must be deregistered when no longer
    /// needed by calling <see cref="ActorCell.RemoveFunctionRef(FunctionRef)"/>. FunctionRefs do not count
    /// towards the live children of an actor, they do not receive the Terminate command
    /// and do not prevent the parent from terminating. FunctionRef is properly
    /// registered for remote lookup and ActorSelection.
    /// 
    /// When using the <see cref="ICanWatch.Watch"/> feature you must ensure that upon reception of the
    /// Terminated message the watched actorRef is <see cref="ICanWatch.Unwatch"/>ed.
    /// </summary>
    internal sealed class FunctionRef : MinimalActorRef
    {
        private readonly EventStream _eventStream;
        private readonly Action<IActorRef, object> _tell;

        private ImmutableHashSet<IActorRef> _watching = ImmutableHashSet<IActorRef>.Empty;
        private ImmutableHashSet<IActorRef> _watchedBy = ImmutableHashSet<IActorRef>.Empty;

        public FunctionRef(ActorPath path, IActorRefProvider provider, EventStream eventStream, Action<IActorRef, object> tell)
        {
            _eventStream = eventStream;
            _tell = tell;
            Path = path;
            Provider = provider;
        }

        public override ActorPath Path { get; }
        public override IActorRefProvider Provider { get; }
        public override bool IsTerminated => Volatile.Read(ref _watchedBy) == null;

        /// <summary>
        /// Have this FunctionRef watch the given Actor. This method must not be
        /// called concurrently from different threads, it should only be called by
        /// its parent Actor.
        /// 
        /// Upon receiving the Terminated message, <see cref="Unwatch"/> must be called from a
        /// safe context (i.e. normally from the parent Actor).
        /// </summary>
        public void Watch(IActorRef actorRef)
        {
            _watching = _watching.Add(actorRef);
            var internalRef = (IInternalActorRef)actorRef;
            internalRef.SendSystemMessage(new Watch(internalRef, this));
        }

        /// <summary>
        /// Have this FunctionRef unwatch the given Actor. This method must not be
        /// called concurrently from different threads, it should only be called by
        /// its parent Actor.
        /// </summary>
        public void Unwatch(IActorRef actorRef)
        {
            _watching = _watching.Remove(actorRef);
            var internalRef = (IInternalActorRef)actorRef;
            internalRef.SendSystemMessage(new Unwatch(internalRef, this));

        }

        /// <summary>
        /// Query whether this FunctionRef is currently watching the given Actor. This
        /// method must not be called concurrently from different threads, it should
        /// only be called by its parent Actor.
        /// </summary>
        public bool IsWatching(IActorRef actorRef) => _watching.Contains(actorRef);

        protected override void TellInternal(object message, IActorRef sender) => _tell(sender, message);

        public override void SendSystemMessage(ISystemMessage message)
        {
            switch (message)
            {
                case Watch watch:
                    AddWatcher(watch.Watchee, watch.Watcher);
                    break;
                case Unwatch unwatch:
                    RemoveWatcher(unwatch.Watchee, unwatch.Watcher);
                    break;
                case DeathWatchNotification deathWatch:
                    this.Tell(new Terminated(deathWatch.Actor, existenceConfirmed: true, addressTerminated: false), deathWatch.Actor);
                    break;
            }
        }

        private void SendTerminated()
        {
            var watchedBy = Interlocked.Exchange(ref _watchedBy, null);
            if (watchedBy != null)
            {
                if (!watchedBy.IsEmpty)
                {
                    foreach (var watcher in watchedBy)
                        SendTerminated(watcher);
                }

                if (!_watching.IsEmpty)
                {
                    foreach (var watched in _watching)
                        UnwatchWatched(watched);

                    _watching = ImmutableHashSet<IActorRef>.Empty;
                }
            }
        }

        private void SendTerminated(IActorRef watcher)
        {
            if (watcher is IInternalActorRef scope)
                scope.SendSystemMessage(new DeathWatchNotification(this, existenceConfirmed: true, addressTerminated: false));
        }

        private void UnwatchWatched(IActorRef watched)
        {
            if (watched is IInternalActorRef internalActorRef)
                internalActorRef.SendSystemMessage(new Unwatch(internalActorRef, this));
        }

        public override void Stop() => SendTerminated();

        private void AddWatcher(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            while (true)
            {
                var watchedBy = Volatile.Read(ref _watchedBy);
                if (watchedBy == null)
                    SendTerminated(watcher);
                else
                {
                    var watcheeSelf = Equals(watchee, this);
                    var watcherSelf = Equals(watcher, this);

                    if (watcheeSelf && !watcherSelf)
                    {
                        if (!watchedBy.Contains(watcher) && !ReferenceEquals(watchedBy, Interlocked.CompareExchange(ref _watchedBy, watchedBy.Add(watcher), watchedBy)))
                        {
                            continue;
                        }
                    }
                    else if (!watcheeSelf && watcherSelf)
                    {
                        Publish(new Warning(Path.ToString(), typeof(FunctionRef), $"Externally triggered watch from {watcher} to {watchee} is illegal on FunctionRef"));
                    }
                    else
                    {
                        Publish(new Warning(Path.ToString(), typeof(FunctionRef), $"BUG: illegal Watch({watchee},{watcher}) for {this}"));
                    }
                }

                break;
            }
        }

        private void RemoveWatcher(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            while (true)
            {
                var watchedBy = Volatile.Read(ref _watchedBy);
                if (watchedBy == null)
                    SendTerminated(watcher);
                else
                {
                    var watcheeSelf = Equals(watchee, this);
                    var watcherSelf = Equals(watcher, this);

                    if (watcheeSelf && !watcherSelf)
                    {
                        if (!watchedBy.Contains(watcher) && !ReferenceEquals(watchedBy, Interlocked.CompareExchange(ref _watchedBy, watchedBy.Remove(watcher), watchedBy)))
                        {
                            continue;
                        }
                    }
                    else if (!watcheeSelf && watcherSelf)
                    {
                        Publish(new Warning(Path.ToString(), typeof(FunctionRef), $"Externally triggered watch from {watcher} to {watchee} is illegal on FunctionRef"));
                    }
                    else
                    {
                        Publish(new Warning(Path.ToString(), typeof(FunctionRef), $"BUG: illegal Watch({watchee},{watcher}) for {this}"));
                    }
                }

                break;
            }
        }

        private void Publish(LogEvent e)
        {
            try
            {
                _eventStream.Publish(e);
            }
            catch (Exception) { }
        }
    }
}
