//-----------------------------------------------------------------------
// <copyright file="ActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{

    /// <summary>
    /// All ActorRefs have a scope which describes where they live. Since it is often
    /// necessary to distinguish between local and non-local references, this is the only
    /// method provided on the scope.
    /// INTERNAL
    /// </summary>
    public interface IActorRefScope
    {
        /// <summary>
        /// TBD
        /// </summary>
        bool IsLocal { get; }
    }

    /// <summary>
    /// Marker interface for Actors that are deployed within local scope, 
    /// i.e. <see cref="IActorRefScope.IsLocal"/> always returns <c>true</c>.
    /// </summary>
    internal interface ILocalRef : IActorRefScope { }

    /// <summary>
    /// RepointableActorRef (and potentially others) may change their locality at
    /// runtime, meaning that isLocal might not be stable. RepointableActorRef has
    /// the feature that it starts out “not fully started” (but you can send to it),
    /// which is why <see cref="IsStarted"/> features here; it is not improbable that cluster
    /// actor refs will have the same behavior.
    /// INTERNAL
    /// </summary>
    public interface IRepointableRef : IActorRefScope
    {
        /// <summary>
        /// TBD
        /// </summary>
        bool IsStarted { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class FutureActorRef : MinimalActorRef
    {
        private readonly TaskCompletionSource<object> _result;
        private readonly Action _unregister;
        private readonly ActorPath _path;

        /// <summary>
        /// TBD
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

            if (message is ISystemMessage) //we have special handling for system messages
            {
                SendSystemMessage(message.AsInstanceOf<ISystemMessage>());
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
    /// TBD
    /// </summary>
    internal static class ActorRefSender
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static IActorRef GetSelfOrNoSender()
        {
            var actorCell = ActorCell.Current;
            return actorCell != null ? actorCell.Self : ActorRefs.NoSender;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IActorRef : ICanTell, IEquatable<IActorRef>, IComparable<IActorRef>, ISurrogated, IComparable
    {
        /// <summary>
        /// TBD
        /// </summary>
        ActorPath Path { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class ActorRefImplicitSenderExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
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
    /// TBD
    /// </summary>
    public static class ActorRefs
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Nobody Nobody = Nobody.Instance;
        /// <summary>
        /// Use this value as an argument to <see cref="ICanTell.Tell"/> if there is not actor to
        /// reply to (e.g. when sending from non-actor code).
        /// </summary>
        public static readonly IActorRef NoSender = null;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class ActorRefBase : IActorRef
    {
        /// <summary>
        /// TBD
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
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            if(Path.Uid == ActorCell.UndefinedUid) return $"[{Path}]";
            return $"[{Path}#{Path.Uid}]";
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            var other = obj as IActorRef;
            if (other == null) return false;
            return Equals(other);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="obj"/> isn't an <see cref="IActorRef"/>.
        /// </exception>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has these meanings: Value Meaning Less than zero This instance precedes <paramref name="obj" /> in the sort order. Zero This instance occurs in the same position in the sort order as <paramref name="obj" />. Greater than zero This instance follows <paramref name="obj" /> in the sort order.
        /// </returns>
        public int CompareTo(object obj)
        {
            if (obj != null && !(obj is IActorRef))
                throw new ArgumentException("Object must be of type IActorRef.", nameof(obj));
            return CompareTo((IActorRef) obj);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(IActorRef other)
        {
            return Path.Uid == other.Path.Uid 
                && Path.Equals(other.Path);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public int CompareTo(IActorRef other)
        {
            var pathComparisonResult = Path.CompareTo(other.Path);
            if (pathComparisonResult != 0) return pathComparisonResult;
            if (Path.Uid < other.Path.Uid) return -1;
            return Path.Uid == other.Path.Uid ? 0 : 1;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public virtual ISurrogate ToSurrogate(ActorSystem system)
        {
            return new Surrogate(Serialization.Serialization.SerializedActorPath(this));
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IInternalActorRef : IActorRef, IActorRefScope
    {
        /// <summary>
        /// TBD
        /// </summary>
        IInternalActorRef Parent { get; }
        /// <summary>
        /// TBD
        /// </summary>
        IActorRefProvider Provider { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Use Context.Watch and Receive<Terminated>")]
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
        /// TBD
        /// </summary>
        /// <param name="causedByFailure">TBD</param>
        void Resume(Exception causedByFailure = null);
        /// <summary>
        /// TBD
        /// </summary>
        void Start();
        /// <summary>
        /// TBD
        /// </summary>
        void Stop();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        void Restart(Exception cause);
        /// <summary>
        /// TBD
        /// </summary>
        void Suspend();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        [Obsolete("Use SendSystemMessage(message)")]
        void SendSystemMessage(ISystemMessage message, IActorRef sender);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        void SendSystemMessage(ISystemMessage message);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class InternalActorRefBase : ActorRefBase, IInternalActorRef
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract IInternalActorRef Parent { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public abstract IActorRefProvider Provider { get; }

        /// <summary>
        /// Obtain a child given the paths element to that actor, by possibly traversing the actor tree or 
        /// looking it up at some provider-specific location. 
        /// A path element of ".." signifies the parent, a trailing "" element must be disregarded. 
        /// If the requested path does not exist, returns <see cref="Nobody"/>.
        /// </summary>
        /// <param name="name">The path elements.</param>
        /// <returns>The <see cref="IActorRef"/>, or if the requested path does not exist, returns <see cref="Nobody"/>.</returns>
        public abstract IActorRef GetChild(IEnumerable<string> name);    //TODO: Refactor this to use an IEnumerator instead as this will be faster instead of enumerating multiple times over name, as the implementations currently do.		
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="causedByFailure">TBD</param>
        public abstract void Resume(Exception causedByFailure = null);
        /// <summary>
        /// TBD
        /// </summary>
        public abstract void Start();
        /// <summary>
        /// TBD
        /// </summary>
        public abstract void Stop();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public abstract void Restart(Exception cause);
        /// <summary>
        /// TBD
        /// </summary>
        public abstract void Suspend();

        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsTerminated { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsLocal { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        [Obsolete("Use SendSystemMessage(message) instead")]
        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            SendSystemMessage(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public abstract void SendSystemMessage(ISystemMessage message);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class MinimalActorRef : InternalActorRefBase, ILocalRef
    {
        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef Parent
        {
            get { return ActorRefs.Nobody; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            if (name.All(string.IsNullOrEmpty))
                return this;
            return ActorRefs.Nobody;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="causedByFailure">TBD</param>
        public override void Resume(Exception causedByFailure = null)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Start()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Stop()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public override void Restart(Exception cause)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Suspend()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
           
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsLocal
        {
            get { return true; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Use Context.Watch and Receive<Terminated>")]
        public override bool IsTerminated { get { return false; } }
    }

    /// <summary> This is an internal look-up failure token, not useful for anything else.</summary>
    public sealed class Nobody : MinimalActorRef
    {
        /// <summary>
        /// TBD
        /// </summary>
        public class NobodySurrogate : ISurrogate
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return Nobody.Instance;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static Nobody Instance = new Nobody();
        private static readonly NobodySurrogate SurrogateInstance = new NobodySurrogate();
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/Nobody");

        private Nobody() { }

        /// <summary>
        /// TBD
        /// </summary>
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
    /// TBD
    /// </summary>
    public abstract class ActorRefWithCell : InternalActorRefBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract ICell Underlying { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract IEnumerable<IActorRef> Children { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
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
        public void RemoveChild(string name,IActorRef child)
        {
            IInternalActorRef tmp;
            if (!_children.TryRemove(name, out tmp))
            {
                Log.Warning("{0} trying to remove non-child {1}",Path,name);
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
            IInternalActorRef child;
            if (_children.TryGetValue(firstName, out child))
                return child.GetChild(new Enumerable<string>(enumerator));
            return ActorRefs.Nobody;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasChildren
        {
            get
            {
                return !_children.IsEmpty;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
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
}

