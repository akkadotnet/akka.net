//-----------------------------------------------------------------------
// <copyright file="HeadlessActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region headless-actor

using Akka.Actor;

namespace AkkaHeadlesssService
{
    internal class HeadlessActor : ReceiveActor
    {
        // Things that are possible with this actor:
        // Connect to an Apache Kafka, Apache Pulsar, RabbitMQ instance or send/receive messages to/from a remote actor!
        public HeadlessActor()
        {

        }
        public static Props Prop()
        {
            return Props.Create<HeadlessActor>();
        }
    }
}
#endregion
