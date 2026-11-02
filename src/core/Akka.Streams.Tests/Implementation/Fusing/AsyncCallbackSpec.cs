// -----------------------------------------------------------------------
//  <copyright file="AsyncCallbackSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using FluentAssertions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Implementation.Fusing;

public class AsyncCallbackSpec: AkkaSpec
{
    private ActorMaterializer Materializer { get; }
    
    internal sealed class Started
    {
        public static readonly Started Instance = new();
        private Started() { }
    }

    internal sealed record Elem(int N);

    internal sealed record ThrowException(string Message);
    
    internal sealed class Stopped
    {
        public static readonly Stopped Instance = new();
        private Stopped() { }
    }
    
    internal sealed record Callbacks(Action<object> Callback, IAsyncCallback<object> CallbackAsync);
    
    internal sealed class AsyncCallbackGraphStage: GraphStageWithMaterializedValue<FlowShape<int, int>, Callbacks>
    {
        #region Logic

        internal sealed class Logic: InAndOutGraphStageLogic
        {
            private readonly AsyncCallbackGraphStage _stage;
            public readonly Action<object> AsyncCallback;
            public readonly IAsyncCallback<object> AsyncCallbackAsync;

            public Logic(AsyncCallbackGraphStage stage, Shape shape) : base(shape)
            {
                _stage = stage;
                AsyncCallback = GetAsyncCallback<object>(Callback);
                AsyncCallbackAsync = GetTypedAsyncCallback<object>(Callback);
                if (_stage._early.HasValue)
                    _stage._early.Value(AsyncCallbackAsync);
                SetHandlers(_stage.In, _stage.Out, this);
            }

            public override void PreStart()
            {
                base.PreStart();
                _stage._probe.Tell(Started.Instance);
            }

            public override void PostStop()
            {
                base.PostStop();
                _stage._probe.Tell(Stopped.Instance);
            }

            public override void OnPush()
            {
                var n = Grab(_stage.In);
                _stage._probe.Tell(new Elem(n));
                Push(_stage.Out, n);
            }

            public override void OnPull()
            {
                Pull(_stage.In);
            }
            
            private void Callback(object whatever)
            {
                switch (whatever)
                {
                    case ThrowException t:
                        throw new TestException(t.Message);
                    case "fail-the-stage":
                        FailStage(new Exception("failing the stage"));
                        break;
                    default:
                        _stage._probe.Tell(whatever);
                        break;
                }
            }
        }

        #endregion
        private readonly IActorRef _probe;
        private readonly Option<Action<IAsyncCallback<object>>> _early;

        public AsyncCallbackGraphStage(IActorRef probe, Option<Action<IAsyncCallback<object>>>? early = null)
        {
            Shape = new FlowShape<int, int>(In, Out);
            _probe = probe;
            _early = early ?? Option<Action<IAsyncCallback<object>>>.None;
        }
        
        public Inlet<int> In { get; } = new("In");
        public Outlet<int> Out { get; } = new("Out");
        public override FlowShape<int, int> Shape { get; }
        
