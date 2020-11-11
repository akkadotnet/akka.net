//-----------------------------------------------------------------------
// <copyright file="MessageBufferSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{

    public class MessageBufferSpec : AkkaSpec
    {
        private sealed class DummyActorRef : MinimalActorRef
        {
            private readonly string id;

            public DummyActorRef(string id)
            {
                this.id = id;
            }

            public override IActorRefProvider Provider => throw new NotImplementedException();

            public override ActorPath Path => throw new NotImplementedException();

            public override string ToString()
            {
                return id;
            }
        }

        private IActorRef String2ActorRef(string s)
        {
            return new DummyActorRef(s); ;
        }

        [Fact]
        public void A_MessageBuffer_must_answer_empty_correctly()
        {
            var buffer = MessageBuffer.Empty();
            buffer.IsEmpty.Should().BeTrue();
            buffer.NonEmpty.Should().BeFalse();
            buffer.Append("m1", String2ActorRef("s1"));
            buffer.IsEmpty.Should().BeFalse();
            buffer.NonEmpty.Should().BeTrue();
        }

        [Fact]
        public void A_MessageBuffer_must_append_and_drop()
        {
            var buffer = MessageBuffer.Empty();
            buffer.Count.Should().Be(0);
            buffer.Append("m1", String2ActorRef("s1"));
            buffer.Count.Should().Be(1);
            buffer.Append("m2", String2ActorRef("s2"));
            buffer.Count.Should().Be(2);
            var (m1, s1) = buffer.Head;
            buffer.Count.Should().Be(2);
            buffer.DropHead();
            buffer.Count.Should().Be(1);
            m1.Should().Be("m1");
            s1.ToString().Should().Be("s1");
            var (m2, s2) = buffer.Head;
            buffer.DropHead();
            buffer.Count.Should().Be(0);
            buffer.DropHead();
            buffer.Count.Should().Be(0);
            m2.Should().Be("m2");
            s2.ToString().Should().Be("s2");
        }

        [Fact]
        public void A_MessageBuffer_must_process_elements_in_the_right_order()
        {
            var buffer = MessageBuffer.Empty();
            buffer.Append("m1", String2ActorRef("s1"));
            buffer.Append("m2", String2ActorRef("s2"));
            buffer.Append("m3", String2ActorRef("s3"));
            var sb1 = new StringBuilder();

            foreach (var i in buffer)
                sb1.Append($"{i.Message}->{i.Ref}:");
            sb1.ToString().Should().Be("m1->s1:m2->s2:m3->s3:");
            buffer.DropHead();
            var sb2 = new StringBuilder();
            foreach (var i in buffer)
                sb2.Append($"{i.Message}->{i.Ref}:");
            sb2.ToString().Should().Be("m2->s2:m3->s3:");
        }

        [Fact]
        public void AMessageBufferMap_must_support_contains_add_append_and_remove()
        {
            var map = new MessageBufferMap<String>();
            map.Contains("id1").Should().BeFalse();
            map.GetOrEmpty("id1").IsEmpty.Should().BeTrue();
            map.TotalCount.Should().Be(0);
            map.Add("id1");
            map.Contains("id1").Should().BeTrue();
            map.GetOrEmpty("id1").IsEmpty.Should().BeTrue();
            map.TotalCount.Should().Be(0);
            map.Append("id1", "m1", String2ActorRef("s1"));
            map.Contains("id1").Should().BeTrue();
            map.GetOrEmpty("id1").IsEmpty.Should().BeFalse();
            map.TotalCount.Should().Be(1);
            map.Remove("id1");
            map.Contains("id1").Should().BeFalse();
            map.GetOrEmpty("id1").IsEmpty.Should().BeTrue();
            map.TotalCount.Should().Be(0);
        }

        [Fact]
        public void AMessageBufferMap_must_handle_multiple_message_buffers()
        {
            var map = new MessageBufferMap<String>();
            map.Append("id1", "m11", String2ActorRef("s11"));
            map.Append("id1", "m12", String2ActorRef("s12"));
            map.Append("id2", "m21", String2ActorRef("s21"));
            map.Append("id2", "m22", String2ActorRef("s22"));
            map.TotalCount.Should().Be(4);
            var sb = new StringBuilder();
            foreach (var i in map.GetOrEmpty("id1"))
                sb.Append($"id1->{i.Message}->{i.Ref}:");
            foreach (var i in map.GetOrEmpty("id2"))
                sb.Append($"id2->{i.Message}->{i.Ref}:");
            sb.ToString().Should().Be("id1->m11->s11:id1->m12->s12:id2->m21->s21:id2->m22->s22:");
        }
    }
}

