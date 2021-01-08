//-----------------------------------------------------------------------
// <copyright file="AckedDeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class AckedDeliverySpec : AkkaSpec
    {
        sealed class Sequenced : IHasSequenceNumber
        {
            public Sequenced(SeqNo seq, string body)
            {
                Body = body;
                Seq = seq;
            }

            public SeqNo Seq { get; private set; }

            public string Body { get; private set; }

            public override string ToString()
            {
                return string.Format("MSG[{0}]]", Seq.RawValue);
            }
        }

        private Sequenced Msg(long seq) { return new Sequenced(seq, "msg" + seq); }

        [Fact]
        public void SeqNo_must_implement_simple_ordering()
        {
            var sm1 = new SeqNo(-1);
            var s0 = new SeqNo(0);
            var s1 = new SeqNo(1);
            var s2 = new SeqNo(2);
            var s0b = new SeqNo(0);

            Assert.True(sm1 < s0);
            Assert.False(sm1 > s0);

            Assert.True(s0 < s1);
            Assert.False(s0 > s1);

            Assert.True(s1 < s2);
            Assert.False(s1 > s2);

            Assert.True(s0b == s0);
        }

        [Fact]
        public void SeqNo_must_correctly_handle_wrapping_over()
        {
            var s1 = new SeqNo(long.MaxValue - 1);
            var s2 = new SeqNo(long.MaxValue);
            var s3 = new SeqNo(long.MinValue);
            var s4 = new SeqNo(long.MinValue + 1);

            Assert.True(s1 < s2);
            Assert.False(s1 > s2);

            Assert.True(s2 < s3);
            Assert.False(s2 > s3);

            Assert.True(s3 < s4);
            Assert.False(s3 > s4);
        }

        [Fact]
        public void SeqNo_must_correctly_handle_large_gaps()
        {
            var smin = new SeqNo(long.MinValue);
            var smin2 = new SeqNo(long.MinValue + 1);
            var s0 = new SeqNo(0);

            Assert.True(s0 < smin);
            Assert.False(s0 > smin);

            Assert.True(smin2 < s0);
            Assert.False(smin2 > s0);
        }

        [Fact]
        public void SeqNo_must_handle_overflow()
        {
            var s1 = new SeqNo(long.MaxValue - 1);
            var s2 = new SeqNo(long.MaxValue);
            var s3 = new SeqNo(long.MinValue);
            var s4 = new SeqNo(long.MinValue + 1);

            Assert.True(s1.Inc() == s2);
            Assert.True(s2.Inc() == s3);
            Assert.True(s3.Inc() == s4);
        }

        [Fact]
        public void SendBuffer_must_aggregate_unacked_messages_in_order()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);

            var b1 = b0.Buffer(msg0);
            Assert.True(b1.NonAcked.SequenceEqual(new[]{msg0}));

            var b2 = b1.Buffer(msg1);
            Assert.True(b2.NonAcked.SequenceEqual(new[] { msg0, msg1 }));

            var b3 = b2.Buffer(msg2);
            Assert.True(b3.NonAcked.SequenceEqual(new[] { msg0, msg1, msg2 }));
        }

        [Fact]
        public void SendBuffer_must_refuse_buffering_new_messages_if_capacity_reached()
        {
            var buffer = new AckedSendBuffer<Sequenced>(4).Buffer(Msg(0)).Buffer(Msg(1)).Buffer(Msg(2)).Buffer(Msg(3));

            XAssert.Throws<ResendBufferCapacityReachedException>(() => buffer.Buffer(Msg(4)));
        }

        [Fact]
        public void SendBuffer_must_remove_messages_from_buffer_when_cumulative_ack_received()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);
            var msg4 = Msg(4);

            var b1 = b0.Buffer(msg0);
            Assert.True(b1.NonAcked.SequenceEqual(new[] { msg0 }));

            var b2 = b1.Buffer(msg1);
            Assert.True(b2.NonAcked.SequenceEqual(new[] { msg0, msg1 }));

            var b3 = b2.Buffer(msg2);
            Assert.True(b3.NonAcked.SequenceEqual(new[] { msg0, msg1, msg2 }));

            var b4 = b3.Acknowledge(new Ack(new SeqNo(1)));
            Assert.True(b4.NonAcked.SequenceEqual(new[]{ msg2 }));

            var b5 = b4.Buffer(msg3);
            Assert.True(b5.NonAcked.SequenceEqual(new []{ msg2,msg3 }));

            var b6 = b5.Buffer(msg4);
            Assert.True(b6.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));

            var b7 = b6.Acknowledge(new Ack(new SeqNo(1)));
            Assert.True(b7.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));

            var b8 = b7.Acknowledge(new Ack(new SeqNo(2)));
            Assert.True(b8.NonAcked.SequenceEqual(new[] { msg3, msg4 }));

            var b9 = b8.Acknowledge(new Ack(new SeqNo(4)));
            Assert.True(b9.NonAcked.Count == 0);
        }

        [Fact]
        public void SendBuffer_must_keep_NACKed_messages_in_buffer_if_selective_nacks_are_received()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);
            var msg4 = Msg(4);

            var b1 = b0.Buffer(msg0);
            Assert.True(b1.NonAcked.SequenceEqual(new[] { msg0 }));

            var b2 = b1.Buffer(msg1);
            Assert.True(b2.NonAcked.SequenceEqual(new[] { msg0, msg1 }));

            var b3 = b2.Buffer(msg2);
            Assert.True(b3.NonAcked.SequenceEqual(new[] { msg0, msg1, msg2 }));

            var b4 = b3.Acknowledge(new Ack(new SeqNo(1), new [] {new SeqNo(0)}));
            Assert.True(b4.NonAcked.SequenceEqual(new []{ msg2 }));
            Assert.True(b4.Nacked.SequenceEqual(new[] { msg0 }));

            var b5 = b4.Buffer(msg3).Buffer(msg4);
            Assert.True(b5.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));
            Assert.True(b5.Nacked.SequenceEqual(new[] { msg0 }));

            var b6 = b5.Acknowledge(new Ack(new SeqNo(4), new []{new SeqNo(2), new SeqNo(3)}));
            Assert.True(b6.NonAcked.Count == 0);
            Assert.True(b6.Nacked.SequenceEqual(new[] { msg2, msg3 }));

            var b7 = b6.Acknowledge(new Ack(new SeqNo(4)));
            Assert.True(b7.NonAcked.Count == 0);
            Assert.True(b7.Nacked.Count == 0);
        }

        [Fact]
        public void SendBuffer_must_throw_exception_if_nonbuffered_sequence_number_is_NACKed()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg1 = Msg(1);
            var msg2 = Msg(2);

            var b1 = b0.Buffer(msg1).Buffer(msg2);
            XAssert.Throws<ResendUnfulfillableException>(() => b1.Acknowledge(new Ack(new SeqNo(2), new []{ new SeqNo(0) })));
        }

        [Fact]
        public void ReceiveBuffer_must_enqueue_message_in_buffer_if_needed_return_the_list_of_deliverable_messages_and_acks()
        {
            var b0 = new AckedReceiveBuffer<Sequenced>();
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);
            var msg4 = Msg(4);
            var msg5 = Msg(5);

            var d1 = b0.Receive(msg1).ExtractDeliverable();
            Assert.True(d1.Deliverables.Count == 0);
            Assert.Equal(new SeqNo(1), d1.Ack.CumulativeAck);
            Assert.True(d1.Ack.Nacks.SequenceEqual(new[]{ new SeqNo(0) }));
            var b1 = d1.Buffer;

            var d2 = b1.Receive(msg0).ExtractDeliverable();
            Assert.True(d2.Deliverables.SequenceEqual(new[] { msg0, msg1 }));
            Assert.Equal(new SeqNo(1), d2.Ack.CumulativeAck);
            var b3 = d2.Buffer;

            var d3 = b3.Receive(msg4).ExtractDeliverable();
            Assert.True(d3.Deliverables.Count == 0);
            Assert.Equal(new SeqNo(4), d3.Ack.CumulativeAck);
            Assert.True(d3.Ack.Nacks.SequenceEqual(new[] { new SeqNo(2), new SeqNo(3) }));
            var b4 = d3.Buffer;

            var d4 = b4.Receive(msg2).ExtractDeliverable();
            Assert.True(d4.Deliverables.SequenceEqual(new[] { msg2 }));
            Assert.Equal(new SeqNo(4), d4.Ack.CumulativeAck);
            Assert.True(d4.Ack.Nacks.SequenceEqual(new[] { new SeqNo(3) }));
            var b5 = d4.Buffer;

            var d5 = b5.Receive(msg5).ExtractDeliverable();
            Assert.True(d5.Deliverables.Count == 0);
            Assert.Equal(new SeqNo(5), d5.Ack.CumulativeAck);
            Assert.True(d5.Ack.Nacks.SequenceEqual(new[] { new SeqNo(3) }));
            var b6 = d5.Buffer;

            var d6 = b6.Receive(msg3).ExtractDeliverable();
            Assert.True(d6.Deliverables.SequenceEqual(new[] { msg3, msg4, msg5 }));
            Assert.Equal(new SeqNo(5), d6.Ack.CumulativeAck);
        }

        [Fact]
        public void ReceiveBuffer_must_handle_duplicate_arrivals_correctly()
        {
            var buf = new AckedReceiveBuffer<Sequenced>();
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);

            var buf2 = buf.Receive(msg0).Receive(msg1).Receive(msg2).ExtractDeliverable().Buffer;

            var buf3 = buf2.Receive(msg0).Receive(msg1).Receive(msg2);

            var d = buf3.ExtractDeliverable();
            Assert.True(d.Deliverables.Count == 0);
            Assert.Equal(new SeqNo(2), d.Ack.CumulativeAck);
        }

        [Fact]
        public void ReceiveBuffer_must_be_able_to_correctly_merge_with_another_receive_buffer()
        {
            var buf1 = new AckedReceiveBuffer<Sequenced>();
            var buf2 = new AckedReceiveBuffer<Sequenced>();
            var msg0 = Msg(0);
            var msg1a = Msg(1);
            var msg1b = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);

            var buf = buf1.Receive(msg1a).Receive(msg2).MergeFrom(buf2.Receive(msg1b).Receive(msg3));

            var d = buf.Receive(msg0).ExtractDeliverable();
            Assert.True(d.Deliverables.SequenceEqual(new []{ msg0, msg1a, msg2, msg3 }));
            Assert.Equal(new SeqNo(3), d.Ack.CumulativeAck);
        }

        #region Receive + Send Buffer Tests

        public bool Happened(double p) { return ThreadLocalRandom.Current.NextDouble() < p; }

        public int Geom(Double p, int limit = 5, int acc = 0)
        {
            while (true)
            {
                if (acc == limit) return acc;
                if (Happened(p)) return acc;
                acc = acc + 1;
            }
        }

        [Fact]
        public void SendBuffer_and_ReceiveBuffer_must_correctly_cooperate_with_each_other()
        {
            var msgCount = 1000;
            var deliveryProbability = 0.5D;
            var referenceList = Enumerable.Range(0, msgCount).Select(x => Msg(x)).ToList();

            var toSend = referenceList;
            var received = new List<Sequenced>();
            var sndBuf = new AckedSendBuffer<Sequenced>(10);
            var rcvBuf = new AckedReceiveBuffer<Sequenced>();
            var log = new List<string>();
            var lastAck = new Ack(new SeqNo(-1));

            Action<string> dbLog = log.Add;
            Action<int, double> senderSteps = (steps, p) =>
            {
                var resends = new List<Sequenced>(sndBuf.Nacked.Concat(sndBuf.NonAcked).Take(steps));

                var sends = new List<Sequenced>();
                if (steps - resends.Count > 0)
                {
                    var tmp = toSend.Take(steps - resends.Count).ToList();
                    toSend = toSend.Drop(steps - resends.Count).ToList();
                    sends = tmp;
                }

                foreach (var msg in resends.Concat(sends))
                {
                    if (sends.Contains(msg)) sndBuf = sndBuf.Buffer(msg);
                    if (Happened(p))
                    {
                        var del = rcvBuf.Receive(msg).ExtractDeliverable();
                        rcvBuf = del.Buffer;
                        dbLog(string.Format("{0} -- {1} --> {2}", sndBuf, msg, rcvBuf));
                        lastAck = del.Ack;
                        received.AddRange(del.Deliverables);
                        dbLog(string.Format("R: {0}", string.Join(",", received.Select(x => x.ToString()))));
                    }
                    else
                    {
                        dbLog(string.Format("{0} -- {1} --X {2}", sndBuf, msg, rcvBuf));
                    }
                }
            };

            Action<double> receiverStep = (p) =>
            {
                if (Happened(p))
                {
                    sndBuf = sndBuf.Acknowledge(lastAck);
                    dbLog(string.Format("{0} <-- {1} -- {2}", sndBuf, lastAck, rcvBuf));
                }
                else
                {
                    dbLog(string.Format("{0} X-- {1} -- {2}", sndBuf, lastAck, rcvBuf));
                }
            };

            //Dropping phase
            global::System.Diagnostics.Debug.WriteLine("Starting unreliable delivery for {0} messages, with delivery probably P = {1}", msgCount, deliveryProbability);
            var nextSteps = msgCount*2;
            while (nextSteps > 0)
            {
                var s = Geom(0.3, limit: 5);
                senderSteps(s, deliveryProbability);
                receiverStep(deliveryProbability);
                nextSteps--;
            }
            global::System.Diagnostics.Debug.WriteLine("Successfully delivered {0} messages from {1}", received.Count, msgCount);
            global::System.Diagnostics.Debug.WriteLine("Entering reliable phase");

            //Finalizing phase
            for (var i = 1; i <= msgCount; i++)
            {
                senderSteps(1, 1.0);
                receiverStep(1.0);
            }

            if (!received.SequenceEqual(referenceList))
            {
                global::System.Diagnostics.Debug.WriteLine(string.Join(Environment.NewLine, log));
                global::System.Diagnostics.Debug.WriteLine("Received: ");
                global::System.Diagnostics.Debug.WriteLine(string.Join(Environment.NewLine, received.Select(x => x.ToString())));
                Assert.True(false,"Not all messages were received");
            }

            global::System.Diagnostics.Debug.WriteLine("All messages have been successfully delivered");
        }

        #endregion

    }
}

