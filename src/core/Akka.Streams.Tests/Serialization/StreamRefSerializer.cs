//-----------------------------------------------------------------------
// <copyright file="StreamRefSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Serialization;

public class StreamRefSerializer: Akka.TestKit.Xunit2.TestKit
{
    public StreamRefSerializer(ITestOutputHelper output) 
        : base(ActorMaterializer.DefaultConfig(), nameof(StreamRefSerializer), output)
    {
    }

    [Fact(DisplayName = "StreamRefSerializer should not throw NRE when configuration were set before ActorSystem started")]
    public void StreamsConfigBugTest()
    {
        var message = new SequencedOnNext(10, "test");
        var serializer = (SerializerWithStringManifest)Sys.Serialization.FindSerializerFor(message);
        var manifest = serializer.Manifest(message);

        byte[] bytes = null;
        Invoking(() =>
        {
            bytes = serializer.ToBinary(message); // This throws an NRE in the bug
        }).Should().NotThrow<NullReferenceException>();

        var deserialized = (SequencedOnNext) serializer.FromBinary(bytes, manifest);
        deserialized.SeqNr.Should().Be(message.SeqNr);
        deserialized.Payload.Should().Be(message.Payload);
    }
}
