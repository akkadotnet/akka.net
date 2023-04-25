//-----------------------------------------------------------------------
// <copyright file="CircuitBreakerStressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Pattern
{
    public class CircuitBreakerStressSpec : AkkaSpec
    {
        internal class RequestJob
        {
            public static RequestJob Instance => new RequestJob();
            private RequestJob() { }
        }

        internal class JobDone
        {
            public static JobDone Instance => new JobDone();
            private JobDone() { }
        }

        internal class GetResult
        {
            public static GetResult Instance => new GetResult();
            private GetResult() { }
        }

        internal class Result
        {
            public int DoneCount { get; }
            public int TimeoutCount { get; }
            public int FailCount { get; }
            public int CircCount { get; }

            public Result(int doneCount, int timeoutCount, int failCount, int circCount)
            {
                DoneCount = doneCount;
                TimeoutCount = timeoutCount;
                FailCount = failCount;
                CircCount = circCount;
            }
        }

        internal class StressActor : UntypedActor
        {
            private readonly CircuitBreaker _breaker;
            private int _doneCount;
            private int _timeoutCount;
            private int _failCount;
            private int _circCount;

            public StressActor(CircuitBreaker breaker) => _breaker = breaker;

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case RequestJob _:
                        _breaker.WithCircuitBreaker(Job).PipeTo(Self);
                        break;
                    case JobDone _:
                        _doneCount++;
                        break;
                    case Status.Failure { Cause: OpenCircuitException _ }:
                        _circCount++;
                        _breaker.WithCircuitBreaker(Job).PipeTo(Self);
                        break;
                    case Status.Failure { Cause: TimeoutException _ }:
                        _timeoutCount++;
                        _breaker.WithCircuitBreaker(Job).PipeTo(Self);
                        break;
                    case Status.Failure _:
                        _failCount++;
                        _breaker.WithCircuitBreaker(Job).PipeTo(Self);
                        break;
                    case GetResult _:
                        Sender.Tell(new Result(_doneCount, _timeoutCount, _failCount, _circCount));
                        break;
                    default:
                        base.Unhandled(message);
                        break;
                }
            }

            private static async Task<JobDone> Job()
            {
                await Task.Delay(TimeSpan.FromMilliseconds(ThreadLocalRandom.Current.Next(300)));
                return JobDone.Instance;
            }
        }

        public CircuitBreakerStressSpec(ITestOutputHelper output) 
            : base(output)
        { }

        [Fact]
        public async Task A_CircuitBreaker_stress_test()
        {
            var breaker = new CircuitBreaker(Sys.Scheduler, 5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(200));
            var stressActors = Enumerable.Range(0, 3).Select(i => Sys.ActorOf(Props.Create<StressActor>(breaker))).ToList();

            for (var i = 0; i < 1000; i++)
                foreach (var stressActor in stressActors)
                {
                    stressActor.Tell(RequestJob.Instance);
                }

            // let them work for a while
            await Task.Delay(3000);

            foreach (var stressActor in stressActors)
            {
                stressActor.Tell(GetResult.Instance);
                var result = ExpectMsg<Result>();
                result.FailCount.ShouldBe(0);

                Output.WriteLine("FailCount:{0}, DoneCount:{1}, CircCount:{2}, TimeoutCount:{3}", 
                    result.FailCount, result.DoneCount, result.CircCount, result.TimeoutCount);
            }
        }
    }
}
