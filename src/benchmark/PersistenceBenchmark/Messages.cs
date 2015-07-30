//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public DateTime StopedAt { get; private set; }

        public void StartMeasure()
        {
            StartedAt = DateTime.Now;
        }

        public double StopMeasure()
        {
            StopedAt = DateTime.Now;
            return MessagesCount/(StopedAt - StartedAt).TotalSeconds;
        }
    }

    public class PerformanceTestException : Exception
    {
        public PerformanceTestException()
        {
        }

        public PerformanceTestException(string message) : base(message)
        {
        }

        public PerformanceTestException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PerformanceTestException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}