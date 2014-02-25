using Pigeon.Dispatch;
using Pigeon.Dispatch.SysMsg;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class FutureActorRef : MinimalActorRef
    {
        private TaskCompletionSource<object> result;
        private ActorRef sender;
        private Action unregister;
        public FutureActorRef(TaskCompletionSource<object> result, ActorRef sender, Action unregister,ActorPath path,TimeSpan? timeout)
        {
            this.result = result;
            this.sender = sender;
            this.unregister = unregister;
            this.Path = path;
            if (timeout != null)
            {
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(timeout.Value);
                    Timeout();
                });
              //  Task.Delay(timeout.Value).ContinueWith(t => timeout);
            }
        }

        private int status = 0;
        private void Timeout()
        {
            if (Interlocked.Exchange(ref status, 1)==0)
            {
                unregister();
                if (sender != null && sender != ActorRef.NoSender)
                {
                    sender.Tell(new CompleteFuture(() => result.TrySetCanceled()));
                }
                else
                {
                    result.TrySetCanceled();
                }
            }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            if (Interlocked.Exchange(ref status, 2) == 0)
            {
                unregister();
                if (sender != ActorRef.NoSender)
                {
                    sender.Tell(new CompleteFuture(() => result.SetResult(message)));
                }
                else
                {
                    result.SetResult(message);
                }
            }
        }

        public override ActorRefProvider Provider
        {
            get { throw new NotImplementedException(); }
        }
    }

    public abstract class ActorRef : ICanTell
    {
        public static readonly Nobody Nobody = new Nobody();
        public static readonly ReservedActorRef Reserved = new ReservedActorRef();

        public virtual ActorPath Path { get;protected set; }

        public void Tell(object message, ActorRef sender)
        {
            if (sender == null)
            {
                throw new ArgumentNullException("sender");
            }

            TellInternal(message, sender);
        }

        public void Tell(object message)
        {
            ActorRef sender = null;

            if (ActorCell.Current != null)
            {
                sender = ActorCell.Current.Self;
            }
            else
            {
                sender = ActorRef.NoSender;
            }

            this.Tell(message,sender);
        }

        protected abstract void TellInternal(object message,ActorRef sender);     

        public static readonly ActorRef NoSender = new NoSender();
      
        public override string ToString()
        {
            return string.Format("[{0}]", this.Path.ToString());
        }
    }



    public abstract class InternalActorRef : ActorRef
    {
        public abstract ActorRef GetChild(IEnumerable<string> name);

        public abstract void Resume(Exception causedByFailure = null);

        public abstract void Stop();

        public abstract void Restart(Exception cause);

        public abstract void Suspend();

        public abstract InternalActorRef Parent { get; }

        public abstract ActorRefProvider Provider { get; }
    }

    public abstract class MinimalActorRef : InternalActorRef
    {

        public override ActorRef GetChild(IEnumerable<string> name)
        {
 	        if (name.All(n => string.IsNullOrEmpty(n)))
                return this;
            else
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

        public override InternalActorRef Parent
        {
            get { return Nobody; }
        }        
    }

    public sealed class Nobody : MinimalActorRef
    {
        public override ActorRefProvider Provider
        {
            get { throw new NotSupportedException("Nobody does not provide"); }
        }
    }

    public sealed class ReservedActorRef : MinimalActorRef
    {
        public override ActorRefProvider Provider
        {
            get { throw new NotSupportedException("Nobody does not provide"); }
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
        public NoSender()
        {
            this.Path = null;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
        }       
    }

    public class VirtualPathContainer : MinimalActorRef
    {
        private InternalActorRef parent;
        public VirtualPathContainer(ActorRefProvider provider,ActorPath path,InternalActorRef parent)
        {            
            this.Path = path;
            this.parent = parent;
            this.provider = provider;
        }

        public override ActorRefProvider Provider
        {
            get { return provider; }
        }

        public override InternalActorRef Parent
        {
            get { return parent; }
        }

        protected ConcurrentDictionary<string, InternalActorRef> children = new ConcurrentDictionary<string, InternalActorRef>();
        private ActorRefProvider provider;

        private InternalActorRef GetChild(string name)
        {
            return children[name];
        }

        public void AddChild(string name,InternalActorRef actor)
        {
            children.AddOrUpdate(name, actor, (k,v) =>
            {
                //TODO:  log.debug("{} replacing child {} ({} -> {})", path, name, old, ref)
                return v;
            });
        }

        public void RemoveChild(string name)
        {
            InternalActorRef tmp;
            if (!children.TryRemove(name,out tmp))
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
            //TODO: I have no clue what the scala version does
            if (!name.Any())
                return this;
            
            var n = name.First();
            if (string.IsNullOrEmpty(n))
                return this;
            else
            {
                var c = children[n];
                if (c == null)
                    return Nobody;
                else
                    return c.GetChild(name.Skip(1));
            }            
        }

        public void ForeachActorRef(Action<ActorRef> action)
        {
            foreach (var child in children.Values)
            {
                action(child);
            }
        }
    }
}
