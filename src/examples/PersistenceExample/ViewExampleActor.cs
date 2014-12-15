using System;
using Akka.Persistence;

namespace PersistenceExample
{
    public class ViewExampleActor : PersistentActorBase
    {
        private int _count = 1;

        public ViewExampleActor()
        {
        }

        public override string PersistenceId { get { return "sample-id-4"; } }

        protected override bool ReceiveRecover(object message)
        {
            _count++;
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message is string)
            {
                Console.WriteLine("PersistentActor received {0} (nr = {1})", message, _count);
                Persist(message.ToString() + _count, s => _count++);
            }

            return false;
        }
    }
}