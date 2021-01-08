//-----------------------------------------------------------------------
// <copyright file="ExamplePersistentFailingActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Persistence;

namespace PersistenceExample
{
    public class ExamplePersistentFailingActor : PersistentActor
    {
        public ExamplePersistentFailingActor()
        {
            Received = new LinkedList<string>();
        }

        public override string PersistenceId { get { return "sample-id-2"; } }
        public LinkedList<string> Received { get; private set; }

        protected override bool ReceiveRecover(object message)
        {
            if (message is string)
                Received.AddFirst(message.ToString());
            else return false;
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message as string == "print")
                Console.WriteLine("Received: " + string.Join(";, ", Enumerable.Reverse(Received)));
            else if (message as string == "boom")
                throw new Exception("controlled demolition");
            else if (message is string)
                Persist(message.ToString(), s => Received.AddFirst(s));
            else return false;
            return true;
        }
    }
}

