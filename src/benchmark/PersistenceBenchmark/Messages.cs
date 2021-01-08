//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace PersistenceBenchmark
{
    [Serializable]
    public sealed class StopMeasure
    {
        public static readonly StopMeasure Instance = new StopMeasure();

        private StopMeasure()
        {
        }
    }

    [Serializable]
    public sealed class FailAt
    {
        public readonly long SequenceNr;

        public FailAt(long sequenceNr)
        {
            SequenceNr = sequenceNr;
        }
    }

    [Serializable]
    public sealed class Measure
    {
        public readonly int MessagesCount;

        public Measure(int messagesCount)
        {
            MessagesCount = messagesCount;
        }

        public DateTime StartedAt { get; private set; }
        public DateTime StoppedAt { get; private set; }

        public void StartMeasure()
        {
            StartedAt = DateTime.Now;
        }

        public double StopMeasure()
        {
            StoppedAt = DateTime.Now;
            return MessagesCount/(StoppedAt - StartedAt).TotalSeconds;
        }
    }

    public class PerformanceTestException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PerformanceTestException"/> class.
        /// </summary>
        public PerformanceTestException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PerformanceTestException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public PerformanceTestException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PerformanceTestException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public PerformanceTestException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PerformanceTestException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected PerformanceTestException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
