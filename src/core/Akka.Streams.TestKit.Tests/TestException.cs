using System;

namespace Akka.Streams.TestKit.Tests
{
    public class TestException : SystemException
    {
        public TestException(string message) : base(message) { }

        protected bool Equals(TestException other)
        {
            return Message.Equals(other.Message);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TestException) obj);
        }

        public override int GetHashCode()
        {
            return Message.GetHashCode();
        }
    }
}