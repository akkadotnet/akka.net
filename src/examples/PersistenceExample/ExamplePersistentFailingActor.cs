using System;
using System.Collections.Generic;
using System.Linq;
using Akka;
using Akka.Persistence;

namespace PersistenceExample
{
    public class ExamplePersistentFailingActor : PersistentActorBase
    {
        public override string PersistenceId { get { return "sample-id-2"; } }

        public List<string> Received { get; set; }
        public override bool ReceiveRecover(object message)
        {
            if (message is string)
                Received = new[] {message.ToString()}.Union(Received).ToList();
            else return false;
            return true;
        }

        public override bool ReceiveCommand(object message)
        {
            if (message == "print")
                Console.WriteLine("Received " + string.Join("; ", Enumerable.Reverse(Received)));
            else if (message == "boom")
                throw new Exception("controlled demolition");
            else if (message is string)
                Persist(message.ToString(), s => Received = new[] { s }.Union(Received).ToList());
            else return false;
            return true;
        }
    }
}