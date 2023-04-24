//-----------------------------------------------------------------------
// <copyright file="Subscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region SampleSubscriber
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;

namespace SampleSubscriber
{
    public sealed class Subscriber: ReceiveActor
    {
        private readonly IActorRef _mediator;
        private readonly ILoggingAdapter _log;
        public Subscriber()
        {
            _log = Context.GetLogger();
            _mediator = DistributedPubSub.Get(Context.System).Mediator;
            _mediator.Tell(new Subscribe("content", Self));
            Receive<SubscribeAck>(ack => 
            {
                if (ack != null && ack.Subscribe.Topic == "content" && ack.Subscribe.Ref.Equals(Self))
                {
                    Become(Ready);
                }
            });
        }
        private void Ready()
        {
            Receive<string>(message => _log.Info("Got {0}", message));
        }
    }
}
#endregion
