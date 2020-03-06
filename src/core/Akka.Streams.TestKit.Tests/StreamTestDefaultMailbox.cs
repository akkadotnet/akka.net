//-----------------------------------------------------------------------
// <copyright file="StreamTestDefaultMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Util.Internal;

namespace Akka.Streams.TestKit.Tests
{
    /// <summary>
    /// INTERNAL API
    /// This mailbox is only used in tests to verify that stream actors are using
    /// the dispatcher defined in <see cref="ActorMaterializerSettings"/>
    /// </summary>
    [InternalApi]
    public sealed class StreamTestDefaultMailbox : MailboxType, IProducesMessageQueue<UnboundedMessageQueue>
    {

        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            if (owner is ActorRefWithCell)
            {
                var actorType = owner.AsInstanceOf<ActorRefWithCell>().Underlying.Props.Type;
                if (!typeof(ActorBase).IsAssignableFrom(actorType))
                    throw new ArgumentException(
                        $"Don't use anonymouse actor classes, actor class for {owner} was [{actorType.FullName}]");
                // StreamTcpManager is allowed to use another dispatcher
                if (actorType.FullName.StartsWith("Akka.Streams."))
                    throw new ArgumentException(
                        $"{owner} with actor class [{actorType.FullName}] must not run on default dispatcher in tests. Did you forget to define `props.withDispatcher` when creating the actor? Or did you forget to configure the `akka.stream.materializer` setting accordingly or force the dispatcher using `ActorMaterializerSettings(sys).withDispatcher(\"akka.test.stream-dispatcher\")` in the test?");
            }
            
            return new UnboundedMessageQueue();
        }

        public StreamTestDefaultMailbox() : this(null, null) { }

        public StreamTestDefaultMailbox(Settings settings, Config config) : base(settings, config)
        {
        }
    }
}
