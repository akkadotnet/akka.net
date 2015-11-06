using System;
using System.Collections.Generic;
using Akka.Actor;

namespace ClusterToolsExample.Shared
{
    [Serializable]
    public sealed class Batch
    {
        public readonly int Size;

        public Batch(int size)
        {
            Size = size;
        }
    }

    [Serializable]
    public sealed class Work
    {
        public readonly int Id;

        public Work(int id)
        {
            Id = id;
        }
    }

    [Serializable]
    public sealed class Result
    {
        public readonly int Id;

        public Result(int id)
        {
            Id = id;
        }
    }

    [Serializable]
    public sealed class SendReport
    {
        public static readonly SendReport Instance = new SendReport();

        private SendReport()
        {
        }
    }

    [Serializable]
    public sealed class Report
    {
        public readonly IDictionary<IActorRef, int> Counts;

        public Report(IDictionary<IActorRef, int> counts)
        {
            Counts = counts;
        }
    }
}