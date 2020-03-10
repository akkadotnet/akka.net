//-----------------------------------------------------------------------
// <copyright file="ActorRefBackpressureSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class ActorRefBackpressureSinkSpec : AkkaSpec
    {

        #region internal classes

        private sealed class TriggerAckMessage
        {
            public static readonly TriggerAckMessage Instance = new TriggerAckMessage();
            private TriggerAckMessage() { }
        }

        private sealed class Fw : ReceiveActor
        {
            public Fw(IActorRef aref)
            {
                Receive<string>(s => s == InitMessage, s =>
                {
                    Sender.Tell(AckMessage);
                    aref.Forward(InitMessage);
                });
                Receive<string>(s => s == CompleteMessage, s => aref.Forward(CompleteMessage));
                Receive<int>(i =>
                {
                    Sender.Tell(AckMessage);
                    aref.Forward(i);
                });
            }
        }

        private sealed class Fw2 : ReceiveActor
        {
            public Fw2(IActorRef aref)
            {
                var actorRef = ActorRefs.NoSender;
                Receive<TriggerAckMessage>(_ => actorRef.Tell(AckMessage));
                ReceiveAny(msg =>
                {
                    actorRef = Sender;
                    aref.Forward(msg);
                });
            }
        }

        #endregion

        private const string InitMessage = "start";
        private const string CompleteMessage = "complete";
        private const string AckMessage = "ack";

        private ActorMaterializer Materializer { get; }

        public ActorRefBackpressureSinkSpec(ITestOutputHelper output) : base(output, ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf"))
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private IActorRef CreateActor<T>() => Sys.ActorOf(Props.Create(typeof(T), TestActor).WithDispatcher("akka.test.stream-dispatcher"));

        [Fact]
        public void ActorBackpressureSink_should_send_the_elements_to_the_ActorRef()
        {
            this.AssertAllStagesStopped(() =>
            {
                var fw = CreateActor<Fw>();
                Source.From(Enumerable.Range(1, 3))
                    .RunWith(Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage), Materializer);
                ExpectMsg("start");
                ExpectMsg(1);
                ExpectMsg(2);
                ExpectMsg(3);
                ExpectMsg(CompleteMessage);
            }, Materializer);
        }

        [Fact]
        public void ActorBackpressureSink_should_send_the_elements_to_the_ActorRef2()
        {
            this.AssertAllStagesStopped(() =>
            {
                var fw = CreateActor<Fw>();
                var probe =
                    this.SourceProbe<int>()
                        .To(Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage))
                        .Run(Materializer);
                probe.SendNext(1);
                ExpectMsg("start");
                ExpectMsg(1);
                probe.SendNext(2);
                ExpectMsg(2);
                probe.SendNext(3);
                ExpectMsg(3);
                probe.SendComplete();
                ExpectMsg(CompleteMessage);
            }, Materializer);
        }

        [Fact]
        public void ActorBackpressureSink_should_cancel_stream_when_actor_terminates()
        {
            this.AssertAllStagesStopped(() =>
            {
                var fw = CreateActor<Fw>();
                var publisher =
                    this.SourceProbe<int>()
                        .To(Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage))
                        .Run(Materializer);
                publisher.SendNext(1);
                ExpectMsg(InitMessage);
                ExpectMsg(1);
                Sys.Stop(fw);
                publisher.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ActorBackpressureSink_should_send_message_only_when_backpressure_received()
        {
            this.AssertAllStagesStopped(() =>
            {
                var fw = CreateActor<Fw2>();
                var publisher = this.SourceProbe<int>()
                        .To(Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage))
                        .Run(Materializer);
                ExpectMsg(InitMessage);
                publisher.SendNext(1);
                ExpectNoMsg();
                fw.Tell(TriggerAckMessage.Instance);
                ExpectMsg(1);

                publisher.SendNext(2);
                publisher.SendNext(3);
                publisher.SendComplete();
                fw.Tell(TriggerAckMessage.Instance);
                ExpectMsg(2);
                fw.Tell(TriggerAckMessage.Instance);
                ExpectMsg(3);

                ExpectMsg(CompleteMessage);
            }, Materializer);
        }

        [Fact]
        public void ActorBackpressureSink_should_keep_on_sending_even_after_the_buffer_has_been_full()
        {
            this.AssertAllStagesStopped(() =>
            {
                var bufferSize = 16;
                var streamElementCount = bufferSize + 4;
                var fw = CreateActor<Fw2>();
                var sink =
                    Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage)
                        .WithAttributes(Attributes.CreateInputBuffer(bufferSize, bufferSize));
                var probe =
                    Source.From(Enumerable.Range(1, streamElementCount))
                        .AlsoToMaterialized(
                            Flow.Create<int>().Take(bufferSize).WatchTermination(Keep.Right).To(Sink.Ignore<int>()),
                            Keep.Right)
                        .To(sink)
                        .Run(Materializer);
                probe.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                probe.IsCompleted.Should().BeTrue();
                ExpectMsg(InitMessage);
                fw.Tell(TriggerAckMessage.Instance);
                for (var i = 1; i <= streamElementCount; i++)
                {
                    ExpectMsg(i);
                    fw.Tell(TriggerAckMessage.Instance);
                }
                ExpectMsg(CompleteMessage);
            }, Materializer);
        }

        [Fact]
        public void ActorBackpressureSink_should_work_with_one_element_buffer()
        {
            this.AssertAllStagesStopped(() =>
            {
                var fw = CreateActor<Fw2>();
                var publisher =
                    this.SourceProbe<int>()
                        .To(Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage))
                        .WithAttributes(Attributes.CreateInputBuffer(1, 1)).Run(Materializer);

                ExpectMsg(InitMessage);
                fw.Tell(TriggerAckMessage.Instance);

                publisher.SendNext(1);
                ExpectMsg(1);

                fw.Tell(TriggerAckMessage.Instance);
                ExpectNoMsg(); // Ack received but buffer empty

                publisher.SendNext(2); // Buffer this value
                fw.Tell(TriggerAckMessage.Instance);
                ExpectMsg(2);

                publisher.SendComplete();
                ExpectMsg(CompleteMessage);
            }, Materializer);
        }

        [Fact]
        public void ActorBackpressurSink_should_fail_to_materialize_with_zero_sized_input_buffer()
        {
            var fw = CreateActor<Fw>();
            var badSink =
                Sink.ActorRefWithAck<int>(fw, InitMessage, AckMessage, CompleteMessage)
                    .WithAttributes(Attributes.CreateInputBuffer(0, 0));
            Source.Single(1).Invoking(s => s.RunWith(badSink, Materializer)).ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void ActorBackpressurSink_should_signal_failure_on_abrupt_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = CreateTestProbe();
            var sink = Sink.ActorRefWithAck<string>(probe.Ref, InitMessage, AckMessage, CompleteMessage)
                .WithAttributes(Attributes.CreateInputBuffer(1, 1));

            Source.Maybe<string>().To(sink).Run(materializer);

            probe.ExpectMsg(InitMessage);
            materializer.Shutdown();
            probe.ExpectMsg<Status.Failure>();
        }
    }
}
