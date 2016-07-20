using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.IO;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Tests.Util
{
    public class ConcurrentEnvelopeQueueTests
    {
        [Fact]
        public void When_empty_Should_not_dequeue()
        {
            var queue = new ConcurrentEnvelopeQueue();

            Envelope result;
            queue.TryDequeue(out result).ShouldBeFalse();
            result.Sender.ShouldBe(null);
            result.Message.ShouldBe(null);
        }

        [Fact]
        public void When_empty_Should_look_like_empty()
        {
            var queue = new ConcurrentEnvelopeQueue();

            queue.Count.ShouldBe(0);
            queue.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void When_enqueued_Should_be_able_to_dequeue()
        {
            var queue = new ConcurrentEnvelopeQueue();
            var message = new object();

            var envelope = new Envelope
            {
                Sender = ActorRefs.NoSender,
                Message = message
            };

            queue.Enqueue(ref envelope);

            Envelope result;
            queue.TryDequeue(out result).ShouldBeTrue();

            result.Message.ShouldBe(message);

            ReferenceEquals(result.Sender, ActorRefs.NoSender).ShouldBeTrue();
        }

        [Fact]
        public void When_enqueued_from_multiple_producers_should_be_dequeued_in_fifo_way()
        {
            const int messagesToSendPerThread = 1 << 16;
            const int producerCount = 2;
            const int expectedMessages = messagesToSendPerThread*producerCount;

            var startEvent = new ManualResetEventSlim(false);
            var producers = new Thread[producerCount];
            var endEvent = new CountdownEvent(producers.Length);
            var lastSeenMessagePerProducer = new int[producerCount];

            var queue = new ConcurrentEnvelopeQueue();

            var messages = new[] {new object(), new object(), new object(), new object(),};
            var envelopes = messages.Select(m => new Envelope {Message = m}).ToArray();

            for (var i = 0; i < producers.Length; i++)
            {
                producers[i] = new Thread(msgs =>
                {
                    var toSend = (Envelope[]) msgs;
                    startEvent.Wait();
                    for (var j = 0; j < messagesToSendPerThread; j++)
                    {
                        queue.Enqueue(ref toSend[(j + 1)%2]);
                    }
                    endEvent.Signal();
                })
                {
                    IsBackground = true,
                    Name = "Producer #" + i
                };
                producers[i].Start(envelopes.Skip(i*2).Take(2).ToArray());
            }

            startEvent.Set();
            var count = 0;
            while (count < expectedMessages)
            {
                Envelope result;
                if (queue.TryDequeue(out result))
                {
                    var index = Array.IndexOf(messages, result.Message);
                    var producerIndex = index >> 1;
                    var messageIndex = index & 1;

                    lastSeenMessagePerProducer[producerIndex].ShouldNotBe(messageIndex);
                    lastSeenMessagePerProducer[producerIndex] = messageIndex;

                    count += 1;
                }
            }

            endEvent.Wait(TimeSpan.FromSeconds(1)).ShouldBeTrue();
        }
    }
}