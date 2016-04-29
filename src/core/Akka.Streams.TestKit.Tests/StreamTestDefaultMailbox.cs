//-----------------------------------------------------------------------
// <copyright file="StreamTestDefaultMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

//-----------------------------------------------------------------------
// <copyright file="StreamTestDefaultMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.TestKit;

namespace Akka.Streams.TestKit.Tests
{
    /// <summary>
    /// INTERNAL API
    /// This mailbox is only used in tests to verify that stream actors are using
    /// the dispatcher defined in <see cref="ActorMaterializerSettings"/>
    /// </summary>
    public sealed class StreamTestDefaultMailbox : UnboundedMailbox
    {
        public override void SetActor(ActorCell actorCell)
        {
            if (actorCell.Self is ActorRefWithCell)
            {
                var actorType = actorCell.Props.Type;
                if (!typeof (ActorBase).IsAssignableFrom(actorType))
                    throw new ArgumentException(
                        string.Format("Don't use anonymouse actor classes, actor class for {0} was [{1}]", actorCell,
                            actorType.FullName));
                // StreamTcpManager is allowed to use another dispatcher
                if (actorType.FullName.StartsWith("Akka.Streams."))
                    throw new ArgumentException(
                        string.Format(
                            "{0} with actor class [{1}] must not run on default dispatcher in tests. Did you forget to define `props.withDispatcher` when creating the actor? Or did you forget to configure the `akka.stream.materializer` setting accordingly or force the dispatcher using `ActorMaterializerSettings(sys).withDispatcher(\"akka.test.stream-dispatcher\")` in the test?",
                            actorCell, actorType.FullName));
            }

            base.SetActor(actorCell);
        }
    }
}