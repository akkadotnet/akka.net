using System;
using Pigeon.Actor;

namespace SymbolLookup.Actors.Messages
{
    public class Failure
    {
        public Failure(Exception ex, ActorRef actor)
        {
            Cause = ex;
            Child = actor;
        }

        public Exception Cause { get; private set; }

        public ActorRef Child { get; private set; }
    }
}
