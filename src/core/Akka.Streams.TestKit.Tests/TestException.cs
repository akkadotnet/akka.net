//-----------------------------------------------------------------------
// <copyright file="TestException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.TestKit.Tests
{
    public class TestException : Exception
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
