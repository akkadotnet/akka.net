using Pigeon.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;
using System.Collections.Concurrent;

namespace Pigeon.Actor
{
    public class ActorSystem : ActorContext, IDisposable
    {       
        public ActorSystem()
        {
            this.EventStream = ActorOf<EventStreamActor>("EventStream");
            this.DeadLetters = ActorOf<DeadLettersActor>("deadLetters");
            this.Guardian = ActorOf<GuardianActor>("user");
            this.SystemGuardian = ActorOf<GuardianActor>("system");
            this.TempGuardian = ActorOf<GuardianActor>("temp");
        }

        public LocalActorRef EventStream { get; private set; }
        public LocalActorRef DeadLetters { get; private set; }
        public LocalActorRef Guardian { get; private set; }
        public LocalActorRef SystemGuardian { get; private set; }
        public LocalActorRef TempGuardian { get; private set; }

        public void Shutdown()
        {
         //   Guardian.Stop();
        }

        public override ActorSystem System
        {
            get
            {
                return this;
            }
            //oh liskov <3
            set
            {
                throw new NotSupportedException("Can't set the system of a system");
            }
        }

        public void Dispose()
        {
        }
    }
}