//-----------------------------------------------------------------------
// <copyright file="CoordinatedShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using FluentAssertions;
using Xunit;
using static Akka.Actor.CoordinatedShutdown;

namespace Akka.Tests.Actor
{
    public class CoordinatedShutdownSpec : AkkaSpec
    {
        public ExtendedActorSystem ExtSys => Sys.AsInstanceOf<ExtendedActorSystem>();

        private Phase Phase(params string[] dependsOn)
        {
            return new Phase(dependsOn?.ToImmutableHashSet() ?? ImmutableHashSet<string>.Empty, TimeSpan.FromSeconds(10), true);
        }

        private static readonly Phase EmptyPhase = new Phase(ImmutableHashSet<string>.Empty, TimeSpan.FromSeconds(10), true);

        private List<string> CheckTopologicalSort(Dictionary<string, Phase> phases)
        {
            var result = CoordinatedShutdown.TopologicalSort(phases);
            result.ZipWithIndex().ForEach(pair =>
            {
                if (!phases.ContainsKey(pair.Key))
                    return;

                var i = pair.Value;
                var p = phases[pair.Key];
                p.DependsOn.ForEach(depPhase =>
                {
                    i.Should().BeGreaterThan(result.IndexOf(depPhase),
                        $"phase [{p}] depends on [{depPhase}] but was ordered before it in topological sort result {string.Join("->", result)}");
                });
            });
            return result;
        }

        private class CustomReason : CoordinatedShutdown.Reason
        {
        }

        private static CoordinatedShutdown.Reason customReason = new CustomReason();


        [Fact]
        public void CoordinatedShutdown_must_sort_phases_in_topological_order()
        {
            CheckTopologicalSort(new Dictionary<string, Phase>()).Count.Should().Be(0);

            CheckTopologicalSort(new Dictionary<string, Phase>() { { "a", EmptyPhase } })
                .Should()
                .Equal(new List<string>() { "a" });

            CheckTopologicalSort(new Dictionary<string, Phase>() { { "b", Phase("a") } })
                .Should()
                .Equal(new List<string>() { "a", "b" });

            var result1 = CheckTopologicalSort(new Dictionary<string, Phase>() { { "c", Phase("a") }, { "b", Phase("a") } });
            result1.First().Should().Be("a");
            // b,c can be in any order
            result1.ToImmutableHashSet()
                .SetEquals(new HashSet<string>(new[] { "a", "b", "c" }))
                .ShouldBeTrue();

            CheckTopologicalSort(new Dictionary<string, Phase>() { { "b", Phase("a") }, { "c", Phase("b") } })
                .Should()
                .Equal(new List<string>() { "a", "b", "c" });

            CheckTopologicalSort(new Dictionary<string, Phase>() { { "b", Phase("a") }, { "c", Phase("a", "b") } })
                .Should()
                .Equal(new List<string>() { "a", "b", "c" });

            var result2 = CheckTopologicalSort(new Dictionary<string, Phase>() { { "c", Phase("a", "b") } });
            result2.Last().Should().Be("c");
            // a, b can be in any order
            result2.ToImmutableHashSet().SetEquals(new[] { "a", "b", "c" }).Should().BeTrue();

            CheckTopologicalSort(new Dictionary<string, Phase>()
            {
                {"b", Phase("a")},
                {"c", Phase("b")},
                {"d", Phase("b", "c")},
                {"e", Phase("d")}
            }).Should().Equal(new List<string>() { "a", "b", "c", "d", "e" });

            var result3 = CheckTopologicalSort(new Dictionary<string, Phase>()
            {
                {"a2", Phase("a1")},
                {"a3", Phase("a2")},
                {"b2", Phase("b1")},
                {"b3", Phase("b2")},
            });
            var a = result3.TakeWhile(x => x.First() == 'a');
            var b = result3.SkipWhile(x => x.First() == 'a');
            a.Should().Equal(new List<string>() { "a1", "a2", "a3" });
            b.Should().Equal(new List<string>() { "b1", "b2", "b3" });
        }

        [Fact]
        public void CoordinatedShutdown_must_detect_cycles_in_phases_non_DAG()
        {
            Intercept<ArgumentException>(() =>
            {
                CoordinatedShutdown.TopologicalSort(new Dictionary<string, Phase>() { { "a", Phase("a") } });
            });

            Intercept<ArgumentException>(() =>
            {
                CoordinatedShutdown.TopologicalSort(new Dictionary<string, Phase>()
                {
                    { "b", Phase("a") },
                    { "a", Phase("b") },
                });
            });

            Intercept<ArgumentException>(() =>
            {
                CoordinatedShutdown.TopologicalSort(new Dictionary<string, Phase>()
                {
                    { "c", Phase("a") },
                    { "c", Phase("b") },
                    { "b", Phase("c") },
                });
            });

            Intercept<ArgumentException>(() =>
            {
                CoordinatedShutdown.TopologicalSort(new Dictionary<string, Phase>()
                {
                    { "d", Phase("a") },
                    { "d", Phase("c") },
                    { "c", Phase("b") },
                    { "b", Phase("d") },
                });
            });
        }

