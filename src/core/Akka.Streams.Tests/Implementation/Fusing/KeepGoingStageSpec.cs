//-----------------------------------------------------------------------
// <copyright file="KeepGoingStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable MemberHidesStaticFromOuterClass

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class KeepGoingStageSpec : AkkaSpec
    {
        private interface IPingCmd { }

        private sealed class Register : IPingCmd
        {
            public Register(IActorRef probe)
            {
                Probe = probe;
            }

            public IActorRef Probe { get; }
        }

        private sealed class Ping : IPingCmd
        {
            public static Ping Instance { get; } = new();

            private Ping() { }
        }

        private sealed class CompleteStage : IPingCmd
        {
            public static CompleteStage Instance { get; } = new();

            private CompleteStage() { }
        }

        private sealed class FailStage : IPingCmd
        {
            public static FailStage Instance { get; } = new();

            private FailStage() { }
        }

        private sealed class Throw : IPingCmd
        {
            public static Throw Instance { get; } = new();

            private Throw() { }
        }

        private interface IPingEvt { }

        private sealed class Pong : IPingEvt
        {
            public static Pong Instance { get; } = new();

            private Pong() { }
        }

        private sealed class PostStop : IPingEvt
        {
            public static PostStop Instance { get; } = new();

            private PostStop() { }
        }

        private sealed class UpstreamCompleted : IPingEvt
        {
            public static UpstreamCompleted Instance { get; } = new();

            private UpstreamCompleted() { }
        }

        private sealed class EndOfEventHandler : IPingEvt
        {
            public static EndOfEventHandler Instance { get; } = new();

            private EndOfEventHandler() { }
        }

        private sealed class PingRef
        {
            private readonly Action<IPingCmd> _cb;

            public PingRef(Action<IPingCmd> cb)
            {
                _cb = cb;
            }

            public void Register(IActorRef probe) => _cb(new Register(probe));

            public void Ping() => _cb(KeepGoingStageSpec.Ping.Instance);

            public void Stop() => _cb(CompleteStage.Instance);

            public void Fail() => _cb(FailStage.Instance);

            public void ThrowEx() => _cb(Throw.Instance);
        }

        private sealed class PingableSink : GraphStageWithMaterializedValue<SinkShape<int>, Task<PingRef>>
        {
            private readonly bool _keepAlive;

            #region internal classes

            private sealed class PingableLogic : GraphStageLogic
            {
                private readonly PingableSink _pingable;
                private IActorRef _listener = Nobody.Instance;

                public PingableLogic(PingableSink pingable) : base(pingable.Shape)
                {
                    _pingable = pingable;

                    SetHandler(_pingable.Shape.Inlet, 
                        () => Pull(_pingable.Shape.Inlet),
                        //Ignore finish
                        () => { _listener.Tell(UpstreamCompleted.Instance); });
                }

                public override void PreStart()
                {
                    SetKeepGoing(_pingable._keepAlive);
                    _pingable._promise.TrySetResult(new PingRef(GetAsyncCallback<IPingCmd>(OnCommand)));
                }

                public override void PostStop() => _listener.Tell(KeepGoingStageSpec.PostStop.Instance);

                private void OnCommand(IPingCmd cmd)
                {
                    switch (cmd)
                    {
                        case Register r:
                            _listener = r.Probe;
                            break;
                        
                        case Ping _:
                            _listener.Tell(Pong.Instance);
                            break;
                        
                        case CompleteStage _:
                            CompleteStage();
                            _listener.Tell(EndOfEventHandler.Instance);
                            break;
                        
                        case FailStage _:
                            FailStage(new TestException("test"));
                            _listener.Tell(EndOfEventHandler.Instance);
                            break;
                        
                        case Throw _:
                            try
                            {
                                throw new TestException("test");
                            }
                            finally
                            {
                                _listener.Tell(EndOfEventHandler.Instance);
                            }
                    }
                }
            }

            #endregion

            private readonly TaskCompletionSource<PingRef> _promise = new();

            public PingableSink(bool keepAlive)
            {
                _keepAlive = keepAlive;
            }

            public override SinkShape<int> Shape { get; } = new(new Inlet<int>("ping.in"));

            public override ILogicAndMaterializedValue<Task<PingRef>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            {
                return new LogicAndMaterializedValue<Task<PingRef>>(new PingableLogic(this), _promise.Task);
            }
        }

        private ActorMaterializer Materializer { get; }

        public KeepGoingStageSpec(ITestOutputHelper helper = null) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task A_stage_with_keep_going_must_still_be_alive_after_all_ports_have_been_closed_until_explicity_closed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var t = Source.Maybe<int>().ToMaterialized(new PingableSink(true), Keep.Both).Run(Materializer);
                var maybePromise = t.Item1;
                var pingerFuture = t.Item2;
                pingerFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var pinger = await pingerFuture;

                pinger.Register(TestActor);

                //Before completion
                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                maybePromise.TrySetResult(0);
                await ExpectMsgAsync<UpstreamCompleted>();

                await ExpectNoMsgAsync(200);

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Stop();
                // PostStop should not be concurrent with the event handler. This event here tests this.
                await ExpectMsgAsync<EndOfEventHandler>();
                await ExpectMsgAsync<PostStop>();
            }, Materializer);
        }

        [Fact]
        public async Task A_stage_with_keep_going_must_still_be_alive_after_all_ports_have_been_closed_until_explicitly_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var t = Source.Maybe<int>().ToMaterialized(new PingableSink(true), Keep.Both).Run(Materializer);
                var maybePromise = t.Item1;
                var pingerFuture = t.Item2;
                pingerFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var pinger = await pingerFuture;

                pinger.Register(TestActor);

                //Before completion
                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                maybePromise.TrySetResult(0);
                await ExpectMsgAsync<UpstreamCompleted>();

                await ExpectNoMsgAsync(200);

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Fail();
                // PostStop should not be concurrent with the event handler. This event here tests this.
                await ExpectMsgAsync<EndOfEventHandler>();
                await ExpectMsgAsync<PostStop>();
            }, Materializer);
        }

        [Fact]
        public async Task A_stage_with_keep_going_must_still_be_alive_after_all_ports_have_been_closed_until_implicity_failed_via_exception()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var t = Source.Maybe<int>().ToMaterialized(new PingableSink(true), Keep.Both).Run(Materializer);
                var maybePromise = t.Item1;
                var pingerFuture = t.Item2;
                pingerFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var pinger = await pingerFuture;

                pinger.Register(TestActor);

                //Before completion
                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                maybePromise.TrySetResult(0);
                await ExpectMsgAsync<UpstreamCompleted>();

                ExpectNoMsg(200);

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                // We need to catch the exception otherwise the test fails
                // ReSharper disable once EmptyGeneralCatchClause
                try { pinger.ThrowEx(); } catch { }
                // PostStop should not be concurrent with the event handler. This event here tests this.
                await ExpectMsgAsync<EndOfEventHandler>();
                await ExpectMsgAsync<PostStop>();
            }, Materializer);
        }

        [Fact]
        public async Task A_stage_with_keep_going_must_close_down_earls_if_keepAlive_is_not_requested()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var t = Source.Maybe<int>().ToMaterialized(new PingableSink(false), Keep.Both).Run(Materializer);
                var maybePromise = t.Item1;
                var pingerFuture = t.Item2;
                pingerFuture.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var pinger = await pingerFuture;

                pinger.Register(TestActor);

                //Before completion
                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                pinger.Ping();
                await ExpectMsgAsync<Pong>();

                maybePromise.TrySetResult(0);
                await ExpectMsgAsync<UpstreamCompleted>();
                await ExpectMsgAsync<PostStop>();
            }, Materializer);
        }
    }
}
