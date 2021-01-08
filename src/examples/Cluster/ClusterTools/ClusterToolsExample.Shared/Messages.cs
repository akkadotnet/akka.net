//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
