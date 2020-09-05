//-----------------------------------------------------------------------
// <copyright file="JournalPerfSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TestKit.Performance
{
    /// <summary>
    /// This spec measures execution times of the basic operations that an <see cref="Akka.Persistence.PersistentActor"/> provides,
    /// using the provided Journal (plugin).
    /// 
    /// It is *NOT* meant to be a comprehensive benchmark, but rather aims to help plugin developers to easily determine
    /// if their plugin's performance is roughly as expected. It also validates the plugin still works under "more messages" scenarios.
    /// 
    /// In case your journal plugin needs some kind of teardown, override the `AfterAll` method (don't forget to call `base` in your overridden methods).
    /// </summary>
    public abstract class JournalPerfSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly TestProbe testProbe;

        /// <summary>
        /// Number of messages sent to the PersistentActor under test for each test iteration
        /// </summary>
        protected int EventsCount = 10 * 1000;

        /// <summary>
        /// Number of measurement iterations each test will be run.
        /// </summary>
        protected int MeasurementIterations = 10;
        
        /// <summary>
        /// Override in order to customize timeouts used for ExpectMsg, in order to tune the awaits to your journal's perf
        /// </summary>
        protected TimeSpan ExpectDuration = TimeSpan.FromSeconds(10);

        private IReadOnlyList<int> Commands => Enumerable.Range(1, EventsCount).ToList();

        protected JournalPerfSpec(Config config, string actorSystem, ITestOutputHelper output)
            : base(config ?? Config.Empty, actorSystem, output)
        {
            testProbe = CreateTestProbe();
        }

        internal IActorRef BenchActor(string pid, int replyAfter)
        {
            return Sys.ActorOf(Props.Create(() => new BenchActor(pid, testProbe, EventsCount, false)));;
        }
        
        internal (IActorRef aut,TestProbe probe) BenchActorNewProbe(string pid, int replyAfter)
        {
            var tp = CreateTestProbe();
            return (Sys.ActorOf(Props.Create(() => new BenchActor(pid, tp, EventsCount, false))), tp);
        }
        
        internal (IActorRef aut,TestProbe probe) BenchActorNewProbeGroup(string pid, int numActors, int numMsgs)
        {
            var tp = CreateTestProbe();
            return (
                Sys.ActorOf(Props
                    .Create(() =>
                        new BenchActor(pid, tp, numMsgs, true))
                    .WithRouter(new RoundRobinPool(numActors))), tp);
        }

        internal void FeedAndExpectLast(IActorRef actor, string mode, IReadOnlyList<int> commands)
        {
            commands.ForEach(c => actor.Tell(new Cmd(mode, c)));
            testProbe.ExpectMsg(commands.Last(), ExpectDuration);
        }
        
        internal void FeedAndExpectLastSpecific((IActorRef actor, TestProbe probe) aut, string mode, IReadOnlyList<int> commands)
        {
            commands.ForEach(c => aut.actor.Tell(new Cmd(mode, c)));
            
            aut.probe.ExpectMsg(commands.Last(), ExpectDuration);
        }
        
        internal void FeedAndExpectLastRouterSet(
            (IActorRef actor, TestProbe probe) autSet, string mode,
            IReadOnlyList<int> commands, int numExpect)
        {
            
            commands.ForEach(c => autSet.actor.Tell(new Broadcast(new Cmd(mode, c))));

            for (int i = 0; i < numExpect; i++)
            {
                autSet.probe.ExpectMsg(commands.Last(), ExpectDuration);    
            }

        }

        /// <summary>
        /// Executes a block of code multiple times (no warm-up)
        /// </summary>
        internal void Measure(Func<TimeSpan, string> msg, Action block)
        {
            var measurements = new List<TimeSpan>(MeasurementIterations);

            block(); //warm-up

            int i = 0;
            while (i < MeasurementIterations)
            {
                var sw = Stopwatch.StartNew();
                block();
                sw.Stop();
                measurements.Add(sw.Elapsed);
                Output.WriteLine(msg(sw.Elapsed));
                i++;
            }

            double avgTime = measurements.Select(c => c.TotalMilliseconds).Sum() / MeasurementIterations;
            double msgPerSec = (EventsCount / avgTime) * 1000;

            Output.WriteLine($"Average time: {avgTime} ms, {msgPerSec} msg/sec");
        }
        
        /// <summary>
        /// Executes a block of actions intended for a group
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="block">Block to Measure</param>
        /// <param name="numMsg">Messages per Group worker</param>
        /// <param name="numGroup">Number of workers in Group being measured</param>
        internal void MeasureGroup(Func<TimeSpan, string> msg, Action block, int numMsg,int numGroup)
        {
            var measurements = new List<TimeSpan>(MeasurementIterations);

            block(); //warm-up

            int i = 0;
            while (i < MeasurementIterations)
            {
                var sw = Stopwatch.StartNew();
                block();
                sw.Stop();
                measurements.Add(sw.Elapsed);
                Output.WriteLine(msg(sw.Elapsed));
                i++;
            }

            double avgTime = measurements.Select(c => c.TotalMilliseconds).Sum() / MeasurementIterations;
            double msgPerSec = (numMsg / avgTime) * 1000;
            double msgPerSecTotal = (numMsg*numGroup / avgTime) * 1000;
            Output.WriteLine($"Workers: {numGroup} , Average time: {avgTime} ms, {msgPerSec} msg/sec/actor, {msgPerSecTotal} total msg/sec.");
        }
        
        private void RunPersistGroupBenchmark(int numGroup, int numCommands)
        {
            var p1 = BenchActorNewProbeGroup("GroupPersistPid" + numGroup, numGroup,
                numCommands);
            MeasureGroup(
                d =>
                    $"Persist()-ing {numCommands} * {numGroup} took {d.TotalMilliseconds} ms",
                () =>
                {
                    FeedAndExpectLastRouterSet(p1, "p",
                        Commands.Take(numCommands).ToImmutableList(),
                        numGroup);
                    p1.aut.Tell(new Broadcast(ResetCounter.Instance));
                }, numCommands, numGroup
            );
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_Persist()
        {
            var p1 = BenchActor("PersistPid", EventsCount);
            Measure(d => $"Persist()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "p", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_PersistAll()
        {
            var p1 = BenchActor("PersistAllPid", EventsCount);
            Measure(d => $"PersistAll()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "pb", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_PersistAsync()
        {
            var p1 = BenchActor("PersistAsyncPid", EventsCount);
            Measure(d => $"PersistAsync()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "pa", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_PersistAllAsync()
        {
            var p1 = BenchActor("PersistAllAsyncPid", EventsCount);
            Measure(d => $"PersistAllAsync()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "pba", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }
        
        [Fact]
        public void PersistenceActor_performance_must_measure_PersistGroup10()
        {
            int numGroup = 10;
            int numCommands = Math.Max(Math.Min(EventsCount/10,1000),100);
            RunPersistGroupBenchmark(numGroup, numCommands);
        }
        
        [Fact]
        public void PersistenceActor_performance_must_measure_PersistGroup25()
        {
            int numGroup = 25;
            int numCommands = Math.Max(Math.Min(EventsCount/25,1000),50);
            RunPersistGroupBenchmark(numGroup, numCommands);
        }
        
        [Fact]
        public void PersistenceActor_performance_must_measure_PersistGroup50()
        {
            int numGroup = 50;
            int numCommands = Math.Max(Math.Min(EventsCount/50,1000),20);
            RunPersistGroupBenchmark(numGroup, numCommands);
        }
        
        [Fact]
        public void PersistenceActor_performance_must_measure_PersistGroup100()
        {
            int numGroup = 100;
            int numCommands = Math.Max(Math.Min(EventsCount/100,1000),10);
            RunPersistGroupBenchmark(numGroup, numCommands);
        }
        
        [Fact]
        public void PersistenceActor_performance_must_measure_PersistGroup200()
        {
            int numGroup = 200;
            int numCommands = Math.Max(Math.Min(EventsCount/200,500),10);
            RunPersistGroupBenchmark(numGroup, numCommands);
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_Recovering()
        {
            var p1 = BenchActor("PersistRecoverPid", EventsCount);

            FeedAndExpectLast(p1, "p", Commands);
            Measure(d => $"Recovering {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                BenchActor("PersistRecoverPid", EventsCount);
                testProbe.ExpectMsg(Commands.Last(), ExpectDuration);
            });
        }
        
        [Fact]
        public void PersistenceActor_performance_must_measure_RecoveringTwo()
        {
            var p1 = BenchActorNewProbe("DoublePersistRecoverPid1", EventsCount);
            var p2 = BenchActorNewProbe("DoublePersistRecoverPid2", EventsCount);
            FeedAndExpectLastSpecific(p1, "p", Commands);
            FeedAndExpectLastSpecific(p2, "p", Commands);
            MeasureGroup(d => $"Recovering {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
               var task1 = Task.Run(()=>
               {
                   var refAndProbe =BenchActorNewProbe("DoublePersistRecoverPid1",
                           EventsCount);
                   refAndProbe.probe.ExpectMsg(Commands.Last(), ExpectDuration);
               });
               var task2 =Task.Run(() =>
               {
                   var refAndProbe =BenchActorNewProbe("DoublePersistRecoverPid2", EventsCount);
                   refAndProbe.probe.ExpectMsg(Commands.Last(), ExpectDuration);
               });
               Task.WaitAll(new[] {task1, task2});

            },EventsCount,2);
        }
        [Fact]
        public void PersistenceActor_performance_must_measure_RecoveringFour()
        {
            var p1 = BenchActorNewProbe("QuadPersistRecoverPid1", EventsCount);
            var p2 = BenchActorNewProbe("QuadPersistRecoverPid2", EventsCount);
            var p3 = BenchActorNewProbe("QuadPersistRecoverPid3", EventsCount);
            var p4 = BenchActorNewProbe("QuadPersistRecoverPid4", EventsCount);
            FeedAndExpectLastSpecific(p1, "p", Commands);
            FeedAndExpectLastSpecific(p2, "p", Commands);
            FeedAndExpectLastSpecific(p3, "p", Commands);
            FeedAndExpectLastSpecific(p4, "p", Commands);
            MeasureGroup(d => $"Recovering {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                var task1 = Task.Run(()=>
                {
                    var refAndProbe =BenchActorNewProbe("QuadPersistRecoverPid1",
                        EventsCount);
                    refAndProbe.probe.ExpectMsg(Commands.Last(), ExpectDuration);
                });
                var task2 =Task.Run(() =>
                {
                    var refAndProbe =BenchActorNewProbe("QuadPersistRecoverPid2", EventsCount);
                    refAndProbe.probe.ExpectMsg(Commands.Last(), ExpectDuration);
                });
                var task3 =Task.Run(() =>
                {
                    var refAndProbe =BenchActorNewProbe("QuadPersistRecoverPid3", EventsCount);
                    refAndProbe.probe.ExpectMsg(Commands.Last(), ExpectDuration);
                });
                var task4 =Task.Run(() =>
                {
                    var refAndProbe =BenchActorNewProbe("QuadPersistRecoverPid4", EventsCount);
                    refAndProbe.probe.ExpectMsg(Commands.Last(), ExpectDuration);
                });
                Task.WaitAll(new[] {task1, task2,task3,task4});

            },EventsCount,4);
        }
    }

    internal class ResetCounter
    {
        public static ResetCounter Instance { get; } = new ResetCounter();
        private ResetCounter() { }
    }

    public class Cmd
    {
        public Cmd(string mode, int payload)
        {
            Mode = mode;
            Payload = payload;
        }

        public string Mode { get; }

        public int Payload { get; }
    }

    internal class BenchActor : UntypedPersistentActor
    {
        private int _counter = 0;
        private const int BatchSize = 50;
        private List<Cmd> _batch = new List<Cmd>(BatchSize);
        
        public BenchActor(string persistenceId, IActorRef replyTo, int replyAfter, bool isGroup = false)
        {
            PersistenceId = isGroup
                ? persistenceId +
                  MurmurHash.StringHash(
                      Context.Parent.Path.Name + Context.Self.Path.Name)
                : persistenceId;
            ReplyTo = replyTo;
            ReplyAfter = replyAfter;
        }
        
        

        public override string PersistenceId { get; }
        public IActorRef ReplyTo { get; }
        public int ReplyAfter { get; }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case Cmd c:
                    _counter++;
                    if (c.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{c.Payload}]");
                    if (_counter == ReplyAfter) ReplyTo.Tell(c.Payload);
                    break;
            }
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Cmd c when c.Mode == "p":
                    Persist(c, d =>
                    {
                        _counter += 1;
                        if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                        if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                    });
                    break;
                case Cmd c when c.Mode == "pb":
                    _batch.Add(c);

                    if (_batch.Count % BatchSize == 0)
                    {
                        PersistAll(_batch, d =>
                        {
                            _counter += 1;
                            if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                            if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                        });
                        _batch = new List<Cmd>(BatchSize);
                    }
                    break;
                case Cmd c when c.Mode == "pa":
                    PersistAsync(c, d =>
                    {
                        _counter += 1;
                        if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                        if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                    });
                    break;
                case Cmd c when c.Mode == "pba":
                    _batch.Add(c);

                    if (_batch.Count % BatchSize == 0)
                    {
                        PersistAllAsync(_batch, d =>
                        {
                            _counter += 1;
                            if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                            if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                        });
                        _batch = new List<Cmd>(BatchSize);
                    }
                    break;
                case ResetCounter _:
                    _counter = 0;
                    break;
            }
        }
    }
}
