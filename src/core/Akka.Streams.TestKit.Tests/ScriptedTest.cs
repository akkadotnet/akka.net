using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.Util;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    [Serializable]
    public class ScriptedException : Exception
    {
        public ScriptedException() { }
        public ScriptedException(string message) : base(message) { }
        public ScriptedException(string message, Exception inner) : base(message, inner) { }
        protected ScriptedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    public abstract class ScriptedTest : AkkaSpec
    {
        protected static class Script
        {
            public static Script<TIn, TOut> Create<TIn, TOut>(params Tuple<IEnumerable<TIn>, IEnumerable<TOut>>[] phases)
            {
                var providedInputs = new List<TIn>();
                var expectedOutputs = new List<TOut>();
                var jumps = new List<int>();

                foreach (var phase in phases)
                {
                    var ins = phase.Item1.ToArray();
                    var outs = phase.Item2.ToArray();

                    providedInputs.AddRange(ins);
                    expectedOutputs.AddRange(outs);

                    jumps.AddRange(new int[ins.Length - 1]);
                    jumps.Add(outs.Length);
                }

                return new Script<TIn, TOut>(providedInputs.ToArray(), expectedOutputs.ToArray(), jumps.ToArray(), 0, 0, 0, false);
            }

        }

        protected class Script<TIn, TOut>
        {
            internal readonly TIn[] ProvidedInputs;
            internal readonly TOut[] ExpectedOutputs;
            internal readonly int[] Jumps;
            internal readonly int InputCursor;
            internal readonly int OutputCursor;
            internal readonly int OutputEndCursor;
            internal readonly bool Completed;

            public Script(TIn[] providedInputs, TOut[] expectedOutputs, int[] jumps, int inputCursor, int outputCursor, int outputEndCursor, bool completed)
            {
                if (jumps.Length != providedInputs.Length) throw new ArgumentException("Inputs count must be equal jumps count");

                ProvidedInputs = providedInputs;
                ExpectedOutputs = expectedOutputs;
                Jumps = jumps;
                InputCursor = inputCursor;
                OutputCursor = outputCursor;
                OutputEndCursor = outputEndCursor;
                Completed = completed;
            }

            public bool IsFinished => OutputCursor == ExpectedOutputs.Length;
            public int PendingOutputs => OutputEndCursor - OutputCursor;
            public bool NoOutputsPending => PendingOutputs == 0;
            public bool SomeOutputsPending => !NoOutputsPending;

            public int PendingInputs => ProvidedInputs.Length - InputCursor;
            public bool NoInputsPending => PendingInputs == 0;
            public bool SomeInputsPending => !NoInputsPending;

            public TIn ProvideInput(out Script<TIn, TOut> script)
            {
                if (NoInputsPending)
                    throw new ScriptedException("Script cannot provide more inputs");

                script = new Script<TIn, TOut>(ProvidedInputs, ExpectedOutputs, Jumps, InputCursor + 1, OutputCursor, OutputEndCursor + Jumps[InputCursor], Completed);
                return ProvidedInputs[InputCursor];
            }

            public Script<TIn, TOut> ConsumeOutput(TOut output)
            {
                if (NoOutputsPending)
                    throw new ScriptedException($"Tried to produce element {output} but no elements should be produced right now");

                if (!Equals(output, ExpectedOutputs[OutputCursor]))
                    throw new ArgumentException("Unexpected output", "output");

                return new Script<TIn, TOut>(ProvidedInputs, ExpectedOutputs, Jumps, InputCursor, OutputCursor + 1, OutputEndCursor, Completed);
            }

            public Script<TIn, TOut> Complete()
            {
                if (!IsFinished)
                    throw new Exception("Received OnComplete prematurely");

                return new Script<TIn, TOut>(ProvidedInputs, ExpectedOutputs, Jumps, InputCursor, OutputCursor + 1, OutputEndCursor, true);
            }

            public Script<TIn, TOut> Error(Exception e)
            {
                throw e;
            }
        }

        protected class ScriptRunner<TIn, TOut, TMat> : ChainSetup<TIn, TOut, TMat>
        {
            private readonly int _maximumOverrun;
            private readonly int _maximumRequests;
            private readonly int _maximumBuffer;

            private readonly List<string> _debugLog = new List<string>();

            private Script<TIn, TOut> _currentScript;
            private int _remainingDemand;
            private long _pendingRequests = 0L;
            private long _outstandingDemand = 0L;
            private bool _completed = false;

            public ScriptRunner(
                Func<Flow<TIn, TIn, Unit>, Flow<TIn, TOut, TMat>> op,
                ActorMaterializerSettings settings,
                Script<TIn, TOut> script,
                int maximumOverrun,
                int maximumRequests,
                int maximumBuffer,
                TestKitBase system) : base(op, settings, ToPublisher<TIn, TOut>, system)
            {
                _currentScript = script;
                _maximumOverrun = maximumOverrun;
                _maximumRequests = maximumRequests;
                _maximumBuffer = maximumBuffer;

                _remainingDemand = _currentScript.ExpectedOutputs.Length + ThreadLocalRandom.Current.Next(1, maximumOverrun);
                DebugLog($"Starting with remaining demand={_remainingDemand}");
            }

            public bool MayProvideInput => _currentScript.SomeInputsPending && (_pendingRequests > 0) && (_currentScript.PendingOutputs <= _maximumBuffer);
            public bool MayRequestMore => _remainingDemand > 0;

            public int GetNextDemand()
            {
                var max = Math.Min(_remainingDemand, _maximumRequests);
                if (max == 1)
                {
                    _remainingDemand = 0;
                    return 1;
                }
                else
                {
                    var demand = ThreadLocalRandom.Current.Next(1, max);
                    _remainingDemand -= max;
                    return demand;
                }
            }

            public void DebugLog(string str)
            {
                _debugLog.Add(str);
            }

            public void Request(int demand)
            {
                DebugLog($"Test environment requests {demand}");
                DownstreamSubscription.Request(demand);
                _outstandingDemand += demand;
            }

            public bool ShakeIt()
            {
                var oneMilli = TimeSpan.FromMilliseconds(1);
                var marker = new object();
                var u = Upstream.ReceiveWhile(oneMilli, filter: msg =>
                {
                    if (msg is TestPublisher.RequestMore)
                    {
                        var more = (TestPublisher.RequestMore)msg;
                        DebugLog($"Operation requests {more.NrOfElements}");
                        _pendingRequests += more.NrOfElements;
                        return marker;
                    }
                    return null;
                });
                var d = Downstream.ReceiveWhile(oneMilli, filter: msg => msg.Match()
                    .With<TestSubscriber.OnNext<TOut>>(next =>
                    {
                        DebugLog($"Operation produces [{next.Element}]");
                        if (_outstandingDemand == 0) throw new Exception("operation produced while there was no demand");
                        _outstandingDemand--;
                        _currentScript = _currentScript.ConsumeOutput(next.Element);
                    })
                    .With<TestSubscriber.OnComplete>(complete =>
                    {
                        _currentScript = _currentScript.Complete();
                    })
                    .With<TestSubscriber.OnError>(error =>
                    {
                        _currentScript = _currentScript.Error(error.Cause);
                    })
                    .WasHandled
                    ? marker
                    : null);

                return u.Union(d).Any(x => x == marker);
            }

            public void Run()
            {
                DebugLog($"Running {_currentScript}");
                Request(GetNextDemand());
                var idleRounds = 0;
                while (true)
                {
                    if (idleRounds > 250) throw new Exception("Too many idle rounds");
                    if (!_currentScript.Completed)
                    {
                        idleRounds = ShakeIt() ? 0 : idleRounds + 1;
                        var tieBreak = ThreadLocalRandom.Current.Next()%2 == 0;

                        if (MayProvideInput && (!MayRequestMore || tieBreak))
                        {
                            Script<TIn, TOut> nextScript;
                            var input = _currentScript.ProvideInput(out nextScript);
                            DebugLog($"Test environment produces [{input}]");
                            _pendingRequests--;
                            _currentScript = nextScript;
                            UpstreamSubscription.SendNext(input);
                        }
                        else if (MayRequestMore && (!MayProvideInput || !tieBreak))
                        {
                            Request(GetNextDemand());
                        }
                        else
                        {
                            if (_currentScript.NoInputsPending && !_completed)
                            {
                                DebugLog("Test environment complete");
                                UpstreamSubscription.SendComplete();
                                _completed = true;
                            }
                        }
                    }
                    else break;
                }
            }
        }

        protected ScriptedTest(ActorSystem system, ITestOutputHelper output = null) : base(system, output)
        {
        }

        protected ScriptedTest(Config config, ITestOutputHelper output = null) : base(config, output)
        {
        }

        protected ScriptedTest(string config, ITestOutputHelper output = null) : base(config, output)
        {
        }

        protected ScriptedTest(ITestOutputHelper output = null) : base(output)
        {
        }

        protected void RunScript<TIn2, TOut2, TMat2>(Script<TIn2, TOut2> script, ActorMaterializerSettings settings,
            Func<Flow<TIn2, TIn2, Unit>, Flow<TIn2, TOut2, TMat2>> op,
            int maximumOverrun = 3, int maximumRequest = 3, int maximumBuffer = 3)
        {
            new ScriptRunner<TIn2, TOut2, TMat2>(op, settings, script, maximumOverrun, maximumRequest, maximumBuffer, this).Run();
        }

        protected static IPublisher<TOut> ToPublisher<TIn, TOut>(Source<TOut, Unit> source, IMaterializer materializer)
        {
            return source.RunWith(Sink.AsPublisher<TOut>(false), materializer);
        }
    }
}