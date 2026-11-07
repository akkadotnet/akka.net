// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe;

internal static class PubSubWireCases
{
    public static readonly Address Owner = new("akka.tcp", "pubsub", "127.0.0.1", 2552);

    public static (object Message, string Manifest, string Name)[] Create() =>
    [
        (new Status(ImmutableDictionary<Address, long>.Empty, false), "A", "StatusEmpty"),
        (new Status(ImmutableDictionary<Address, long>.Empty.Add(Owner, 17), true), "A", "StatusReply"),
        (new Delta(ImmutableList<Bucket>.Empty), "B", "DeltaEmpty"),
        (new Delta(ImmutableList.Create(new Bucket(Owner, 3,
            ImmutableDictionary<string, ValueHolder>.Empty.Add("/user/gone", new ValueHolder(7, null!))))), "B", "DeltaTombstone"),
        (new Send("/user/target", "hello", false), "C", "Send"),
        (new Send("/user/target", "hello", true), "C", "SendLocal"),
        (new SendToAll("/user/target", "hello", false), "D", "SendToAll"),
        (new SendToAll("/user/target", "hello", true), "D", "SendToAllExcludeSelf"),
        (new Publish("topic", "hello", false), "E", "Publish"),
        (new Publish("topic", "hello", true), "E", "PublishEachGroup"),
        (new SendToOneSubscriber("hello"), "F", "SendToOneSubscriber"),
        (new Send("/user/target", null!, true), "C", "SendNull"),
        (new SendToAll("/user/target", null!, true), "D", "SendToAllNull"),
        (new Publish("topic", null!, true), "E", "PublishNull"),
        (new SendToOneSubscriber(null!), "F", "SendToOneSubscriberNull")
    ];
}
