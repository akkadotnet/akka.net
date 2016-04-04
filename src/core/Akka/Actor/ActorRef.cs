//-----------------------------------------------------------------------
// <copyright file="ActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
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
        bool IsStarted { get; }
    }

    public class FutureActorRef : MinimalActorRef
    {
        private readonly TaskCompletionSource<object> _result;
        private readonly Action _unregister;
        private readonly ActorPath _path;

        public FutureActorRef(TaskCompletionSource<object> result, Action unregister, ActorPath path)
        {
            if (ActorCell.Current != null)
            {
                _actorAwaitingResultSender = ActorCell.Current.Sender;
            }
            _result = result;
            _unregister = unregister;
            _path = path;
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        public override IActorRefProvider Provider
        {
            get { throw new NotImplementedException(); }
        }


        private const int INITIATED = 0;
        private const int COMPLETED = 1;
        private int status = INITIATED;
        private readonly IActorRef _actorAwaitingResultSender;

        protected override void TellInternal(object message, IActorRef sender)
        {

            if (message is ISystemMessage) //we have special handling for system messages
            {
                SendSystemMessage(message.AsInstanceOf<ISystemMessage>(), sender);
            }
            else
            {
                if (Interlocked.Exchange(ref status, COMPLETED) == INITIATED)
                {
                    _unregister();
                    _result.TrySetResult(message);
                }
            }
        }
    }



    internal static class ActorRefSender
    {
        public static IActorRef GetSelfOrNoSender()
        {
            var actorCell = ActorCell.Current;
            return actorCell != null ? actorCell.Self : ActorRefs.NoSender;
        }
    }
    public interface IActorRef : ICanTell, IEquatable<IActorRef>, IComparable<IActorRef>, ISurrogated, IComparable
    {
        ActorPath Path { get; }
    }

    public static class ActorRefImplicitSenderExtensions
    {
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
    public static class ActorRefs
    {
        public static readonly Nobody Nobody = Nobody.Instance;
        /// <summary>
        /// Use this value as an argument to <see cref="ICanTell.Tell"/> if there is not actor to
        /// reply to (e.g. when sending from non-actor code).
        /// </summary>
        public static readonly IActorRef NoSender = null;
    }

    public abstract class ActorRefBase : IActorRef
    {
        public class Surrogate : ISurrogate
        {
            public Surrogate(string path)
            {
                Path = path;
            }

            public string Path { get; private set; }

            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return ((ActorSystemImpl)system).Provider.ResolveActorRef(Path);
            }
        }

        public abstract ActorPath Path { get; }

        public void Tell(object message, IActorRef sender)
        {
            if (sender == null)
            {
                sender = ActorRefs.NoSender;
            }

            TellInternal(message, sender);
        }

        protected abstract void TellInternal(object message, IActorRef sender);

        public override string ToString()
        {
            return string.Format("[{0}]", Path);
        }

        public override bool Equals(object obj)
        {
            var other = obj as IActorRef;
            if (other == null) return false;
            return Equals(other);
        }

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

        public int CompareTo(object obj)
        {
            if (obj != null && !(obj is IActorRef))
                throw new ArgumentException("Object must be of type IActorRef.");
            return CompareTo((IActorRef) obj);
        }

        public bool Equals(IActorRef other)
        {
            return Path.Uid == other.Path.Uid && Path.Equals(other.Path);
        }

        public int CompareTo(IActorRef other)
        {
            var pathComparisonResult = Path.CompareTo(other.Path);
            if (pathComparisonResult != 0) return pathComparisonResult;
            if (Path.Uid < other.Path.Uid) return -1;
            return Path.Uid == other.Path.Uid ? 0 : 1;
        }

        public virtual ISurrogate ToSurrogate(ActorSystem system)
        {
            return new Surrogate(Serialization.Serialization.SerializedActorPath(this));
        }
    }


    public interface IInternalActorRef : IActorRef, IActorRefScope
    {
        IInternalActorRef Parent { get; }
        IActorRefProvider Provider { get; }
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

        void Resume(Exception causedByFailure = null);
        void Start();
        void Stop();
        void Restart(Exception cause);
        void Suspend();
        void SendSystemMessage(ISystemMessage message, IActorRef sender);
    }

    public abstract class InternalActorRefBase : ActorRefBase, IInternalActorRef
    {
        public abstract IInternalActorRef Parent { get; }
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
        public abstract void Resume(Exception causedByFailure = null);
        public abstract void Start();
        public abstract void Stop();
        public abstract void Restart(Exception cause);
        public abstract void Suspend();

        public abstract bool IsTerminated { get; }
        public abstract bool IsLocal { get; }
        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            var d = message as DeathWatchNotification;
            if (message is Terminate)
            {
                Stop();
            }
            else if (d != null)
            {
                this.Tell(new Terminated(d.Actor, d.ExistenceConfirmed, d.AddressTerminated));
            }
        }
    }

    public abstract class MinimalActorRef : InternalActorRefBase, ILocalRef
    {
        public override IInternalActorRef Parent
        {
            get { return ActorRefs.Nobody; }
        }

        public override IActorRef GetChild(IEnumerable<string> name)
        {
            if (name.All(string.IsNullOrEmpty))
                return this;
            return ActorRefs.Nobody;
        }

        public override void Resume(Exception causedByFailure = null)
        {
        }

        public override void Start()
        {
        }

        public override void Stop()
        {
        }

        public override void Restart(Exception cause)
        {
        }

        public override void Suspend()
        {
        }

        protected override void TellInternal(object message, IActorRef sender)
        {
        }

        public override bool IsLocal
        {
            get { return true; }
        }

        public override bool IsTerminated { get { return false; } }
    }

    /// <summary> This is an internal look-up failure token, not useful for anything else.</summary>
    public sealed class Nobody : MinimalActorRef
    {
        public class NobodySurrogate : ISurrogate
        {
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return Nobody.Instance;
            }
        }
        public static Nobody Instance = new Nobody();
        private static readonly NobodySurrogate SurrogateInstance = new NobodySurrogate();
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/Nobody");

        private Nobody() { }

        public override ActorPath Path { get { return _path; } }

        public override IActorRefProvider Provider
        {
            get { throw new NotSupportedException("Nobody does not provide"); }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return SurrogateInstance;
        }
    }

    public abstract class ActorRefWithCell : InternalActorRefBase
    {
        public abstract ICell Underlying { get; }

        public abstract IEnumerable<IActorRef> Children { get; }

        public abstract IInternalActorRef GetSingleChild(string name);

    }

    internal class VirtualPathContainer : MinimalActorRef
    {
        private readonly IInternalActorRef _parent;
        private readonly ILoggingAdapter _log;
        private readonly IActorRefProvider _provider;
        private readonly ActorPath _path;

        private readonly ConcurrentDictionary<string, IInternalActorRef> _children = new ConcurrentDictionary<string, IInternalActorRef>();

        public VirtualPathContainer(IActorRefProvider provider, ActorPath path, IInternalActorRef parent, ILoggingAdapter log)
        {
            _parent = parent;
            _log = log;
            _provider = provider;
            _path = path;
        }

        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }

        public override IInternalActorRef Parent
        {
            get { return _parent; }
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        public ILoggingAdapter Log
        {
            get { return _log; }
        }


        protected bool TryGetChild(string name, out IInternalActorRef child)
        {
            return _children.TryGetValue(name, out child);
        }

        public void AddChild(string name, IInternalActorRef actor)
        {
            _children.AddOrUpdate(name, actor, (k, v) =>
            {
                Log.Warning("{0} replacing child {1} ({2} -> {3})", name, actor, v, actor);
                return v;
            });
        }

        public void RemoveChild(string name)
        {
            IInternalActorRef tmp;
            if (!_children.TryRemove(name, out tmp))
            {
                Log.Warning("{0} trying to remove non-child {1}", Path, name);
            }
        }

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

        public bool HasChildren
        {
            get
            {
                return !_children.IsEmpty;
            }
        }

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

