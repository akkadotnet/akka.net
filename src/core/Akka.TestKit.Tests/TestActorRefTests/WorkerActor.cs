//-----------------------------------------------------------------------
// <copyright file="WorkerActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Util;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WorkerActor : TActorBase
    {
        public WorkerActor(Thread parentThread, AtomicReference<Thread> otherThread) : base(parentThread, otherThread)
        {
        }
        
        protected override bool ReceiveMessage(object message)
        {
            if((message as string) == "work")
            {
                Sender.Tell("workDone");
                Context.Stop(Self);
                return true;

            }
            //TODO: case replyTo: Promise[_] ⇒ replyTo.asInstanceOf[Promise[Any]].success("complexReply")
            if(message is IActorRef @ref)
            {
                @ref.Tell("complexReply", Self);
                return true;
            }
            return false;
        }
    }
}

