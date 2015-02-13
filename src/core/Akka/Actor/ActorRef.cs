﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using Akka.Actor.Internals;
using Akka.Dispatch.SysMsg;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Dispatch;
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
    // ReSharper disable once InconsistentNaming
    public interface ActorRefScope
    {
        bool IsLocal { get; }
    }

    /// <summary>
    /// Marker interface for Actors that are deployed within local scope, 
    /// i.e. <see cref="ActorRefScope.IsLocal"/> always returns <c>true</c>.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    internal interface LocalRef : ActorRefScope { }

    /// <summary>
    /// RepointableActorRef (and potentially others) may change their locality at
    /// runtime, meaning that isLocal might not be stable. RepointableActorRef has
    /// the feature that it starts out “not fully started” (but you can send to it),
    /// which is why <see cref="IsSt"/> features here; it is not improbable that cluster
    /// actor refs will have the same behavior.
    /// INTERNAL
    /// </summary>
    public interface RepointableRef : ActorRefScope
    {
        bool IsStarted { get; }
    }

    public class FutureActorRef : MinimalActorRef
    {
        private readonly TaskCompletionSource<object> _result;
        private readonly Action _unregister;
        private readonly ActorPath _path;
        private readonly ActorRef _actorAwaitingResult;

        public FutureActorRef(TaskCompletionSource<object> result, ActorRef actorAwaitingResult, Action unregister, ActorPath path)
        {
            if (ActorCell.Current != null)
            {
                _actorAwaitingResultSender = ActorCell.Current.Sender;
            }
            _result = result;
            _actorAwaitingResult = actorAwaitingResult ?? NoSender;
            _unregister = unregister;
            _path = path;
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        public override ActorRefProvider Provider
        {
            get { throw new NotImplementedException(); }
        }


        private const int INITIATED = 0;
        private const int COMPLETED = 1;
        private int status = INITIATED;
        private readonly ActorRef _actorAwaitingResultSender;

        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message is SystemMessage) //we have special handling for system messages
            {
                SendSystemMessage(message.AsInstanceOf<SystemMessage>(), sender);
            }
            else
            {
                if (Interlocked.Exchange(ref status, COMPLETED) == INITIATED)
                {
                    //hide any potential local thread state
                    var tmp = InternalCurrentActorCellKeeper.Current;
                    InternalCurrentActorCellKeeper.Current = null;
                    try
                    {
                        CallContext.LogicalSetData("akka.state", new AmbientState()
                        {
                            Self = _actorAwaitingResult,
                            Message = "",
                            Sender = _actorAwaitingResultSender,
                        });

                        _result.TrySetResult(message);
                    }
                    finally
                    {
                        InternalCurrentActorCellKeeper.Current = tmp;
                    }
                }
            }
        }

        protected void SendSystemMessage(SystemMessage message, ActorRef sender)
        {
            var d = message as DeathWatchNotification;
            if (message is Terminate)
            {
                Stop();
            }
            else if (d != null)
            {
                Tell(new Terminated(d.Actor, d.ExistenceConfirmed, d.AddressTerminated));
            }
        }
    }

    

    internal static class ActorRefSender
    {
        public static ActorRef GetSelfOrNoSender()
        {
            var actorCell = ActorCell.Current;
            return actorCell != null ? actorCell.Self : ActorRef.NoSender;
        }
    }

    public abstract class ActorRef : ICanTell, IEquatable<ActorRef>, IComparable<ActorRef>, ISurrogated
    {
        public class Surrogate : ISurrogate
        {
            public Surrogate(string path)
            {
                Path = path;
            }

            public string Path { get; private set; }

            public object FromSurrogate(ActorSystem system)
            {
                return ((ActorSystemImpl)system).Provider.ResolveActorRef(Path);
            }
        }

        public static readonly Nobody Nobody = Nobody.Instance;
        public static readonly ActorRef NoSender = Actor.NoSender.Instance; //In Akka this is just null

        public abstract ActorPath Path { get; }

        public void Tell(object message, ActorRef sender)
        {
            if (sender == null) throw new ArgumentNullException("sender", "A actorAwaitingResult must be specified");

            TellInternal(message, sender);
        }

        public void Tell(object message)
        {
            var sender = ActorCell.GetCurrentSelfOrNoSender();
            TellInternal(message, sender);
        }

        /// <summary>
        /// Forwards the message using the current Sender
        /// </summary>
        /// <param name="message"></param>
        public void Forward(object message)
        {
            var sender = ActorCell.GetCurrentSenderOrNoSender();
            TellInternal(message, sender);
        }

        protected abstract void TellInternal(object message, ActorRef sender);

        public override string ToString()
        {
            return string.Format("[{0}]", Path);
        }

        public override bool Equals(object obj)
        {
            var other = obj as ActorRef;
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

        public bool Equals(ActorRef other)
        {
            return Path.Uid == other.Path.Uid && Path.Equals(other.Path);
        }

        public static bool operator ==(ActorRef left, ActorRef right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ActorRef left, ActorRef right)
        {
            return !Equals(left, right);
        }

        public int CompareTo(ActorRef other)
        {
            var pathComparisonResult = Path.CompareTo(other.Path);
            if (pathComparisonResult != 0) return pathComparisonResult;
            if (Path.Uid < other.Path.Uid) return -1;
            return Path.Uid == other.Path.Uid ? 0 : 1;
        }

        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new Surrogate(Serialization.Serialization.SerializedActorPath(this));
        }
    }


    public abstract class InternalActorRef : ActorRef, ActorRefScope
    {
        public abstract InternalActorRef Parent { get; }
        public abstract ActorRefProvider Provider { get; }

        /// <summary>
        /// Obtain a child given the paths element to that actor, by possibly traversing the actor tree or 
        /// looking it up at some provider-specific location. 
        /// A path element of ".." signifies the parent, a trailing "" element must be disregarded. 
        /// If the requested path does not exist, returns <see cref="Nobody"/>.
        /// </summary>
        /// <param name="name">The path elements.</param>
        /// <returns>The <see cref="ActorRef"/>, or if the requested path does not exist, returns <see cref="Nobody"/>.</returns>
        public abstract ActorRef GetChild(IEnumerable<string> name);    //TODO: Refactor this to use an IEnumerator instead as this will be faster instead of enumerating multiple times over name, as the implementations currently do.
        public abstract void Resume(Exception causedByFailure = null);
        public abstract void Start();
        public abstract void Stop();
        public abstract void Restart(Exception cause);
        public abstract void Suspend();

        public abstract bool IsTerminated { get; }
        public abstract bool IsLocal { get; }
    }

    public abstract class MinimalActorRef : InternalActorRef, LocalRef
    {
        public override InternalActorRef Parent
        {
            get { return Nobody; }
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            if (name.All(string.IsNullOrEmpty))
                return this;
            return Nobody;
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

        protected override void TellInternal(object message, ActorRef sender)
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
        public static Nobody Instance = new Nobody();
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/Nobody");

        private Nobody() { }

        public override ActorPath Path { get { return _path; } }

        public override ActorRefProvider Provider
        {
            get { throw new NotSupportedException("Nobody does not provide"); }
        }

    }

    public abstract class ActorRefWithCell : InternalActorRef
    {
        public abstract Cell Underlying { get; }

        public abstract IEnumerable<ActorRef> Children { get; }

        public abstract InternalActorRef GetSingleChild(string name);

    }

    public sealed class NoSender : ActorRef
    {
        public static readonly NoSender Instance = new NoSender();
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/NoSender");

        private NoSender() { }

        public override ActorPath Path { get { return _path; } }

        protected override void TellInternal(object message, ActorRef sender)
        {
        }
    }

    internal class VirtualPathContainer : MinimalActorRef
    {
        private readonly InternalActorRef _parent;
        private readonly LoggingAdapter _log;
        private readonly ActorRefProvider _provider;
        private readonly ActorPath _path;

        private readonly ConcurrentDictionary<string, InternalActorRef> _children = new ConcurrentDictionary<string, InternalActorRef>();

        public VirtualPathContainer(ActorRefProvider provider, ActorPath path, InternalActorRef parent, LoggingAdapter log)
        {
            _parent = parent;
            _log = log;
            _provider = provider;
            _path = path;
        }

        public override ActorRefProvider Provider
        {
            get { return _provider; }
        }

        public override InternalActorRef Parent
        {
            get { return _parent; }
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        public LoggingAdapter Log
        {
            get { return _log; }
        }


        protected bool TryGetChild(string name, out InternalActorRef child)
        {
            return _children.TryGetValue(name, out child);
        }

        public void AddChild(string name, InternalActorRef actor)
        {
            _children.AddOrUpdate(name, actor, (k, v) =>
            {
                //TODO:  log.debug("{} replacing child {} ({} -> {})", path, name, old, ref)
                return v;
            });
        }

        public void RemoveChild(string name)
        {
            InternalActorRef tmp;
            if (!_children.TryRemove(name, out tmp))
            {
                //TODO: log.warning("{} trying to remove non-child {}", path, name)
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

        public override ActorRef GetChild(IEnumerable<string> name)
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
            InternalActorRef child;
            if (_children.TryGetValue(firstName, out child))
                return child.GetChild(new Enumerable<string>(enumerator));
            return Nobody;
        }

        public void ForeachActorRef(Action<InternalActorRef> action)
        {
            foreach (InternalActorRef child in _children.Values)
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