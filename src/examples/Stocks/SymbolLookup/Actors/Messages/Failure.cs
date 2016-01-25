//-----------------------------------------------------------------------
// <copyright file="Failure.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace SymbolLookup.Actors.Messages
{
    public class Failure
    {
        public Failure(Exception ex, IActorRef actor)
        {
            Cause = ex;
            Child = actor;
        }

        public Exception Cause { get; private set; }

        public IActorRef Child { get; private set; }
    }
}

