//-----------------------------------------------------------------------
// <copyright file="TestJournalFailureException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class TestJournalFailureException : Exception
    {
        public TestJournalFailureException() { }
        public TestJournalFailureException(string message) : base(message) { }
        public TestJournalFailureException(string message, Exception inner) : base(message, inner) { }
        protected TestJournalFailureException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
