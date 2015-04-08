using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class ReceiveTimeoutActor : ActorBase
    {
        private readonly IActorRef _target;
        private CancellationTokenSource _cancellationTokenSource;

        public ReceiveTimeoutActor(IActorRef target)
        {
            _target = target;

            _cancellationTokenSource = new CancellationTokenSource();
            Task.Delay(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token).ContinueWith(t =>
            {
                if(t.IsCompleted) Self.Tell("tasktimeout");
            });
        }

        protected override bool Receive(object message)
        {
            _cancellationTokenSource.Cancel(false);
            var strMessage = message as string;
            if(strMessage == "tasktimeout")
            {
                _target.Tell("timeout", Self);
                Context.Stop(Self);
                return true;
            }
            return false;
        }
    }
}