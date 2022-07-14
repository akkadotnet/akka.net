//-----------------------------------------------------------------------
// <copyright file="AsyncEnumerableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Routing;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Nito.AsyncEx.Synchronous;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams.Actors;
using Akka.Streams.Tests.Actor;
using Reactive.Streams;

namespace Akka.Streams.Tests.Dsl
{
    public class AsyncEnumerableSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public AsyncEnumerableSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

#if NETCOREAPP
        [Fact] public async Task RunAsAsyncEnumerable_Uses_CancellationToken()
        {
            var input = Enumerable.Range(1, 6).ToList();

            var cts = new CancellationTokenSource();
            var token = cts.Token;
            
            var asyncEnumerable = Source.From(input).RunAsAsyncEnumerable(Materializer);
            var output = input.ToArray();
            bool caught = false;
            try
            {
                await foreach (var a in asyncEnumerable.WithCancellation(token))
                {
                    cts.Cancel();
                }
            }
            catch (OperationCanceledException e)
            {
                caught = true;
            }
            
            caught.ShouldBeTrue();
        }
        
        [Fact]
        public async Task RunAsAsyncEnumerable_must_return_an_IAsyncEnumerableT_from_a_Source()
        {
            var input = Enumerable.Range(1, 6).ToList();
            var asyncEnumerable = Source.From(input).RunAsAsyncEnumerable(Materializer);
            var output = input.ToArray();
            await foreach (var a in asyncEnumerable)
            {
                (output[0] == a).ShouldBeTrue("Did not get elements in order!");
                output = output.Skip(1).ToArray();
            }
            output.Length.ShouldBe(0,"Did not receive all elements!");
        }

        [Fact]
        public async Task RunAsAsyncEnumerable_must_allow_multiple_enumerations()
        {
            var input = Enumerable.Range(1, 6).ToList();
            var asyncEnumerable = Source.From(input).RunAsAsyncEnumerable(Materializer);
            var output = input.ToArray();
            await foreach (var a in asyncEnumerable)
            {
                (output[0] == a).ShouldBeTrue("Did not get elements in order!");
                output = output.Skip(1).ToArray();
            }
            output.Length.ShouldBe(0,"Did not receive all elements!");
            
            output = input.ToArray();
            await foreach (var a in asyncEnumerable)
            {
                (output[0] == a).ShouldBeTrue("Did not get elements in order!");
                output = output.Skip(1).ToArray();
            }
            output.Length.ShouldBe(0,"Did not receive all elements in second enumeration!!");
        }


        [Fact]
        public async Task RunAsAsyncEnumerable_Throws_on_Abrupt_Stream_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = this.CreatePublisherProbe<int>();
            var task = Source.FromPublisher(probe).RunAsAsyncEnumerable(materializer);
            
            var a = Task.Run( async () =>
            {
                await foreach (var notused in task)
                {
                    materializer.Shutdown();
                }
            });
            //since we are collapsing the stream inside the read
            //we want to send messages so we aren't just waiting forever.
            probe.SendNext(1);
            probe.SendNext(2);
            bool thrown = false;
            try
            {
                await a;
            }
            catch (StreamDetachedException e) 
            { 
                thrown = true; 
            } 
            catch (AbruptTerminationException e)
            {
                thrown = true;
            }
            thrown.ShouldBeTrue();
        }
        
        [Fact]
        public async Task RunAsAsyncEnumerable_Throws_if_materializer_gone_before_Enumeration()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = this.CreatePublisherProbe<int>();
            var task = Source.FromPublisher(probe).RunAsAsyncEnumerable(materializer);
            materializer.Shutdown();

            async Task ShouldThrow()
            {
                await foreach (var a in task)
                {
                    
                }
            }
            
            await Assert.ThrowsAsync<IllegalStateException>(ShouldThrow);
        }
        

#else
#endif
        [Fact]
        public async Task AsyncEnumerableSource_must_allow_multiple_IAsyncEnumerable()
        {
            var materializer = Sys.Materializer();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                // creating actor with default supervision, because stream supervisor default strategy is to 
                var actorRef = Sys.ActorOf(ManualSubscriber.Props(TestActor));
                Source.AsyncEnumerableAsRun(RangeAsync(1, 7))
                    .RunWith(Sink.FromSubscriber(new ActorSubscriberImpl<int>(actorRef)), materializer);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(1);
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(2);
                await ExpectNoMsgAsync(200);
                actorRef.Tell("boom");
                actorRef.Tell("ready");
                actorRef.Tell("ready");
                actorRef.Tell("boom");
                await foreach (var n in RangeAsync(3, 4))
                {
                    (await ExpectMsgAsync<OnNext>()).Element.Should().Be(n);
                }
                await ExpectNoMsgAsync(200);
                actorRef.Tell("ready");
                (await ExpectMsgAsync<OnNext>()).Element.Should().Be(7);
                await ExpectMsgAsync<OnComplete>();
            }, materializer);
        }
        
        [Fact]
        public void AsyncEnumerableSource_Must_Complete_Immediately_With_No_elements_When_An_Empty_IAsyncEnumerable_Is_Passed_In()
        {
            var p = Source.AsyncEnumerableAsRun(AsyncEnumerable.Empty<int>()).RunWith(Sink.AsPublisher<int>(false), Sys.Materializer());
            var c = this.CreateManualSubscriberProbe<int>();
            p.Subscribe(c);
            c.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void AsyncEnumerableSource_Select()
        {
            var materializer = Sys.Materializer();
            var p = Source.AsyncEnumerableAsRun(RangeAsync(1, 100))
                    .Select(message =>
                    {
                        return message;
                    });
            ///////////
        }
        static async IAsyncEnumerable<int> RangeAsync(int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(i);
                yield return start + i;
            }
        }
        
    }
}
