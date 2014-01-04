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
                    .Default(m => Context.CurrentBehavior(m));
            }
            catch (Exception reason)
            {
                Context.Parent.Self.Tell(new SuperviceChild(reason));
            }
        }

		private void SuperviceChild(SuperviceChild m)
        {
            this.SupervisorStrategyLazy().Handle(Sender, m.Reason);
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
