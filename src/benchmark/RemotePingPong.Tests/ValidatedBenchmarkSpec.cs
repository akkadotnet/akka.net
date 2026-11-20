// -----------------------------------------------------------------------
// <copyright file="ValidatedBenchmarkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using static RemotePingPong.ValidatedBenchmark;

namespace RemotePingPong.Tests
{
    public sealed class ValidatedBenchmarkSpec : Akka.TestKit.Xunit.TestKit
    {
        public ValidatedBenchmarkSpec(ITestOutputHelper output) : base(output: output) { }

        private static Agreement Settings(int messages = 3, int window = 2, bool oneWay = true) =>
            new(Guid.NewGuid(), messages, window, Math.Max(1, window / 2), oneWay, 352);

        [Theory(DisplayName = "Validated delivery exceeds credit, confirms partial batches and bounds small runs in both directions")]
        [InlineData(true, 8193, 10)]
        [InlineData(false, 8193, 10)]
        [InlineData(true, 7, 1)]
        [InlineData(false, 7, 1)]
        [InlineData(true, 3, 10)]
        [InlineData(false, 3, 10)]
        public async Task Should_ConfirmUniqueDelivery_When_CountAndCreditDiffer(bool oneWay, int messages, int window)
        {
            var settings = Settings(messages, window, oneWay);
            var payloads = PreparePayloads(messages);
            var completion = new Outcome();
            var receiver = Sys.ActorOf(Props.Create(() => new Receiver(settings)));
            var sender = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, receiver, completion)));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.True(await sender.Ask<bool>(new Prepare(), deadline.Token));
            sender.Tell(new Messages.Run());
            Assert.Equal(messages, await completion.Task.WaitAsync(deadline.Token));
        }

        [Theory(DisplayName = "Receiver rejects duplicate, skipped, reordered, corrupted and excess data before the final fence")]
        [InlineData("duplicate")]
        [InlineData("skipped")]
        [InlineData("reordered")]
        [InlineData("content")]
        [InlineData("extra")]
        public async Task Should_RejectData_When_SequenceOrContentIsInvalid(string fault)
        {
            var settings = Settings();
            var receiver = Sys.ActorOf(Props.Create(() => new Receiver(settings)));
            var payloads = PreparePayloads(4);
            receiver.Tell(settings, TestActor);
            Assert.Equal(settings, (await ExpectMsgAsync<Ready>()).Settings);
            if (fault == "extra")
            {
                for (var i = 0; i < 3; i++)
                {
                    receiver.Tell(payloads[i], TestActor);
                    Assert.Equal(i + 1, (await ExpectMsgAsync<Credit>()).Received);
                }
                receiver.Tell(payloads[3], TestActor);
            }
            else if (fault == "duplicate")
            {
                receiver.Tell(payloads[0], TestActor);
                await ExpectMsgAsync<Credit>();
                receiver.Tell(payloads[0], TestActor);
            }
            else
            {
                var bad = fault == "content" ? payloads[0] with { Value = -1 }
                    : payloads[fault == "skipped" ? 1 : 2];
                receiver.Tell(bad, TestActor);
            }
            await ExpectMsgAsync<Failed>();
        }

        [Theory(DisplayName = "Receiver rejects foreign phase, parameter mismatch, early fence and post-fence data")]
        [InlineData("phase")]
        [InlineData("parameters")]
        [InlineData("early-fence")]
        [InlineData("after-fence")]
        public async Task Should_RejectPhase_When_AgreementOrFenceIsInvalid(string fault)
        {
            var settings = Settings(1);
            var receiver = Sys.ActorOf(Props.Create(() => new Receiver(settings)));
            receiver.Tell(fault == "parameters" ? settings with { Window = 3 } : settings, TestActor);
            if (fault == "parameters")
            {
                await ExpectMsgAsync<Failed>();
                return;
            }
            await ExpectMsgAsync<Ready>();
            if (fault == "after-fence")
            {
                receiver.Tell(PreparePayloads(1)[0], TestActor);
                await ExpectMsgAsync<Credit>();
                receiver.Tell(new Fence(settings.RunId), TestActor);
                Assert.Equal(1, (await ExpectMsgAsync<Receipt>()).Received);
                receiver.Tell(PreparePayloads(1)[0], TestActor);
            }
            else
                receiver.Tell(new Fence(fault == "phase" ? Guid.NewGuid() : settings.RunId), TestActor);
            await ExpectMsgAsync<Failed>();
        }

        [Theory(DisplayName = "Sender rejects forged ACKs, invalid echoes, early receipts and stalled delivery")]
        [InlineData("credit")]
        [InlineData("foreign-credit")]
        [InlineData("echo")]
        [InlineData("receipt")]
        [InlineData("timeout")]
        public async Task Should_FailCompletion_When_ProgressIsInvalid(string fault)
        {
            var probe = CreateTestProbe();
            var settings = Settings(oneWay: fault != "echo");
            var payloads = PreparePayloads(settings.Messages);
            var completion = new Outcome();
            var sender = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, probe.Ref, completion)));
            sender.Tell(new Prepare(), TestActor);
            Assert.Equal(settings, await probe.ExpectMsgAsync<Agreement>());
            probe.Send(sender, new Ready(settings));
            Assert.True(await ExpectMsgAsync<bool>());
            sender.Tell(new Messages.Run());
            await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>();
            await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>();
            object bad = fault switch
            {
                "credit" => new Credit(settings.RunId, 3),
                "foreign-credit" => new Credit(Guid.NewGuid(), 1),
                "echo" => payloads[1],
                "receipt" => new Receipt(settings, settings.Messages),
                _ => ReceiveTimeout.Instance
            };
            probe.Send(sender, bad);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var error = await Record.ExceptionAsync(async () => await completion.Task.WaitAsync(deadline.Token));
            Assert.NotNull(error);
            Assert.IsType(fault == "timeout" ? typeof(TimeoutException) : typeof(InvalidOperationException), error);
        }

        [Fact(DisplayName = "Stopping a validated sender cancels incomplete delivery without producing a successful count")]
        public async Task Should_CancelCompletion_When_SenderStops()
        {
            var settings = Settings();
            var payloads = PreparePayloads(settings.Messages);
            var completion = new Outcome();
            var sender = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, TestActor, completion)));
            Watch(sender);
            Sys.Stop(sender);
            await ExpectTerminatedAsync(sender);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await completion.Task);
        }

        [Fact(DisplayName = "Validated credit bounds outstanding data and requires the last credit and delivery receipt")]
        public async Task Should_WaitForCreditAndReceipt_When_InitialWindowIsExhausted()
        {
            var probe = CreateTestProbe();
            var settings = Settings();
            var payloads = PreparePayloads(3);
            var completion = new Outcome();
            var sender = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, probe.Ref, completion)));
            sender.Tell(new Prepare(), TestActor);
            await probe.ExpectMsgAsync<Agreement>();
            probe.Send(sender, new Ready(settings));
            Assert.True(await ExpectMsgAsync<bool>());
            sender.Tell(new Messages.Run());
            Assert.Equal(payloads[0], await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
            Assert.Equal(payloads[1], await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            Assert.False(completion.Task.IsCompleted);
            probe.Send(sender, new Credit(settings.RunId, 1));
            Assert.Equal(payloads[2], await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
            Assert.Equal(settings.RunId, (await probe.ExpectMsgAsync<Fence>()).RunId);
            probe.Send(sender, new Credit(settings.RunId, 2));
            probe.Send(sender, new Credit(settings.RunId, 3));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            Assert.False(completion.Task.IsCompleted);
            probe.Send(sender, new Receipt(settings, 3));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal(3, await completion.Task.WaitAsync(deadline.Token));
        }

        [Fact(DisplayName = "Validated agreement and progress records survive the real default serializer")]
        public void Should_RoundTripControlRecords_When_RemoteSerializationIsUsed()
        {
            var settings = Settings();
            object[] records = { settings, new Ready(settings), new Credit(settings.RunId, 1),
                new Fence(settings.RunId), new Receipt(settings, 3), new Failed("injected failure") };
            var serialization = ((ExtendedActorSystem)Sys).Serialization;
            foreach (var record in records)
            {
                var serializer = serialization.FindSerializerFor(record);
                var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, record);
                Assert.Equal(record, serialization.Deserialize(serializer.ToBinary(record), serializer.Identifier, manifest));
            }
        }

        [Fact(DisplayName = "A receiver failure after its receipt invalidates the outcome retained through teardown")]
        public async Task Should_RetainLateFailure_When_ReceiptHasAlreadyCompleted()
        {
            var probe = CreateTestProbe();
            var settings = Settings(1, 1);
            var payloads = PreparePayloads(1);
            var outcome = new Outcome();
            var sender = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, probe.Ref, outcome)));
            sender.Tell(new Prepare(), TestActor);
            await probe.ExpectMsgAsync<Agreement>();
            probe.Send(sender, new Ready(settings));
            await ExpectMsgAsync<bool>();
            sender.Tell(new Messages.Run());
            await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>();
            await probe.ExpectMsgAsync<Fence>();
            probe.Send(sender, new Credit(settings.RunId, 1));
            probe.Send(sender, new Receipt(settings, 1));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal(1, await outcome.Task.WaitAsync(deadline.Token));
            probe.Send(sender, new Failed("post-fence duplicate"));
            await AwaitAssertAsync(() => Assert.Throws<InvalidOperationException>(outcome.ThrowIfFailed),
                TimeSpan.FromSeconds(3));
        }

        [Fact(DisplayName = "Missing delivery credit triggers the actual bounded progress timeout without retrying")]
        public async Task Should_Timeout_When_ThePeerStopsAcknowledging()
        {
            var probe = CreateTestProbe();
            var settings = Settings(3, 1);
            var payloads = PreparePayloads(3);
            var outcome = new Outcome();
            var sender = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, probe.Ref, outcome)));
            sender.Tell(new Prepare(), TestActor);
            await probe.ExpectMsgAsync<Agreement>();
            probe.Send(sender, new Ready(settings));
            await ExpectMsgAsync<bool>();
            sender.Tell(new Messages.Run());
            await probe.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Assert.ThrowsAsync<TimeoutException>(async () => await outcome.Task.WaitAsync(deadline.Token));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }
    }
}
