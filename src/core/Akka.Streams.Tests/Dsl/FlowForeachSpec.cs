//-----------------------------------------------------------------------
// <copyright file="FlowForeachSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowForeachSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowForeachSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task A_Foreach_must_call_the_procedure_for_each_element()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 3)).RunForeach(i => 
                TestActor.Tell(i), Materializer)
                .ContinueWith(                                                                             
                    task =>                                                                             
                    {                                                                                
                        if (task.IsCompleted && task.Exception == null)                                                                                  
                            TestActor.Tell("done");                                                                             
                    });
                await ExpectMsgAsync(1);
                await ExpectMsgAsync(2);
                await ExpectMsgAsync(3);
                await ExpectMsgAsync("done");
            }, Materializer);
        }

        [Fact]
        public async Task A_Foreach_must_complete_the_future_for_an_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.Empty<int>().RunForeach(i => 
                TestActor.Tell(i), Materializer).ContinueWith(                                                                        
                    task =>                                                                        
                    {                                                                         
                        if (task.IsCompleted && task.Exception == null)                                                                                     
                            TestActor.Tell("done");                                                                             
                    });
                await ExpectMsgAsync("done");
            }, Materializer);
        }

        [Fact]
        public async Task A_Foreach_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p).RunForeach(i => TestActor.Tell(i), Materializer).ContinueWith(task =>
                {
                    if (task.Exception != null)
                        TestActor.Tell(task.Exception.InnerException);
                });
                var proc = await p.ExpectSubscriptionAsync();
                var ex = new TestException("ex");
                proc.SendError(ex);
                await ExpectMsgAsync(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_Foreach_must_complete_future_with_failure_when_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var error = new TestException("test");
                var future = Source.Single(1).RunForeach(_ =>
                {
                    throw error;
                }, Materializer);

                future.Invoking(f => f.Wait(TimeSpan.FromSeconds(3)))
                    .Should().Throw<TestException>()
                    .And.Should()
                    .Be(error);
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
