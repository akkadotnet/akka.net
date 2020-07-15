//-----------------------------------------------------------------------
// <copyright file="Follower.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using System;
using System.Collections.Immutable;

namespace DocsExamples.Actor.UntypedActorAPI
{
    #region UntypedActor
    public class Follower : UntypedActor
    {
        private string identifyId = "1";

        public Follower()
        {
            Context.ActorSelection("/user/another").Tell(new Identify(identifyId));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ActorIdentity a when a.MessageId.Equals(identifyId) && a.Subject != null:
                    Context.Watch(a.Subject);
                    Context.Become(Active(a.Subject));
                    break;
                case ActorIdentity a when a.MessageId.Equals(identifyId) && a.Subject == null:
                    Context.Stop(Self);
                    break;
            }
        }

        public UntypedReceive Active(IActorRef another)
        {
            return (message) =>
            {
                if (message is Terminated t && t.ActorRef.Equals(another))
                {
                    Context.Stop(Self);
                }
            };
        }
    }
    #endregion
}
