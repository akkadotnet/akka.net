//-----------------------------------------------------------------------
// <copyright file="WorkerActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WorkerActor : TActorBase
    {
        protected override bool ReceiveMessage(object message)
        {
            if((message as string) == "work")
            {
                Sender.Tell("workDone");
                Context.Stop(Self);
                return true;

            }
            //TODO: case replyTo: Promise[_] ⇒ replyTo.asInstanceOf[Promise[Any]].success("complexReply")
            if(message is IActorRef)
            {
                ((IActorRef)message).Tell("complexReply", Self);
                return true;
            }
            return false;
        }
    }
}