        [Fact]
        public void CoordinatedShutdown_must_predefined_phases_from_config()
        {
            CoordinatedShutdown.Get(Sys).OrderedPhases.Should().Equal(new[]
            {
                PhaseBeforeServiceUnbind,
                PhaseServiceUnbind,
                PhaseServiceRequestsDone,
                PhaseServiceStop,
                PhaseBeforeClusterShutdown,
                PhaseClusterShardingShutdownRegion,
                PhaseClusterLeave,
                PhaseClusterExiting,
                PhaseClusterExitingDone,
                PhaseClusterShutdown,
                PhaseBeforeActorSystemTerminate,
                PhaseActorSystemTerminate
            });
        }

        [Fact]
        public void CoordinatedShutdown_must_run_ordered_phases()
        {
            var phases = new Dictionary<string, Phase>()
            {
                { "a", EmptyPhase },
                { "b", Phase("a") },
                { "c", Phase("b", "a") }
            };

            var co = new CoordinatedShutdown(ExtSys, phases);
            co.AddTask("a", "a1", () =>
            {
                TestActor.Tell("A");
                return TaskEx.Completed;
            });

            co.AddTask("b", "b1", () =>
            {
                TestActor.Tell("B");
                return TaskEx.Completed;
            });

            co.AddTask("b", "b2", () =>
            {
                // to verify that c is not performed before b
                Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();
                TestActor.Tell("B");
                return TaskEx.Completed;
            });

            co.AddTask("c", "c1", () =>
            {
                TestActor.Tell("C");
                return TaskEx.Completed;
            });

            co.Run(CoordinatedShutdown.UnknownReason.Instance).Wait(RemainingOrDefault);
            ReceiveN(4).Should().Equal(new object[] { "A", "B", "B", "C" });
        }

        [Fact]
        public void CoordinatedShutdown_must_run_from_given_phase()
        {
            var phases = new Dictionary<string, Phase>()
            {
                { "a", EmptyPhase },
                { "b", Phase("a") },
                { "c", Phase("b", "a") }
            };

            var co = new CoordinatedShutdown(ExtSys, phases);
            co.AddTask("a", "a1", () =>
            {
                TestActor.Tell("A");
                return TaskEx.Completed;
            });

            co.AddTask("b", "b1", () =>
            {
                TestActor.Tell("B");
                return TaskEx.Completed;
            });

            co.AddTask("c", "c1", () =>
            {
                TestActor.Tell("C");
                return TaskEx.Completed;
            });

            co.Run(customReason, "b").Wait(RemainingOrDefault);
            ReceiveN(2).Should().Equal(new object[] { "B", "C" });
            co.ShutdownReason.ShouldBeEquivalentTo(customReason);
        }

        [Fact]
        public void CoordinatedShutdown_must_only_run_once()
        {
            var phases = new Dictionary<string, Phase>()
            {
                { "a", EmptyPhase }
            };

            var co = new CoordinatedShutdown(ExtSys, phases);
            co.AddTask("a", "a1", () =>
            {
                TestActor.Tell("A");
                return TaskEx.Completed;
            });

            co.ShutdownReason.Should().BeNull();
            co.Run(customReason).Wait(RemainingOrDefault);
            co.ShutdownReason.ShouldBeEquivalentTo(customReason);
            ExpectMsg("A");
            co.Run(CoordinatedShutdown.UnknownReason.Instance).Wait(RemainingOrDefault);
            TestActor.Tell("done");
            ExpectMsg("done"); // no additional A
            co.ShutdownReason.ShouldBeEquivalentTo(customReason);
        }

        [Fact]
        public void CoordinatedShutdown_must_continue_after_timeout_or_failure()
        {
            var phases = new Dictionary<string, Phase>()
            {
                { "a", EmptyPhase },
                { "b",  new Phase(ImmutableHashSet<string>.Empty.Add("a"), TimeSpan.FromMilliseconds(100), true)},
                { "c", Phase("b", "a") }
            };

            var co = new CoordinatedShutdown(ExtSys, phases);
            co.AddTask("a", "a1", () =>
            {
                TestActor.Tell("A");
                return TaskEx.FromException<Done>(new Exception("boom"));
            });

            co.AddTask("a", "a2", () =>
            {
                Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();
                TestActor.Tell("A");
                return TaskEx.Completed;
            });

            co.AddTask("b", "b1", () =>
            {
                TestActor.Tell("B");
                return new TaskCompletionSource<Done>().Task; // never completed
            });

            co.AddTask("c", "c1", () =>
            {
                TestActor.Tell("C");
                return TaskEx.Completed;
            });

            co.Run(CoordinatedShutdown.UnknownReason.Instance).Wait(RemainingOrDefault);
            ExpectMsg("A");
            ExpectMsg("A");
            ExpectMsg("B");
            ExpectMsg("C");
        }

