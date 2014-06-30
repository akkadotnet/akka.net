using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Dispatch.SysMsg;
using System.Threading;

namespace Akka.Actor
{
    
    /// <summary>
    /// All ActorRefs have a scope which describes where they live. Since it is often
    /// necessary to distinguish between local and non-local references, this is the only
    /// method provided on the scope.
    /// </summary>
// ReSharper disable once InconsistentNaming
    internal interface ActorRefScope
    {
        bool IsLocal { get; }
    }

    /// <summary>
    /// Marker interface for Actors that are deployed within local scope
    /// </summary>
// ReSharper disable once InconsistentNaming
    internal interface LocalRef : ActorRefScope { }

    public class FutureActorRef : MinimalActorRef
    {
        private readonly TaskCompletionSource<object> _result;
        private readonly Action _unregister;
        private readonly ActorPath _path;
        private ActorRef _sender;

        public FutureActorRef(TaskCompletionSource<object> result, ActorRef sender, Action unregister, ActorPath path)
        {
            _result = result;
            _sender = sender ?? ActorRef.NoSender;
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
        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message is SystemMessage) //we have special handling for system messages
            {
                SendSystemMessage(message.AsInstanceOf<SystemMessage>(), sender);
            }
            else
            {
                if (Interlocked.Exchange(ref status,COMPLETED) == INITIATED)
                {
                    _unregister();
                    if (_sender == NoSender || message is Terminated)
                    {
                        _result.TrySetResult(message);
                    }
                    else
                    {
                        _sender.Tell(new CompleteFuture(() => _result.TrySetResult(message)));
                    }
                }
            }            
        }

        protected void SendSystemMessage(SystemMessage message, ActorRef sender)
        {
            PatternMatch.Match(message)
                .With<Terminate>(t => Stop())
                .With<DeathWatchNotification>(d => Tell(new Terminated(d.Actor, d.ExistenceConfirmed, d.AddressTerminated)));
        }
    }

    public class ActorRefSurrogate
    {
        public ActorRefSurrogate(string path)
        {
            Path = path;
        }

        public string Path { get; private set; }
    }

    internal static class ActorRefSender
    {
        public static ActorRef GetSelfOrNoSender()
        {
            var actorCell = ActorCell.Current;
            return actorCell != null ? (ActorRef)actorCell.Self : ActorRef.NoSender;
        }
    }

    public abstract class ActorRef : ICanTell
    {

        public static readonly Nobody Nobody = Nobody.Instance;
        public static readonly ReservedActorRef Reserved = ReservedActorRef.Instance;
        public static readonly ActorRef NoSender = Actor.NoSender.Instance; //In Akka this is just null

        public abstract ActorPath Path { get; }

        public void Tell(object message, ActorRef sender)
        {
            if(sender == null)
            {
                throw new ArgumentNullException("sender");
            }

            TellInternal(message, sender);
        }

        public void Tell(object message)
        {
            ActorRef sender = null;

            if(ActorCell.Current != null)
            {
                sender = ActorCell.Current.Self;
            }
            else
            {
                sender = NoSender;
            }

            Tell(message, sender);
        }


        protected abstract void TellInternal(object message, ActorRef sender);

        public override string ToString()
        {
            return string.Format("[{0}]", Path);
        }

        public static implicit operator ActorRefSurrogate(ActorRef @ref)
        {
            if(@ref != null)
            {
                return new ActorRefSurrogate(Serialization.Serialization.SerializedActorPath(@ref));
            }

            return null;
        }

        public static implicit operator ActorRef(ActorRefSurrogate surrogate)
        {
            return Serialization.Serialization.CurrentSystem.Provider.ResolveActorRef(surrogate.Path);
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
        public abstract ActorRef GetChild(IEnumerable<string> name);

        public abstract void Resume(Exception causedByFailure = null);
        public abstract void Stop();
        public abstract void Restart(Exception cause);
        public abstract void Suspend();

        public bool IsTerminated { get; internal set; }
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
            if(name.All(string.IsNullOrEmpty))
                return this;
            return Nobody;
        }

        public override void Resume(Exception causedByFailure = null)
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

    public sealed class ReservedActorRef : MinimalActorRef      //TODO: This isn't really an ActorRef. When ActorCell uses a better ChildrensCollection we can remove this
    {
        public static ReservedActorRef Instance = new ReservedActorRef();
        public override ActorPath Path { get { throw new NotSupportedException(); } }
        private ReservedActorRef() { }

        public override ActorRefProvider Provider
        {
            get { throw new NotSupportedException("Reserved does not provide"); }
        }
    }

    public abstract class ActorRefWithCell : InternalActorRef
    {
        public ActorCell Cell { get; protected set; }

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

    public class VirtualPathContainer : MinimalActorRef
    {
        private readonly InternalActorRef _parent;
        private readonly ActorRefProvider _provider;
        private readonly ActorPath _path;

        private readonly ConcurrentDictionary<string, InternalActorRef> _children = new ConcurrentDictionary<string, InternalActorRef>();

        public VirtualPathContainer(ActorRefProvider provider, ActorPath path, InternalActorRef parent)
        {
            _parent = parent;
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
            if(!_children.TryRemove(name, out tmp))
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
            if(!enumerator.MoveNext())
            {
                //name was empty
                return this;
            }
            var firstName = enumerator.Current;
            if(string.IsNullOrEmpty(firstName))
                return this;
            InternalActorRef child;
            if(_children.TryGetValue(firstName, out child))
                return child.GetChild(new Enumerable<string>(enumerator));
            return Nobody;
        }

        public void ForeachActorRef(Action<ActorRef> action)
        {
            foreach(InternalActorRef child in _children.Values)
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