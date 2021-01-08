//-----------------------------------------------------------------------
// <copyright file="TestJournalRejectionException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class TestJournalRejectionException : Exception
    {
        public TestJournalRejectionException() { }
        public TestJournalRejectionException(string message) : base(message) { }
        public TestJournalRejectionException(string message, Exception inner) : base(message, inner) { }
        protected TestJournalRejectionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
