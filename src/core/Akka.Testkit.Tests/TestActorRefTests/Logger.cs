using Akka.Actor;
using Akka.Event;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class Logger : ActorBase
    {
        private int _count;
        private string _msg;
        protected override bool Receive(object message)
        {
            var warning = message as Warning;
            if(warning != null && warning.Message is string)
            {
                _count++;
                _msg = (string)warning.Message;
                return true;
            }
            return false;
        }
    }
}