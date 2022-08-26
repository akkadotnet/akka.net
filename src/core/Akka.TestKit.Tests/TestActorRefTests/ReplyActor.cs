//-----------------------------------------------------------------------
// <copyright file="ReplyActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Util;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class ReplyActor : TActorBase
    {
        private IActorRef _replyTo;

        protected override bool ReceiveMessage(object message)
        {
            switch((string)message)
            {
                case "complexRequest":
                    _replyTo = Sender;
                    var worker = new TestActorRef<WorkerActor>(System, Props.Create(() => new WorkerActor(ParentThread, OtherThread)));
                    worker.Tell("work");
                    return true;
                case "complexRequest2":
                    var worker2 = new TestActorRef<WorkerActor>(System, Props.Create(() => new WorkerActor(ParentThread, OtherThread)));
                    worker2.Tell(Sender, Self);
                    return true;
                case "workDone":
                    _replyTo.Tell("complexReply", Self);
                    return true;
                case "simpleRequest":
                    Sender.Tell("simpleReply", Self);
                    return true;
            }
            return false;
        }

        public ReplyActor(Thread parentThread, AtomicReference<Thread> otherThread) : base(parentThread, otherThread)
        {
        }
    }
}

