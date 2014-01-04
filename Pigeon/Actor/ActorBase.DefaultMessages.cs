using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract partial class ActorBase
    {
        private void OnReceiveInternal(object message)
        {
            try
            {
                Pattern.Match(message)
					//add watcher
                    .With<Watch>(Watch)
                    //complete the future callback by setting the result in this thread
                    .With<CompleteFuture>(CompleteFuture)
                    //resolve time distance to actor
                    .With<Ping>(Ping)
					//supervice exception from child actor
                    .With<SuperviceChild>(SuperviceChild)
                    //handle any other message
                    .Default(Default);
            }
            catch (Exception reason)
            {
                Context.Parent.Self.Tell(new SuperviceChild(reason));
            }
        }

		private void Default(object m)
        {
            Context.CurrentBehavior(m);
        }

		private void SuperviceChild(SuperviceChild m)
        {
            var whatToDo = this.SupervisorStrategyLazy().Handle(Sender, m.Reason);
            if (whatToDo == Directive.Escalate)
            {
				//rethrow exception in this actor
				//TODO: is this the right way to do it=
                throw m.Reason;
            }
            if (whatToDo == Directive.Resume)
            {
            }
            if (whatToDo == Directive.Restart)
            {
                Context.Restart((LocalActorRef)Sender);
            }
            if (whatToDo == Directive.Stop)
                Context.Stop((LocalActorRef)Sender);
        }

		private void Ping(Ping m)
        {
            Sender.Tell(
                        new Pong
                        {
                            LocalUtcNow = m.LocalUtcNow,
                            RemoteUtcNow = DateTime.UtcNow
                        });
        }

		private void Watch(Watch m)
        {
            Watchers.Add(Sender);
        }

		private void CompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}
