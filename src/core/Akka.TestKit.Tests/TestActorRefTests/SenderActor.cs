//-----------------------------------------------------------------------
// <copyright file="SenderActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class SenderActor : TActorBase
    {
        private readonly AtomicCounter _counter;
        private readonly IActorRef _replyActor;

        public SenderActor(IActorRef replyActor, AtomicCounter counter, Thread parentThread, AtomicReference<Thread> otherThread) : base(parentThread, otherThread)
        {
            _replyActor = replyActor;
            _counter = counter;
        }

        protected override bool ReceiveMessage(object message)
        {
            var strMessage = message as string;
            switch(strMessage)
            {
                case "complex":
                    _replyActor.Tell("complexRequest", Self);
                    return true;
                case "complex2":
                    _replyActor.Tell("complexRequest2", Self);
                    return true;
                case "simple":
                    _replyActor.Tell("simpleRequest", Self);
                    return true;
                case "complexReply":
                    _counter.Decrement();
                    return true;
                case "simpleReply":
                    _counter.Decrement();
                    return true;
            }
            return false;
        }
    }
}

