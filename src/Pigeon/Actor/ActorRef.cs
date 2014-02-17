using Pigeon.Dispatch;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ActorRef
    {        
        public long UID { get; protected set; }
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

       

        public Task<object> Ask(object message)
        {
            var result = new TaskCompletionSource<object>();
            var future = ActorCell.Current.System.Provider.TempGuardian.Cell.ActorOf<FutureActor>();
            future.Tell(new SetRespondTo { Result = result }, ActorCell.Current.Self);
            Tell(message, future);
            return result.Task;
        }

        public Task<object> Ask(object message,ActorSystem system)
        {
            var result = new TaskCompletionSource<object>();
            var future = system.Provider.TempGuardian.Cell.ActorOf<FutureActor>();
            future.Tell(new SetRespondTo { Result = result });
            Tell(message, future);
            return result.Task;
        }        
    }



    public abstract class InternalActorRef : ActorRef
    {
        public abstract ActorRef GetChild(IEnumerable<string> name);

        public abstract void Resume(Exception causedByFailure = null);

        public abstract void Stop();

        public abstract void Restart(Exception cause);

        public abstract void Suspend();
    }

    public abstract class MinimalActorRef : InternalActorRef
    {

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
        private ConcurrentDictionary<string, InternalActorRef> children = new ConcurrentDictionary<string, InternalActorRef>();

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

        public void Remove(string name)
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

            var child = name.FirstOrDefault();
            if (child != null && child != "")
                return children[child] ?? this;

            return this;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            throw new NotImplementedException();
        }

        public override void Resume(Exception causedByFailure = null)
        {
            throw new NotImplementedException();
        }

        public override void Stop()
        {
            throw new NotImplementedException();
        }

        public void ForeachActorRef(Action<ActorRef> action)
        {
            foreach (var child in children.Values)
            {
                action(child);
            }
        }

        public override void Restart(Exception cause)
        {
            throw new NotImplementedException();
        }

        public override void Suspend()
        {
            throw new NotImplementedException();
        }
    }
}
