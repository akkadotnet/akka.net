//-----------------------------------------------------------------------
// <copyright file="DirectOutboundMessagePathSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Remote.Transport;
using Akka.TestKit;
using Google.Protobuf;
using Xunit;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Tests.Transport;

public class DirectOutboundMessagePathSpec : AkkaSpec
{
    private readonly AkkaPduProtobuffCodec _codec;
    private readonly Address _localAddress = new("akka.test", "testsystem", "localhost", 1234);

    public DirectOutboundMessagePathSpec(ITestOutputHelper output)
        : base("akka.actor.provider = remote", output)
    {
        _codec = new AkkaPduProtobuffCodec(Sys);
    }

    [Fact]
    public void Direct_message_payload_builder_should_preserve_current_wire_format_without_ack()
    {
        var serialized = new SerializedMessage
        {
            SerializerId = 17,
            Message = ByteString.CopyFromUtf8("hello"),
            MessageManifest = ByteString.CopyFromUtf8("System.String")
        };

        var current = _codec.ConstructPayload(_codec.ConstructMessage(_localAddress, TestActor, serialized, Sys.DeadLetters));
        var direct = _codec.ConstructMessagePayload(_localAddress, TestActor, serialized, Sys.DeadLetters);

        Assert.Equal(current, direct);
    }

}
