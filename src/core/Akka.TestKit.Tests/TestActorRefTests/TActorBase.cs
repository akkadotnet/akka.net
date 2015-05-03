﻿//-----------------------------------------------------------------------
// <copyright file="TActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    // ReSharper disable once InconsistentNaming
    public abstract class TActorBase : ActorBase
    {
        protected sealed override bool Receive(object message)
        {
            var currentThread = Thread.CurrentThread;
            if(currentThread != TestActorRefSpec.Thread)
                TestActorRefSpec.OtherThread = currentThread;
            return ReceiveMessage(message);
        }

        protected abstract bool ReceiveMessage(object message);

        protected ActorSystem System
        {
            get { return ((LocalActorRef)Self).Cell.System; }
        }
    }
}

