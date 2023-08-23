//-----------------------------------------------------------------------
// <copyright file="HotSwapSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor {
    public class HotSwapSpec : AkkaSpec {

        [Fact]
        public async Task Must_be_able_to_become_in_its_constructor() 
        {
            var a = Sys.ActorOf<ConstructorBecomer>();

            a.Tell("pigdog");
            await ExpectMsgAsync("pigdog");
        }

        [Fact]
        public async Task Must_be_able_to_become_multiple_times_in_its_constructor() {
            var a = Sys.ActorOf<MultipleConstructorBecomer>();

            a.Tell("pigdog");
            await ExpectMsgAsync("4:pigdog");
        }

        [Fact]
        public async Task Must_be_able_to_become_with_stacking_in_its_constructor() {
            var a = Sys.ActorOf<StackingConstructorBecomer>();

            a.Tell("pigdog");
            await ExpectMsgAsync("pigdog:pigdog");
            a.Tell("badass");
            await ExpectMsgAsync("badass:badass");
        }

        [Fact]
        public async Task Must_be_able_to_become_with_stacking_multiple_times_in_its_constructor() {
            var a = Sys.ActorOf<MultipleStackingConstructorBecomer>();

            a.Tell("pigdog");
            a.Tell("pigdog");
            a.Tell("pigdog");
            a.Tell("pigdog");
            await ExpectMsgAsync("4:pigdog");
            await ExpectMsgAsync("3:pigdog");
            await ExpectMsgAsync("2:pigdog");
            await ExpectMsgAsync("1:pigdog");
        }

        [Fact]
        public async Task Must_be_to_hotswap_its_behaviour_with_become() {

            var a = Sys.ActorOf<HotSwapWithBecome>();

            a.Tell("init");
            await ExpectMsgAsync("init");
            a.Tell("swap");
            a.Tell("swapped");
            await ExpectMsgAsync("swapped");
        }

        [Fact]
        public async Task Must_be_able_to_revert_hotswap_its_behaviour_with_unbecome() {
            var a = Sys.ActorOf<HotSwapRevertUnBecome>();

            a.Tell("init");
            await ExpectMsgAsync("init");
            a.Tell("swap");
            a.Tell("swapped");
            await ExpectMsgAsync("swapped");

            a.Tell("revert");
            a.Tell("init");
            await ExpectMsgAsync("init");
        }

        [Fact]
        public async Task Must_be_able_to_revert_to_initial_state_on_restart() {
            var a = Sys.ActorOf<RevertToInitialState>();

            a.Tell("state");
            await ExpectMsgAsync("0");

            a.Tell("swap");
            await ExpectMsgAsync("swapped");

            a.Tell("state");
            await ExpectMsgAsync("1");

            await EventFilter.Exception<Exception>("Crash (expected)!").ExpectAsync(1, () =>
            {
                a.Tell("crash");
                return Task.CompletedTask;
            });

            a.Tell("state");
            await ExpectMsgAsync("0");

        }

        class ConstructorBecomer : ReceiveActor
        {

            public ConstructorBecomer()
            {
                Become(Echo);
                ReceiveAny(_ => Sender.Tell("FAILURE"));
            }

            private void Echo()
            {
                ReceiveAny(always => Sender.Tell(always));
            }
        }

        class MultipleConstructorBecomer : ReceiveActor
        {
            public MultipleConstructorBecomer()
            {
                for (int i = 1; i <= 4; i++)
                {
                    var i1 = i;
                    Become(() => ReceiveAny(m => Sender.Tell(i1 + ":" + m)));
                }
                ReceiveAny(_ => Sender.Tell("FAILURE"));
            }
        }

        class StackingConstructorBecomer : ReceiveActor
        {
            public StackingConstructorBecomer()
            {

                BecomeStacked(() => {
                    ReceiveAny(m => {
                        Sender.Tell("pigdog:" + m);
                        UnbecomeStacked();
                    });
                });

                ReceiveAny(always => Sender.Tell("badass:" + always));
            }
        }

        class MultipleStackingConstructorBecomer : ReceiveActor
        {
            public MultipleStackingConstructorBecomer()
            {

                for (int i = 1; i <= 4; i++)
                {
                    var i1 = i;
                    BecomeStacked(() => {
                        ReceiveAny(m => {
                            Sender.Tell(i1 + ":" + m);
                            UnbecomeStacked();
                        });
                    });

                }
                ReceiveAny(_ => Sender.Tell("FAILURE"));
            }
        }

        class HotSwapWithBecome : ReceiveActor {
            public HotSwapWithBecome() {

                Receive<string>(m => "init".Equals(m), (_) => Sender.Tell("init"));
                Receive<string>(m => "swap".Equals(m), (_) => {
                    Become(() => Receive<string>(x => Sender.Tell(x)));
                });
            }
        }

        class HotSwapRevertUnBecome : ReceiveActor {
            public HotSwapRevertUnBecome() {
                
                Receive<string>(m => "init".Equals(m), (_) => Sender.Tell("init"));
                Receive<string>(m => "swap".Equals(m), (_) => {
                    BecomeStacked(() => {
                        Receive<string>(x => "swapped".Equals(x), x => Sender.Tell(x));
                        Receive<string>(x => "revert".Equals(x), _ => Context.UnbecomeStacked());
                    });
                });
            }
        }

        class RevertToInitialState : ReceiveActor {
            public RevertToInitialState() {

                Receive<string>(m => "state".Equals(m), (_) => Sender.Tell("0"));
                Receive<string>(m => "swap".Equals(m), (_) => {
                    BecomeStacked(() => {
                        Receive<string>(x => "state".Equals(x), _ => Sender.Tell("1"));
                        Receive<string>(x => "swapped".Equals(x), _ => Sender.Tell("swapped"));
                        Receive<string>(x => "crash".Equals(x), _ => {
                            Crash();
                        });
                    });
                    Sender.Tell("swapped");
                });
            }

            private void Crash() {
                throw new Exception("Crash (expected)!");
            }
        }
    }
}
