using System.Threading;
using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    // ReSharper disable once InconsistentNaming
    public abstract class TActorBase : ActorBase
    {
        protected sealed override bool Receive(object message)
        {
            var currentThread = Thread.CurrentThread;
            if(currentThread != TestActorRefSpec.Thread)
                TestActorRefSpec.OtherThread = currentThread;
            return ReceiveMessage(message);
        }

        protected abstract bool ReceiveMessage(object message);

        protected ActorSystem System
        {
            get { return ((LocalActorRef)Self).Cell.System; }
        }
    }
}