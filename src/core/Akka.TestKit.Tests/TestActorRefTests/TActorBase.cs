//-----------------------------------------------------------------------
// <copyright file="TActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Util;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    // ReSharper disable once InconsistentNaming
    public abstract class TActorBase : ActorBase
    {
        protected readonly Thread ParentThread;
        protected readonly AtomicReference<Thread> OtherThread;

        protected TActorBase(Thread parentThread, AtomicReference<Thread> otherThread)
        {
            ParentThread = parentThread;
            OtherThread = otherThread;
        }

        protected sealed override bool Receive(object message)
        {
            var currentThread = Thread.CurrentThread;
            if (currentThread != ParentThread)
                OtherThread.GetAndSet(currentThread);
            return ReceiveMessage(message);
        }

        protected abstract bool ReceiveMessage(object message);

        protected ActorSystem System
        {
            get { return ((LocalActorRef)Self).Cell.System; }
        }
    }
}

