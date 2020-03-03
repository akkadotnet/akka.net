//-----------------------------------------------------------------------
// <copyright file="TransformationFrontend.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            switch (message)
            {
                case TransformationMessages.TransformationJob job1 when Backends.Count == 0:
                    Sender.Tell(new TransformationMessages.JobFailed("Service unavailable, try again later.", job1), Sender);
                    break;
                case TransformationMessages.TransformationJob job:
                    Jobs++;
                    Backends[Jobs % Backends.Count].Forward(job);
                    break;
                default:
                {
                    if (message.Equals(TransformationMessages.BACKEND_REGISTRATION))
                    {
                        Context.Watch(Sender);
                        Backends.Add(Sender);
                    }
                    else if (message is Terminated terminated)
                    {
                        Backends.Remove(terminated.ActorRef);
                    }
                    else
                    {
                        Unhandled(message);
                    }

                    break;
                }
            }
        }
    }
}

