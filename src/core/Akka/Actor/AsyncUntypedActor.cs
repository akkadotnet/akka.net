using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public abstract class AsyncUntypedActor : UntypedActor, WithUnboundedStash
    {
        public IStash Stash { get; set; }
        bool _awaiting;
        readonly object AwaitComplete = new object();

        protected sealed override void OnReceive(object message)
        {
            if (_awaiting)
            {
                if (message == AwaitComplete)
                {
                    _awaiting = false;
                    Stash.UnstashAll();
                    return;
                }

                if (message is ExceptionDispatchInfo && Sender.Equals(Self))
                {
                    _awaiting = false;
                    Stash.UnstashAll();
                    ((ExceptionDispatchInfo)message).Throw();
                }

                Stash.Stash();
                return;
            }

            var task = OnReceiveAsync(message);

            if (task.IsFaulted)
                ExceptionDispatchInfo.Capture(task.Exception.InnerException).Throw();

            if (task.IsCompleted)
                return;

            var self = Self;
            _awaiting = true;

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    self.Tell(ExceptionDispatchInfo.Capture(t.Exception.InnerException), self);
                else
                    self.Tell(AwaitComplete, ActorRef.NoSender);
            });
        }

        protected abstract Task OnReceiveAsync(object message);
    }
}