        [Fact]
        public void CoordinatedShutdown_must_abort_if_recover_is_off()
        {
            var phases = new Dictionary<string, Phase>()
            {
                { "b",  new Phase(ImmutableHashSet<string>.Empty.Add("a"), TimeSpan.FromMilliseconds(100), false)},
                { "c", Phase("b", "a") }
            };

            var co = new CoordinatedShutdown(ExtSys, phases);
            co.AddTask("b", "b1", () =>
            {
                TestActor.Tell("B");
                return new TaskCompletionSource<Done>().Task; // never completed
            });

            co.AddTask("c", "c1", () =>
            {
                TestActor.Tell("C");
                return TaskEx.Completed;
            });

            var result = co.Run(CoordinatedShutdown.UnknownReason.Instance);
            ExpectMsg("B");
            Intercept<AggregateException>(() =>
            {
                if (result.Wait(RemainingOrDefault))
                {
                    result.Exception?.Flatten().InnerException.Should().BeOfType<TimeoutException>();
                }
                else
                {
                    throw new Exception("CoordinatedShutdown task did not complete");
                }
            });

            ExpectNoMsg(TimeSpan.FromMilliseconds(200)); // C not run
        }

        [Fact]
        public void CoordinatedShutdown_must_be_possible_to_add_tasks_in_later_phase_from_earlier_phase()
        {
            var phases = new Dictionary<string, Phase>()
            {
                { "a", EmptyPhase },
                {"b", Phase("a") }
            };

            var co = new CoordinatedShutdown(ExtSys, phases);
            co.AddTask("a", "a1", () =>
            {
                TestActor.Tell("A");
                co.AddTask("b", "b1", () =>
                {
                    TestActor.Tell("B");
                    return TaskEx.Completed;
                });
                return TaskEx.Completed;
            });

            co.Run(CoordinatedShutdown.UnknownReason.Instance).Wait(RemainingOrDefault);
            ExpectMsg("A");
            ExpectMsg("B");
        }

        [Fact]
        public void CoordinatedShutdown_must_be_possible_to_parse_phases_from_config()
        {
            CoordinatedShutdown.PhasesFromConfig(ConfigurationFactory.ParseString(@"
            default-phase-timeout = 10s
            phases {
              a = {}
              b {
                depends-on = [a]
                timeout = 15s
              }
              c {
                depends-on = [a, b]
                recover = off
              }
            }")).Should()
                .Equal(new Dictionary<string, Phase>()
                {
                    { "a", new Phase(ImmutableHashSet<string>.Empty, TimeSpan.FromSeconds(10), true)},
                    { "b", new Phase(ImmutableHashSet<string>.Empty.Add("a"), TimeSpan.FromSeconds(15), true)},
                    { "c", new Phase(ImmutableHashSet<string>.Empty.Add("a").Add("b"), TimeSpan.FromSeconds(10), false)},
                });
        }

        [Fact]
        public void CoordinatedShutdown_must_terminate_ActorSystem()
        {
            var shutdownSystem = CoordinatedShutdown.Get(Sys).Run(customReason);
            shutdownSystem.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

            Sys.WhenTerminated.IsCompleted.Should().BeTrue();
            CoordinatedShutdown.Get(Sys).ShutdownReason.ShouldBeEquivalentTo(customReason);
        }

        [Fact]
        public async Task CoordinatedShutdown_must_be_run_by_ActorSystem_Terminate()
        {
            await Sys.Terminate();
            Sys.WhenTerminated.IsCompleted.Should().BeTrue();
            CoordinatedShutdown.Get(Sys).ShutdownReason.ShouldBeEquivalentTo(CoordinatedShutdown.ActorSystemTerminateReason.Instance);
        }

        [Fact]
        public async Task CoordinatedShutdown_must_not_be_run_by_ActorSystem_Terminate_when_run_by_actor_system_terminate_is_off()
        {
            var sys = ActorSystem.Create(
                "name", 
                ConfigurationFactory
                    .ParseString(@"
                        akka.coordinated-shutdown.terminate-actor-system = on
                        akka.coordinated-shutdown.run-by-actor-system-terminate = off")
                    .WithFallback(Sys.Settings.Config));
            var actor = CoordinatedShutdown.Get(sys);

            try
            {
                await sys.Terminate();
                sys.WhenTerminated.IsCompleted.Should().BeTrue();
                actor.ShutdownReason.ShouldBeEquivalentTo(null);
            }
            finally
            {
                Shutdown(sys);
            }
        }

        [Fact]
        public void CoordinatedShutdown_must_not_allow_terminate_actor_system_set_to_off_and_run_by_actor_system_terminate_set_to_on()
        {
            Action act = () => {
                ActorSystem sys = null;
                try
                {
                    sys = ActorSystem.Create(
                        "name",
                            ConfigurationFactory
                            .ParseString(@"
                                akka.coordinated-shutdown.terminate-actor-system = off
                                akka.coordinated-shutdown.run-by-actor-system-terminate = on")
                            .WithFallback(Sys.Settings.Config));
                    var actor = CoordinatedShutdown.Get(sys);
                }
                finally
                {
                    if (sys != null)
                        Shutdown(sys);
                }
            };

            act.Invoking(a => a()).ShouldThrow<ConfigurationException>();
        }

    }
}