        public override ILogicAndMaterializedValue<Callbacks> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this, Shape);
            return new LogicAndMaterializedValue<Callbacks>(logic, new Callbacks(logic.AsyncCallback, logic.AsyncCallbackAsync));
        }
    }

    public AsyncCallbackSpec(ITestOutputHelper output) : base(output, "akka.loglevel = DEBUG")
    {
        Materializer = ActorMaterializer.Create(Sys, ActorMaterializerSettings.Create(Sys).WithFuzzingMode(false));
    }

    [Fact(DisplayName = "The support for async callbacks must invoke without feedback, happy path")]
    public async Task WithoutFeedbackHappyPathTest()
    {
        var probe = CreateTestProbe();
        var upstream = this.CreatePublisherProbe<int>();
        var downstream = this.CreateSubscriberProbe<int>();
        var (callback, _) = Source.FromPublisher(upstream)
            .ViaMaterialized(new AsyncCallbackGraphStage(probe.Ref), Keep.Right)
            .To(Sink.FromSubscriber(downstream))
            .Run(Materializer);

        await downstream.EnsureSubscriptionAsync();
        
        await probe.ExpectMsgAsync<Started>();
        await downstream.RequestAsync(1);
        await upstream.ExpectRequestAsync();

        foreach (var n in Enumerable.Range(0, 10))
        {
            var msg = $"whatever{n}";
            callback(msg);
            await probe.ExpectMsgAsync(msg);
        }

        await upstream.SendCompleteAsync();
        await downstream.ExpectCompleteAsync();

        probe.ExpectMsg<Stopped>();
    }
    
    [Fact(DisplayName = "The support for async callbacks must invoke with feedback, happy path")]
    public async Task WithFeedbackHappyPathTest()
    {
        var probe = CreateTestProbe();
        var upstream = this.CreatePublisherProbe<int>();
        var downstream = this.CreateSubscriberProbe<int>();
        var (_, callback) = Source.FromPublisher(upstream)
            .ViaMaterialized(new AsyncCallbackGraphStage(probe.Ref), Keep.Right)
            .To(Sink.FromSubscriber(downstream))
            .Run(Materializer);

        await probe.ExpectMsgAsync<Started>();
        
        await downstream.EnsureSubscriptionAsync();
        await downstream.RequestAsync(1);
        await upstream.ExpectRequestAsync();

        foreach (var n in Enumerable.Range(0, 10))
        {
            var msg = $"whatever{n}";
            var feedback = callback.InvokeWithFeedback(msg);
            await probe.ExpectMsgAsync(msg);
            await feedback;
            feedback.IsCompleted.Should().BeTrue();
            feedback.Result.Should().Be(Done.Instance);
        }

        await upstream.SendCompleteAsync();
        await downstream.ExpectCompleteAsync();

        probe.ExpectMsg<Stopped>();
    }
    
    [Fact(DisplayName = "The support for async callbacks must fail the feedback if stage is stopped")]
    public async Task FailedFeedbackStageStoppedTest()
    {
        var probe = CreateTestProbe();
        var (_, callback) = Source.Empty<int>()
            .ViaMaterialized(new AsyncCallbackGraphStage(probe.Ref), Keep.Right)
            .To(Sink.Ignore<int>())
            .Run(Materializer);

        await probe.ExpectMsgAsync<Started>();
        await probe.ExpectMsgAsync<Stopped>();

        var feedback = callback.InvokeWithFeedback("whatever");

        Invoking(() => feedback.GetAwaiter().GetResult())
            .Should().Throw<StreamDetachedException>();
    }
    
    [Fact(DisplayName = "The support for async callbacks must invoke early")]
    public async Task InvokeEarlyTest()
    {
        var probe = CreateTestProbe();
        var upstream = this.CreatePublisherProbe<int>();
        var (callback, _) = Source.FromPublisher(upstream)
            .ViaMaterialized(new AsyncCallbackGraphStage(
                probe.Ref,
                Option<Action<IAsyncCallback<object>>>.Create(asyncCb => asyncCb.InvokeWithFeedback("early"))), Keep.Right)
            .To(Sink.Ignore<int>())
            .Run(Materializer);

        await Task.Delay(100);
        // and deliver in order
        callback("later");
        
        await probe.ExpectMsgAsync<Started>();
        await probe.ExpectMsgAsync("early");
        await probe.ExpectMsgAsync("later");

        await upstream.SendCompleteAsync();
        probe.ExpectMsg<Stopped>();
    }

    [Fact(DisplayName = "The support for async callbacks must invoke with feedback early")]
    public async Task InvokeFeedbackEarlyTest()
    {
        var probe = CreateTestProbe();
        var earlyFeedback = new TaskCompletionSource<Done>();
        var upstream = this.CreatePublisherProbe<int>();
        var (_, callback) = Source.FromPublisher(upstream)
            .ViaMaterialized(new AsyncCallbackGraphStage(
                probe.Ref,
                Option<Action<IAsyncCallback<object>>>.Create(asyncCb =>
                {
                    asyncCb.InvokeWithFeedback("early");
                    earlyFeedback.SetResult(Done.Instance);
                })
                ), Keep.Right)
            .To(Sink.Ignore<int>())
            .Run(Materializer);

        await Task.Delay(100);
        // and deliver in order
        var laterFeedback = callback.InvokeWithFeedback("later");
        
        await probe.ExpectMsgAsync<Started>();
        await probe.ExpectMsgAsync("early");
        earlyFeedback.Task.Result.Should().Be(Done.Instance);

        await probe.ExpectMsgAsync("later");
        laterFeedback.Result.Should().Be(Done.Instance);
        
        await upstream.SendCompleteAsync();
        probe.ExpectMsg<Stopped>();
    }
    
    [Fact(DisplayName = "The support for async callbacks must accept concurrent inputs")]
    public async Task ConcurrentInputTest()
    {
        var probe = CreateTestProbe();
        var upstream = this.CreatePublisherProbe<int>();
        var (_, callback) = Source.FromPublisher(upstream)
            .ViaMaterialized(new AsyncCallbackGraphStage(probe.Ref), Keep.Right)
            .To(Sink.Ignore<int>())
            .Run(Materializer);

        await probe.ExpectMsgAsync<Started>();
        
        var feedbacks = Enumerable.Range(1, 100)
            .Select(n => callback.InvokeWithFeedback(n.ToString()));
        
        var cbResults = await Task.WhenAll(feedbacks);
        cbResults.Length.Should().Be(100);
        Enumerable.Range(1, 100)
            .Select(_ => probe.ExpectMsg<string>())
            .ToHashSet().Count.Should().Be(100);

        await upstream.SendCompleteAsync();
        probe.ExpectMsg<Stopped>();
    }

    [Fact(DisplayName = "The support for async callbacks must fail the feedback if the handler throws")]
    public async Task FailingFeedbackHandlerThrowsTest()
    {
        var probe = CreateTestProbe();
        var upstream = this.CreatePublisherProbe<int>();
        var (_, callback) = Source.FromPublisher(upstream)
            .ViaMaterialized(new AsyncCallbackGraphStage(probe.Ref), Keep.Right)
            .To(Sink.Ignore<int>())
            .Run(Materializer);

        await probe.ExpectMsgAsync<Started>();
        (await callback.InvokeWithFeedback("happy-case")).Should().Be(Done.Instance);
        await probe.ExpectMsgAsync("happy-case");

        var feedback = callback.InvokeWithFeedback(new ThrowException("oh my gosh, whale of a wash!"));
        await Awaiting(async () => await feedback)
            .Should().ThrowAsync<TestException>()
            .WithMessage("oh my gosh, whale of a wash!");

        await upstream.ExpectCancellationAsync();
    }

    [Fact(DisplayName = "The support for async callbacks must fail the feedback if the handler fails the stage")]
    public async Task FailingFeedbackHandlerFailsStageTest()
    {
        var probe = CreateTestProbe();
        var (_, callback) = Source.Empty<int>()
            .ViaMaterialized(new AsyncCallbackGraphStage(probe.Ref), Keep.Right)
            .To(Sink.Ignore<int>())
            .Run(Materializer);

        await probe.ExpectMsgAsync<Started>();
        await probe.ExpectMsgAsync<Stopped>();
        
        var feedback = callback.InvokeWithFeedback("fail-the-stage");
        await Awaiting(async () => await feedback)
            .Should().ThrowAsync<StreamDetachedException>();
    }
}