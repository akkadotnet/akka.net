using Pigeon.Dispatch;
using System;
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

        public abstract void Resume(Exception causedByFailure = null);

        public abstract void Stop();

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

    public sealed class NoSender : ActorRef
    {
        public NoSender()
        {
            this.Path = null;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
        }

        public override void Resume(Exception causedByFailure = null)
        {
        }

        public override void Stop()
        {
        }
    }
}
