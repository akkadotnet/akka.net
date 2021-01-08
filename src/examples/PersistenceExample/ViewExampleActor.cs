//-----------------------------------------------------------------------
// <copyright file="ViewExampleActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Persistence;

namespace PersistenceExample
{
    public class ViewExampleActor : PersistentActor
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

