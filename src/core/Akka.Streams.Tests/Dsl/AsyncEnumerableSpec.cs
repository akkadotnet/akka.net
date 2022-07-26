﻿//-----------------------------------------------------------------------
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
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Akka.Util;
using FluentAssertions.Extensions;

namespace Akka.Streams.Tests.Dsl
{
#if NETCOREAPP
    public class AsyncEnumerableSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }
        private ITestOutputHelper _helper;

        public AsyncEnumerableSpec(ITestOutputHelper helper) : base(
            AkkaSpecConfig.WithFallback(StreamTestDefaultMailbox.DefaultConfig),
            helper)
        {
            _helper = helper;
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }


        [Fact]
        public async Task RunAsAsyncEnumerable_Uses_CancellationToken()
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

            output.Length.ShouldBe(0, "Did not receive all elements!");
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

            output.Length.ShouldBe(0, "Did not receive all elements!");

            output = input.ToArray();
            await foreach (var a in asyncEnumerable)
            {
                (output[0] == a).ShouldBeTrue("Did not get elements in order!");
                output = output.Skip(1).ToArray();
            }

            output.Length.ShouldBe(0, "Did not receive all elements in second enumeration!!");
        }


        [Fact]
        public async Task RunAsAsyncEnumerable_Throws_on_Abrupt_Stream_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = this.CreatePublisherProbe<int>();
            var task = Source.FromPublisher(probe).RunAsAsyncEnumerable(materializer);

            var a = Task.Run(async () =>
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
            var thrown = false;
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

        [Fact]
        public async Task
            AsyncEnumerableSource_Must_Complete_Immediately_With_No_elements_When_An_Empty_IAsyncEnumerable_Is_Passed_In()
        {
            IAsyncEnumerable<int> Range() => RangeAsync(0, 0);
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.From(Range)
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);

            var subscription = await subscriber.ExpectSubscriptionAsync();
            subscription.Request(100);
            await subscriber.ExpectCompleteAsync();
        }

        [Fact]
        public async Task AsyncEnumerableSource_Must_Process_All_Elements()
        {
            IAsyncEnumerable<int> Range() => RangeAsync(0, 100);
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.From(Range)
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);

            var subscription = await subscriber.ExpectSubscriptionAsync();
            subscription.Request(101);

            await subscriber.ExpectNextNAsync(Enumerable.Range(0, 100));
            
            await subscriber.ExpectCompleteAsync();
        }

        [Fact]
        public async Task AsyncEnumerableSource_Must_Process_Source_That_Immediately_Throws()
        {
            IAsyncEnumerable<int> Range() => ThrowingRangeAsync(0, 100, 50);
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.From(Range)
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);

            var subscription = await subscriber.ExpectSubscriptionAsync();
            subscription.Request(101);

            await subscriber.ExpectNextNAsync(Enumerable.Range(0, 50));

            var exception = await subscriber.ExpectErrorAsync();
            
            // Exception should be automatically unrolled, this SHOULD NOT be AggregateException
            exception.Should().BeOfType<TestException>();
            exception.Message.Should().Be("BOOM!");
        }

        [Fact]
        public async Task AsyncEnumerableSource_Must_Cancel_Running_Source_If_Downstream_Completes()
        {
            var latch = new AtomicBoolean();
            IAsyncEnumerable<int> Range() => ProbeableRangeAsync(0, 100, latch);
            var subscriber = this.CreateManualSubscriberProbe<int>();

            Source.From(Range)
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);

            var subscription = await subscriber.ExpectSubscriptionAsync();
            subscription.Request(50);
            await subscriber.ExpectNextNAsync(Enumerable.Range(0, 50));
            subscription.Cancel();

            // The cancellation token inside the IAsyncEnumerable should be cancelled
            await WithinAsync(3.Seconds(), async () => latch.Value);
        }

        private static async IAsyncEnumerable<int> RangeAsync(int start, int count, 
            [EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (var i in Enumerable.Range(start, count))
            {
                await Task.Delay(10, token);
                if(token.IsCancellationRequested)
                    yield break;
                yield return i;
            }
        }
        
        private static async IAsyncEnumerable<int> ThrowingRangeAsync(int start, int count, int throwAt,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (var i in Enumerable.Range(start, count))
            {
                if(token.IsCancellationRequested)
                    yield break;

                if (i == throwAt)
                    throw new TestException("BOOM!");
                
                yield return i;
            }
        }
        
        private static async IAsyncEnumerable<int> ProbeableRangeAsync(int start, int count, AtomicBoolean latch,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            token.Register(() =>
            {
                latch.GetAndSet(true);
            });
            foreach (var i in Enumerable.Range(start, count))
            {
                if(token.IsCancellationRequested)
                    yield break;

                yield return i;
            }
        }
        
    }
#else
#endif
}