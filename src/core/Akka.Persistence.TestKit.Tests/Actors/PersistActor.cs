// -----------------------------------------------------------------------
//  <copyright file="PersistActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using Actor;

    public class PersistActor : UntypedPersistentActor
    {
        public override string PersistenceId  => "foo";

        protected override void OnCommand(object message)
        {
            var sender = Sender;
            switch (message as string)
            {
                case "write":
                    Persist(message, _ =>
                    {
                        sender.Tell("ack");
                    });
                    
                    break;
                
                default:
                    return;
            }
        }

        protected override void OnRecover(object message)
        {
            // noop
        }

        protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
        {
            Sender.Tell("rejected");

            base.OnPersistRejected(cause, @event, sequenceNr);
        }
    }
}