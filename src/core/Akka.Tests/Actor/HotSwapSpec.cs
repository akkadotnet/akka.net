//-----------------------------------------------------------------------
// <copyright file="HotSwapSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor {
    public class HotSwapSpec : AkkaSpec {

        [Fact]
        public void Must_be_able_to_become_in_its_constructor() {
            var a = Sys.ActorOf<ConstructorBecomer>();

            a.Tell("pigdog");
            ExpectMsg("pigdog");
        }

        [Fact]
        public void Must_be_able_to_become_multiple_times_in_its_constructor() {
            var a = Sys.ActorOf<MultipleConstructorBecomer>();

            a.Tell("pigdog");
            ExpectMsg("4:pigdog");
        }

        [Fact]
        public void Must_be_able_to_become_with_stacking_in_its_constructor() {
            var a = Sys.ActorOf<StackingConstructorBecomer>();

            a.Tell("pigdog");
            ExpectMsg("pigdog:pigdog");
            a.Tell("badass");
            ExpectMsg("badass:badass");
        }

        [Fact]
        public void Must_be_able_to_become_with_stacking_multiple_times_in_its_constructor() {
            var a = Sys.ActorOf<MultipleStackingConstructorBecomer>();

            a.Tell("pigdog");
            a.Tell("pigdog");
            a.Tell("pigdog");
            a.Tell("pigdog");
            ExpectMsg("4:pigdog");
            ExpectMsg("3:pigdog");
            ExpectMsg("2:pigdog");
            ExpectMsg("1:pigdog");
        }

        [Fact]
        public void Must_be_to_hotswap_its_behaviour_with_become() {

            var a = Sys.ActorOf<HotSwapWithBecome>();

            a.Tell("init");
            ExpectMsg("init");
            a.Tell("swap");
            a.Tell("swapped");
            ExpectMsg("swapped");
        }

        [Fact]
        public void Must_be_able_to_revert_hotswap_its_behaviour_with_unbecome() {
            var a = Sys.ActorOf<HotSwapRevertUnBecome>();

            a.Tell("init");
            ExpectMsg("init");
            a.Tell("swap");
            a.Tell("swapped");
            ExpectMsg("swapped");

            a.Tell("revert");
            a.Tell("init");
            ExpectMsg("init");
        }

        [Fact]
        public void Must_be_able_to_revert_to_initial_state_on_restart() {
            var a = Sys.ActorOf<RevertToInitialState>();

            a.Tell("state");
            ExpectMsg("0");

            a.Tell("swap");
            ExpectMsg("swapped");

            a.Tell("state");
            ExpectMsg("1");

            EventFilter.Exception<Exception>("Crash (expected)!").Expect(1, () => {
                a.Tell("crash");
            });

            a.Tell("state");
            ExpectMsg("0");

        }

        class ConstructorBecomer : ReceiveActor
        {

            public ConstructorBecomer()
            {
                Become(Echo);
                ReceiveAny(always => Sender.Tell("FAILURE"));
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
                ReceiveAny(always => Sender.Tell("FAILURE"));
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
                ReceiveAny(always => Sender.Tell("FAILURE"));
            }
        }

        class HotSwapWithBecome : ReceiveActor {
            public HotSwapWithBecome() {

                Receive<string>(m => "init".Equals(m), (m) => Sender.Tell("init"));
                Receive<string>(m => "swap".Equals(m), (m) => {
                    Become(() => Receive<string>(x => Sender.Tell(x)));
                });
            }
        }

        class HotSwapRevertUnBecome : ReceiveActor {
            public HotSwapRevertUnBecome() {
                
                Receive<string>(m => "init".Equals(m), (m) => Sender.Tell("init"));
                Receive<string>(m => "swap".Equals(m), (m) => {
                    BecomeStacked(() => {
                        Receive<string>(x => "swapped".Equals(x), x => Sender.Tell(x));
                        Receive<string>(x => "revert".Equals(x), x => Context.UnbecomeStacked());
                    });
                });
            }
        }

        class RevertToInitialState : ReceiveActor {
            public RevertToInitialState() {

                Receive<string>(m => "state".Equals(m), (m) => Sender.Tell("0"));
                Receive<string>(m => "swap".Equals(m), (m) => {
                    BecomeStacked(() => {
                        Receive<string>(x => "state".Equals(x), x => Sender.Tell("1"));
                        Receive<string>(x => "swapped".Equals(x), x => Sender.Tell("swapped"));
                        Receive<string>(x => "crash".Equals(x), x => {
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
