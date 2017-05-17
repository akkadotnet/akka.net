//-----------------------------------------------------------------------
// <copyright file="TransformationFrontend.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;

namespace Samples.Cluster.Transformation
{
    public class TransformationFrontend : UntypedActor
    {
        protected List<IActorRef> Backends = new List<IActorRef>();
        protected int Jobs = 0;

        protected override void OnReceive(object message)
        {
            if (message is TransformationMessages.TransformationJob && Backends.Count == 0)
            {
                var job = (TransformationMessages.TransformationJob) message;
                Sender.Tell(new TransformationMessages.JobFailed("Service unavailable, try again later.", job), Sender);
            }
            else if (message is TransformationMessages.TransformationJob)
            {
                var job = (TransformationMessages.TransformationJob)message;
                Jobs++;
                Backends[Jobs % Backends.Count].Forward(job);
            }
            else if (message.Equals(TransformationMessages.BACKEND_REGISTRATION))
            {
                Context.Watch(Sender);
                Backends.Add(Sender);
            }
            else if (message is Terminated)
            {
                var terminated = (Terminated) message;
                Backends.Remove(terminated.ActorRef);
            }
            else
            {
                Unhandled(message);
            }
        }
    }
}

