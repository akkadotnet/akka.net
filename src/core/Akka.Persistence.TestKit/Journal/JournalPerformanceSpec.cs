using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.TestKit.Journal
{
    public sealed class ResetCounter
    {
        public static readonly ResetCounter Instance = new ResetCounter();
        private ResetCounter() { }
    }

    public sealed class Cmd
    {

        public Cmd(string mode, int payload)
        {
            Mode = mode;
            Payload = payload;
        }

        public string Mode { get; private set; }
        public int Payload { get; private set; }
    }

    public class BenchmarkActor : PersistentActor
    {
        private readonly string _persistenceId;
        private readonly ActorRef _replyTo;
        private readonly int _replyAfter;

        private int _counter = 0;

        public BenchmarkActor(string persistenceId, ActorRef replyTo, int replyAfter)
        {
            _persistenceId = persistenceId;
            _replyTo = replyTo;
            _replyAfter = replyAfter;
        }

        public override string PersistenceId { get { return _persistenceId; } }

        protected override bool ReceiveRecover(object message)
        {
            Cmd cmd;
            if ((cmd = message as Cmd) != null)
            {
                CmdHandler(cmd);
                return true;
            }

            return false;
        }

        protected override bool ReceiveCommand(object message)
        {
            Cmd cmd;
            if ((cmd = message as Cmd) != null)
            {
                switch (cmd.Mode)
                {
                    case "p":
                        {
                            Persist(cmd, CmdHandler);
                            break;
                        }
                    case "pa":
                        {
                            PersistAsync(cmd, CmdHandler);
                            break;
                        }
                    case "par":
                        {
                            _counter++;
                            Persist(cmd, AssertPayload);
                            if (_counter == _replyAfter) _replyTo.Tell(cmd.Payload);
                            break;
                        }
                    case "n":
                        {
                            CmdHandler(cmd);
                            break;
                        }
                }
            }
            else if (message is ResetCounter)
            {
                _counter = 0;
            }
            else return false;
            return true;
        }

        private void CmdHandler(Cmd cmd)
        {
            _counter++;
            AssertPayload(cmd);

            if (_counter == _replyAfter)
                _replyTo.Tell(cmd.Payload);
        }

        private void AssertPayload(Cmd cmd)
        {
            if (cmd.Payload != _counter)
            {
                throw new ArgumentException(String.Format("Expected to receive {0} but got {1}", _counter, cmd.Payload));
            }
        }
    }

    public class JournalPerformanceSpec : JournalSpec
    {
        private readonly TestProbe _probe;

        public JournalPerformanceSpec(Config config = null, string actorSystemName = null, string testActorName = null)
            : base(config, actorSystemName ?? "JournalPerformanceSpec", testActorName)
        {
            _probe = CreateTestProbe();
            AwaitDuration = TimeSpan.FromSeconds(10);
            EventsCount = 10000;
            MeasurementIterations = 10;
        }

        public int AwaitDurationMilis { get { return (int)AwaitDuration.TotalMilliseconds; }}

        /// <summary>
        /// Timeout used for <see cref="ExpectMsg"/>, in order to tune the awaits to your journal's performance.
        /// </summary>
        public TimeSpan AwaitDuration { get; protected set; }

        /// <summary>
        /// Number of messages sent to the PersistentActor under test for each test iteration.
        /// </summary>
        protected int EventsCount { get; set; }

        /// <summary>
        /// Number of measurement iterations each test will be run. 
        /// </summary>
        protected int MeasurementIterations { get; set; }

        protected ActorRef BenchmarkActor(int replyAfter)
        {
            return ActorOf(() => new BenchmarkActor(Pid, _probe.Ref, replyAfter));
        }

        protected void FeedAndExpectLast(ActorRef aref, string mode, params int[] commands)
        {
            foreach (var command in commands)
            {
                aref.Tell(new Cmd(mode, command));
            }
            _probe.ExpectMsg(commands[commands.Length - 1], AwaitDuration);
        }

        protected void Measure(Func<TimeSpan, string> msg, Action block)
        {
            var measurements = new TimeSpan[MeasurementIterations];
            var i = 0;
            while (i < MeasurementIterations)
            {
                var start = DateTime.Now;
                
                block();
                
                var stop = DateTime.Now;
                var duration = stop - start;

                measurements[i] = duration;

                if(msg != null) Log.Info(string.Format(msg(duration)));

                i++;
            }

            var avg = new TimeSpan((long)measurements.Average(d => d.Ticks));
            Log.Info(string.Format("Average time: {0} ms", avg.TotalMilliseconds));
        }

        [Fact]
        public void PersistentActor_performance_of_PersistAsync()
        {
            var commands = Enumerable.Range(1, EventsCount + 1).ToArray();
            var pref = BenchmarkActor(EventsCount);
            Measure(null, () =>
            {
                FeedAndExpectLast(pref, "pa", commands);
                pref.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistentActor_performance_of_Persist()
        {
            var commands = Enumerable.Range(1, EventsCount + 1).ToArray();
            var pref = BenchmarkActor(EventsCount);
            Measure(null, () =>
            {
                FeedAndExpectLast(pref, "p", commands);
                pref.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistentActor_performance_of_recovering()
        {
            var commands = Enumerable.Range(1, EventsCount + 1).ToArray();
            var pref = BenchmarkActor(EventsCount);
            FeedAndExpectLast(pref, "p", commands);

            Measure(null, () =>
            {
                BenchmarkActor(EventsCount);
                _probe.ExpectMsg(commands[commands.Length - 1], AwaitDuration);
            });
        }
    }
}