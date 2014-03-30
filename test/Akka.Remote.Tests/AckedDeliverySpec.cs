using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Akka.Tests;
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

            var b4 = b3.Acknowledge(new Ack(new SeqNo(1), new HashSet<SeqNo>() {new SeqNo(0)}));
            Assert.IsTrue(b4.NonAcked.SequenceEqual(new []{ msg2 }));
            Assert.IsTrue(b4.Nacked.SequenceEqual(new[] { msg0 }));

            var b5 = b4.Buffer(msg3).Buffer(msg4);
            Assert.IsTrue(b5.NonAcked.SequenceEqual(new[] { msg2, msg3, msg4 }));
            Assert.IsTrue(b5.Nacked.SequenceEqual(new[] { msg0 }));

            var b6 = b5.Acknowledge(new Ack(new SeqNo(4), new HashSet<SeqNo>() {new SeqNo(2), new SeqNo(3)}));
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
            intercept<ResendUnfulfillableException>(() => b1.Acknowledge(new Ack(new SeqNo(2), new HashSet<SeqNo>(){ new SeqNo(0) })));
        }
    }
}
