//-----------------------------------------------------------------------
// <copyright file="JsonFramingBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.IO;
using Akka.Streams.Implementation;
using NBench;

namespace Akka.Streams.Tests.Performance
{
    public class JsonFramingBenchmark
    {
        private const string BracketThroughputCounterName = "Bracket";

        private const string Input = @"{""fname"":""Frank"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false},
             {""fname"":""Bob"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false},
             {""fname"":""Bob"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false},
             {""fname"":""Bob"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false},
             {""fname"":""Bob"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false},
             {""fname"":""Bob"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false},
             {""fname"":""Hank"",""name"":""Smith"",""age"":42,""id"":1337,""boardMember"":false}";

        private static readonly ByteString Json = ByteString.FromString(Input);
        
        private Counter _bracketThroughputCounter;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _bracketThroughputCounter = context.GetCounter(BracketThroughputCounterName);
        }

        [PerfBenchmark(Description = "Testing throughput for an Offer and a single Poll call",
            RunMode = RunMode.Throughput,
            NumberOfIterations = 13, TestMode = TestMode.Test, RunTimeMilliseconds = 1000)]
        [CounterThroughputAssertion(BracketThroughputCounterName, MustBe.GreaterThan, 150000)]
        public void Counting_1()
        {
            var bracket = new JsonObjectParser();
            bracket.Offer(Json);
            bracket.Poll();
            _bracketThroughputCounter.Increment();
        }

        [PerfBenchmark(Description = "Testing throughput for an Offer and 6 Poll calls",
            RunMode = RunMode.Throughput,
            NumberOfIterations = 13, TestMode = TestMode.Test, RunTimeMilliseconds = 1000)]
        [CounterThroughputAssertion(BracketThroughputCounterName, MustBe.GreaterThan, 20000)]
        public void Counting_offer_5()
        {
            var bracket = new JsonObjectParser();
            bracket.Offer(Json);
            bracket.Poll();
            bracket.Poll();
            bracket.Poll();
            bracket.Poll();
            bracket.Poll();
            bracket.Poll();
            _bracketThroughputCounter.Increment();
        }
    }
}
