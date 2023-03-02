//-----------------------------------------------------------------------
// <copyright file="ExamplePersistentFailingActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        public LinkedList<string> Received { get; }

        protected override bool ReceiveRecover(object message)
        {
            if (!(message is string str)) 
                return false;
            
            Received.AddFirst(str);
            return true;

        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case string str when str == "print":
                    Console.WriteLine("Received: " + string.Join(";, ", Enumerable.Reverse(Received)));
                    return true;
                case string str when str == "boom":
                    throw new Exception("controlled demolition");
                case string str:
                    Persist(str, s => Received.AddFirst(s));
                    return true;
                default:
                    return false;
            }
        }
    }
}

