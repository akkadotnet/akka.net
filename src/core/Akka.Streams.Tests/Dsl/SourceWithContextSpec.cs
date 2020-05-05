//-----------------------------------------------------------------------
// <copyright file="SourceWithContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SourceWithContextSpec : AkkaSpec
    {
        sealed class Message : IEquatable<Message>
        {
            public string Data { get; }
            public long Offset { get; }

            public Message(string data, long offset)
            {
                Data = data;
                Offset = offset;
            }

            public bool Equals(Message other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return string.Equals(Data, other.Data) && Offset == other.Offset;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
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

        private ActorMaterializer Materializer { get; }

        public SourceWithContextSpec(ITestOutputHelper output) : base(output)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void SourceWithContext_must_get_created_from_AsSourceWithContext()
        {
            var msg = new Message("a", 1);

            var sink = this.CreateSubscriberProbe<(Message, long)>();

            Source.From(new[] { msg })
                .AsSourceWithContext(x => x.Offset)
                .AsSource()
                .RunWith(Sink.FromSubscriber(sink), Materializer);

            var sub = sink.ExpectSubscription();
            sub.Request(1);
            sink.ExpectNext((msg, 1L));
            sink.ExpectComplete();
        }

        [Fact]
        public void SourceWithContext_must_be_able_to_get_turned_back_into_a_normal_source()
        {
            var msg = new Message("a", 1);

            var sink = this.CreateSubscriberProbe<Message>();

            Source.From(new[] { msg })
                .AsSourceWithContext(x => x.Offset)
                .AsSource()
                .Select(t => t.Item1)
                .RunWith(Sink.FromSubscriber(sink), Materializer);

            var sub = sink.ExpectSubscription();
            sub.Request(1);
            sink.ExpectNext(msg);
            sink.ExpectComplete();
        }

        [Fact]
        public void SourceWithContext_must_pass_through_context_using_Select_and_Where()
        {
            var sink = this.CreateSubscriberProbe<(string, long)>();

            Source.From(new[]
                {
                    new Message("A", 1),
                    new Message("B", 2),
                    new Message("D", 3),
                    new Message("C", 4),
                })
                .AsSourceWithContext(x => x.Offset)
                .Select(m => m.Data.ToLower())
                .Where(x => x != "b")
                .WhereNot(x => x == "d")
                .AsSource()
                .RunWith(Sink.FromSubscriber(sink), Materializer);

            var sub = sink.ExpectSubscription();
            sub.Request(2);
            sink.ExpectNext(("a", 1L));
            sink.ExpectNext(("c", 4L));
            sink.ExpectComplete();
        }

        [Fact]
        public void SourceWithContext_must_pass_through_context_using_FlowWithContext()
        {
            var flowWithContext = FlowWithContext.Create<long, string>();

            var msg = new Message("a", 1);

            var sink = this.CreateSubscriberProbe<(string, long)>();

            Source.From(new[] { msg })
                .AsSourceWithContext(x => x.Offset)
                .Select(x => x.Data)
                .Via(flowWithContext.Select(s => s + "b"))
                .AsSource()
                .RunWith(Sink.FromSubscriber(sink), Materializer);

            var sub = sink.ExpectSubscription();
            sub.Request(1);
            sink.ExpectNext(("ab", 1L));
            sink.ExpectComplete();
        }

        [Fact]
        public void SourceWithContext_must_pass_through_context_via_SelectConcat()
        {
            var msg = new Message("a", 1);

            var sink = this.CreateSubscriberProbe<(string, long)>();

            Source.From(new[] { msg })
                .AsSourceWithContext(x => x.Offset)
                .Select(x => x.Data)
                .SelectConcat(str => new[] { 1, 2, 3 }.Select(i => $"{str}-{i}"))
                .AsSource()
                .RunWith(Sink.FromSubscriber(sink), Materializer);

            var sub = sink.ExpectSubscription();
            sub.Request(3);
            sink.ExpectNext(("a-1", 1L));
            sink.ExpectNext(("a-2", 1L));
            sink.ExpectNext(("a-3", 1L));
            sink.ExpectComplete();
        }

        [Fact]
        public void SourceWithContext_must_pass_through_sequence_of_context_per_element_via_Grouped()
        {
            var msg = new Message("a", 1);

            var sink = this.CreateSubscriberProbe<(IReadOnlyList<string>, IReadOnlyList<long>)>();

            Source.From(new[] { msg })
                .AsSourceWithContext(x => x.Offset)
                .Select(x => x.Data)
                .SelectConcat(str => new[] { 1, 2, 3, 4 }.Select(i => $"{str}-{i}"))
                .Grouped(2)
                .AsSource()
                .RunWith(Sink.FromSubscriber(sink), Materializer);

            var sub = sink.ExpectSubscription();
            sub.Request(3);
            var a = sink.ExpectNext();
            var b = sink.ExpectNext();

            a.Item1.ShouldBe(new[] { "a-1", "a-2" });
            a.Item2.ShouldBe(new[] { 1L, 1L });
            b.Item1.ShouldBe(new[] { "a-3", "a-4" });
            b.Item2.ShouldBe(new[] { 1L, 1L });

            sink.ExpectComplete();
        }
    }
}
