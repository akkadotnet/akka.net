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
    }
}
