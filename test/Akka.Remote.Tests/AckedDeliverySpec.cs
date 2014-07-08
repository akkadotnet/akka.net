using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Akka.Event;
using Akka.Tests;
using Akka.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests
{
    [TestClass]
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

        [TestMethod]
        public void SeqNo_must_implement_simple_ordering()
        {
            var sm1 = new SeqNo(-1);
            var s0 = new SeqNo(0);
            var s1 = new SeqNo(1);
            var s2 = new SeqNo(2);
            var s0b = new SeqNo(0);

            Assert.IsTrue(sm1 < s0);
            Assert.IsFalse(sm1 > s0);

            Assert.IsTrue(s0 < s1);
            Assert.IsFalse(s0 > s1);

            Assert.IsTrue(s1 < s2);
            Assert.IsFalse(s1 > s2);

            Assert.IsTrue(s0b == s0);
        }

        [TestMethod]
        public void SeqNo_must_correctly_handle_wrapping_over()
        {
            var s1 = new SeqNo(long.MaxValue - 1);
            var s2 = new SeqNo(long.MaxValue);
            var s3 = new SeqNo(long.MinValue);
            var s4 = new SeqNo(long.MinValue + 1);

            Assert.IsTrue(s1 < s2);
            Assert.IsFalse(s1 > s2);

            Assert.IsTrue(s2 < s3);
            Assert.IsFalse(s2 > s3);

            Assert.IsTrue(s3 < s4);
            Assert.IsFalse(s3 > s4);
        }

        [TestMethod]
        public void SeqNo_must_correctly_handle_large_gaps()
        {
            var smin = new SeqNo(long.MinValue);
            var smin2 = new SeqNo(long.MinValue + 1);
            var s0 = new SeqNo(0);

            Assert.IsTrue(s0 < smin);
            Assert.IsFalse(s0 > smin);

            Assert.IsTrue(smin2 < s0);
            Assert.IsFalse(smin2 > s0);
        }

        [TestMethod]
        public void SeqNo_must_handle_overflow()
        {
            var s1 = new SeqNo(long.MaxValue - 1);
            var s2 = new SeqNo(long.MaxValue);
            var s3 = new SeqNo(long.MinValue);
            var s4 = new SeqNo(long.MinValue + 1);

            Assert.IsTrue(s1.Inc() == s2);
            Assert.IsTrue(s2.Inc() == s3);
            Assert.IsTrue(s3.Inc() == s4);
        }

        [TestMethod]
        public void SendBuffer_must_aggregate_unacked_messages_in_order()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);

            var b1 = b0.Buffer(msg0);
            Assert.IsTrue(b1.NonAcked.SequenceEqual(new[]{msg0}));

            var b2 = b1.Buffer(msg1);
            Assert.IsTrue(b2.NonAcked.SequenceEqual(new[] { msg0, msg1 }));

            var b3 = b2.Buffer(msg2);
            Assert.IsTrue(b3.NonAcked.SequenceEqual(new[] { msg0, msg1, msg2 }));
        }

        [TestMethod]
        public void SendBuffer_must_refuse_buffering_new_messages_if_capacity_reached()
        {
            var buffer = new AckedSendBuffer<Sequenced>(4).Buffer(Msg(0)).Buffer(Msg(1)).Buffer(Msg(2)).Buffer(Msg(3));

            intercept<ResendBufferCapacityReachedException>(() => buffer.Buffer(Msg(4)));
        }

        [TestMethod]
        public void SendBuffer_must_remove_messages_from_buffer_when_cumulative_ack_received()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);
            var msg4 = Msg(4);

            var b1 = b0.Buffer(msg0);
            Assert.IsTrue(b1.NonAcked.SequenceEqual(new[] { msg0 }));

            var b2 = b1.Buffer(msg1);
            Assert.IsTrue(b2.NonAcked.SequenceEqual(new[] { msg0, msg1 }));

            var b3 = b2.Buffer(msg2);
            Assert.IsTrue(b3.NonAcked.SequenceEqual(new[] { msg0, msg1, msg2 }));

            var b4 = b3.Acknowledge(new Ack(new SeqNo(1)));
            Assert.IsTrue(b4.NonAcked.SequenceEqual(new[]{ msg2 }));

            var b5 = b4.Buffer(msg3);
            Assert.IsTrue(b5.NonAcked.SequenceEqual(new []{ msg2,msg3 }));

            var b6 = b5.Buffer(msg4);
            Assert.IsTrue(b6.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));

            var b7 = b6.Acknowledge(new Ack(new SeqNo(1)));
            Assert.IsTrue(b7.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));

            var b8 = b7.Acknowledge(new Ack(new SeqNo(2)));
            Assert.IsTrue(b8.NonAcked.SequenceEqual(new[] { msg3, msg4 }));

            var b9 = b8.Acknowledge(new Ack(new SeqNo(5)));
            Assert.IsTrue(b9.NonAcked.Count == 0);
        }

        [TestMethod]
        public void SendBuffer_must_keep_NACKed_messages_in_buffer_if_selective_nacks_are_received()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);
            var msg4 = Msg(4);

            var b1 = b0.Buffer(msg0);
            Assert.IsTrue(b1.NonAcked.SequenceEqual(new[] { msg0 }));

            var b2 = b1.Buffer(msg1);
            Assert.IsTrue(b2.NonAcked.SequenceEqual(new[] { msg0, msg1 }));

            var b3 = b2.Buffer(msg2);
            Assert.IsTrue(b3.NonAcked.SequenceEqual(new[] { msg0, msg1, msg2 }));

            var b4 = b3.Acknowledge(new Ack(new SeqNo(1), new [] {new SeqNo(0)}));
            Assert.IsTrue(b4.NonAcked.SequenceEqual(new []{ msg2 }));
            Assert.IsTrue(b4.Nacked.SequenceEqual(new[] { msg0 }));

            var b5 = b4.Buffer(msg3).Buffer(msg4);
            Assert.IsTrue(b5.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));
            Assert.IsTrue(b5.Nacked.SequenceEqual(new[] { msg0 }));

            var b6 = b5.Acknowledge(new Ack(new SeqNo(4), new []{new SeqNo(2), new SeqNo(3)}));
            Assert.IsTrue(b6.NonAcked.Count == 0);
            Assert.IsTrue(b6.Nacked.SequenceEqual(new[] { msg2, msg3 }));

            var b7 = b6.Acknowledge(new Ack(new SeqNo(5)));
            Assert.IsTrue(b7.NonAcked.Count == 0);
            Assert.IsTrue(b7.Nacked.Count == 0);
        }

        [TestMethod]
        public void SendBuffer_must_throw_exception_if_nonbuffered_sequence_number_is_NACKed()
        {
            var b0 = new AckedSendBuffer<Sequenced>(10);
            var msg1 = Msg(1);
            var msg2 = Msg(2);

            var b1 = b0.Buffer(msg1).Buffer(msg2);
            intercept<ResendUnfulfillableException>(() => b1.Acknowledge(new Ack(new SeqNo(2), new []{ new SeqNo(0) })));
        }

        [TestMethod]
        public void ReceiveBuffer_must_enqueue_message_in_buffer_if_needed_return_the_list_of_deliverable_messages_and_acks()
        {
            var b0 = new AckedReceiveBuffer<Sequenced>();
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);
            var msg3 = Msg(3);
            var msg4 = Msg(4);
            var msg5 = Msg(5);

            var d1 = b0.Receive(msg1).ExtractDeliverable;
            Assert.IsTrue(d1.Deliverables.Count == 0);
            Assert.AreEqual(new SeqNo(1), d1.Ack.CumulativeAck);
            Assert.IsTrue(d1.Ack.Nacks.SequenceEqual(new[]{ new SeqNo(0) }));
            var b1 = d1.Buffer;

            var d2 = b1.Receive(msg0).ExtractDeliverable;
            Assert.IsTrue(d2.Deliverables.SequenceEqual(new[] { msg0, msg1 }));
            Assert.AreEqual(new SeqNo(1), d2.Ack.CumulativeAck);
            var b3 = d2.Buffer;

            var d3 = b3.Receive(msg4).ExtractDeliverable;
            Assert.IsTrue(d3.Deliverables.Count == 0);
            Assert.AreEqual(new SeqNo(4), d3.Ack.CumulativeAck);
            Assert.IsTrue(d3.Ack.Nacks.SequenceEqual(new[] { new SeqNo(2), new SeqNo(3) }));
            var b4 = d3.Buffer;

            var d4 = b4.Receive(msg2).ExtractDeliverable;
            Assert.IsTrue(d4.Deliverables.SequenceEqual(new[] { msg2 }));
            Assert.AreEqual(new SeqNo(4), d4.Ack.CumulativeAck);
            Assert.IsTrue(d4.Ack.Nacks.SequenceEqual(new[] { new SeqNo(3) }));
            var b5 = d4.Buffer;

            var d5 = b5.Receive(msg5).ExtractDeliverable;
            Assert.IsTrue(d5.Deliverables.Count == 0);
            Assert.AreEqual(new SeqNo(5), d5.Ack.CumulativeAck);
            Assert.IsTrue(d5.Ack.Nacks.SequenceEqual(new[] { new SeqNo(3) }));
            var b6 = d5.Buffer;

            var d6 = b6.Receive(msg3).ExtractDeliverable;
            Assert.IsTrue(d6.Deliverables.SequenceEqual(new[] { msg3, msg4, msg5 }));
            Assert.AreEqual(new SeqNo(5), d6.Ack.CumulativeAck);
        }

        [TestMethod]
        public void ReceiveBuffer_must_handle_duplicate_arrivals_correctly()
        {
            var buf = new AckedReceiveBuffer<Sequenced>();
            var msg0 = Msg(0);
            var msg1 = Msg(1);
            var msg2 = Msg(2);

            var buf2 = buf.Receive(msg0).Receive(msg1).Receive(msg2).ExtractDeliverable.Buffer;

            var buf3 = buf2.Receive(msg0).Receive(msg1).Receive(msg2);

            var d = buf3.ExtractDeliverable;
            Assert.IsTrue(d.Deliverables.Count == 0);
            Assert.AreEqual(new SeqNo(2), d.Ack.CumulativeAck);
        }

        [TestMethod]
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

            var d = buf.Receive(msg0).ExtractDeliverable;
            Assert.IsTrue(d.Deliverables.SequenceEqual(new []{ msg0, msg1b, msg2, msg3 }));
            Assert.AreEqual(new SeqNo(3), d.Ack.CumulativeAck);
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

        [TestMethod]
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
                        var del = rcvBuf.Receive(msg).ExtractDeliverable;
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
            System.Diagnostics.Debug.WriteLine("Starting unreliable delivery for {0} messages, with delivery probaboly P = {1}", msgCount, deliveryProbability);
            var nextSteps = msgCount*2;
            while (nextSteps > 0)
            {
                var s = Geom(0.3, limit: 5);
                senderSteps(s, deliveryProbability);
                receiverStep(deliveryProbability);
                nextSteps--;
            }
            System.Diagnostics.Debug.WriteLine("Successfully delivered {0} messages from {1}", received.Count, msgCount);
            System.Diagnostics.Debug.WriteLine("Entering reliable phase");

            //Finalizing pahase
            for (var i = 1; i <= msgCount; i++)
            {
                senderSteps(1, 1.0);
                receiverStep(1.0);
            }

            if (!received.SequenceEqual(referenceList))
            {
                System.Diagnostics.Debug.WriteLine(string.Join(Environment.NewLine, log));
                System.Diagnostics.Debug.WriteLine("Received: ");
                System.Diagnostics.Debug.WriteLine(string.Join(Environment.NewLine, received.Select(x => x.ToString())));
                Assert.Fail("Not all messages were received");
            }

            System.Diagnostics.Debug.WriteLine("All messages have been successfully delivered");
        }

        #endregion

    }
}
