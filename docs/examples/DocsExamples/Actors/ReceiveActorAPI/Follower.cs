using Akka.Actor;
using Akka.Event;
using System;
using System.Collections.Immutable;

namespace DocsExamples.Actor.ReceiveActorAPI
{
    public class Follower : ReceiveActor
    {
        private readonly IActorRef _probe;
        private string identifyId = "1";
        private IActorRef _another;

        public Follower(IActorRef probe)
        {
            _probe = probe;

            var selection = Context.ActorSelection("/user/another");
            selection.Tell(new Identify(identifyId), Self);

            Receive<ActorIdentity>(identity =>
            {
                if (identity.MessageId.Equals(identifyId))
                {
                    var subject = identity.Subject;

                    if (subject == null)
                    {
                        Context.Stop(Self);
                    }
                    else
                    {
                        _another = subject;
                        Context.Watch(_another);
                        _probe.Tell(subject, Self);
                    }
                }
            });

            Receive<Terminated>(t =>
            {
                if (t.ActorRef.Equals(_another))
                {
                    Context.Stop(Self);
                }
            });
        }
    }

}
