//-----------------------------------------------------------------------
// <copyright file="FlowWithContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowWithContextSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowWithContextSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_FlowWithContext_must_get_created_from_FlowAsFlowWithContext()
        {
            var flow = Flow.Create<Message>().Select(m => m.Copy(data: m.Data + "z"));
            var flowWithContext = flow.AsFlowWithContext<Message, long, Message, long, NotUsed, Message>((m, o) => new Message(m.Data, o), m => m.Offset);

            Source.From(new[] { new Message("a", 1L) })
                .AsSourceWithContext(m => m.Offset)
                .Via(flowWithContext)
                .AsSource()
                .RunWith(this.SinkProbe<(Message, long)>(), Materializer)
                .Request(1)
                .ExpectNext((new Message("az", 1L), 1L))
                .ExpectComplete();
        }

        [Fact]
        public void A_FlowWithContext_must_be_able_to_map_materialized_value_via_FlowWithContext_MapMaterializedValue()
        {
            var materializedValue = "MatedValue";
            var mapMaterializedValueFlow = FlowWithContext.Create<Message, long>().MapMaterializedValue(_ => materializedValue);

            var (matValue, probe) = Source.From(new[] { new Message("a", 1L) })
                .MapMaterializedValue(_ => 42)
                .AsSourceWithContext(m => m.Offset)
                .ViaMaterialized(mapMaterializedValueFlow, Keep.Both)
                .ToMaterialized(this.SinkProbe<(Message, long)>(), Keep.Both)
                .Run(Materializer);

            matValue.ShouldBe((42, materializedValue));
            probe.Request(1).ExpectNext((new Message("a", 1L), 1L)).ExpectComplete();
        }
    }

    sealed class Message : IEquatable<Message>
    {
        public string Data { get; }
        public long Offset { get; }

        public Message(string data, long offset)
        {
            Data = data;
            Offset = offset;
        }

        public Message Copy(string data = null, long? offset = null) => new Message(data ?? Data, offset ?? Offset);

        public bool Equals(Message other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Data, other.Data) && Offset == other.Offset;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Message other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Data != null ? Data.GetHashCode() : 0) * 397) ^ Offset.GetHashCode();
            }
        }
    }
}
