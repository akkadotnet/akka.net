//-----------------------------------------------------------------------
// <copyright file="ScriptedTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.Util;
using Reactive.Streams;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    [Serializable]
    public class ScriptException : Exception
    {
        public ScriptException() { }
        public ScriptException(string message) : base(message) { }
        public ScriptException(string message, Exception inner) : base(message, inner) { }

#if SERIALIZATION
        protected ScriptException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#endif
    }

    public abstract class ScriptedTest : AkkaSpec
    {
        protected static class Script
        {
            public static Script<TIn, TOut> Create<TIn, TOut>(params (ICollection<TIn>, ICollection<TOut>)[] phases)
            {
                var providedInputs = new List<TIn>();
                var expectedOutputs = new List<TOut>();
                var jumps = new List<int>();

                foreach (var phase in phases)
                {
                    var ins = phase.Item1;
                    var outs = phase.Item2;

                    providedInputs.AddRange(ins);
                    expectedOutputs.AddRange(outs);

                    var jump = new int[ins.Count];
                    jump.Initialize();
                    jump[jump.Length - 1] = outs.Count;

                    jumps.AddRange(jump);
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

            public (TIn, Script<TIn, TOut>) ProvideInput()
            {
                if (NoInputsPending)
                    throw new ScriptException("Script cannot provide more inputs");

                var script = new Script<TIn, TOut>(ProvidedInputs, ExpectedOutputs, Jumps, InputCursor + 1, OutputCursor, OutputEndCursor + Jumps[InputCursor], Completed);
                return (ProvidedInputs[InputCursor], script);
            }

            public Script<TIn, TOut> ConsumeOutput(TOut output)
            {
                if (NoOutputsPending)
                    throw new ScriptException($"Tried to produce element {output} but no elements should be produced right now");

                var equalsExpectedOutput = typeof (IEnumerable).IsAssignableFrom(typeof (TOut))
                    ? ((IEnumerable) output).Cast<object>().SequenceEqual(((IEnumerable) ExpectedOutputs[OutputCursor]).Cast<object>())
                    : Equals(output, ExpectedOutputs[OutputCursor]);
                if (!equalsExpectedOutput)
                    throw new ArgumentException("Unexpected output", nameof(output));

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

            public string Debug()
            {
                return
                    $"Script(pending=({string.Join(",", PendingInputs)} in, {string.Join(",", PendingOutputs)} out), remainingIns={string.Join("/", ProvidedInputs.Skip(InputCursor))}, remainingOuts={string.Join("/", ExpectedOutputs.Skip(OutputCursor))})";
            }
        }

        protected class ScriptRunner<TIn, TOut, TMat> : ChainSetup<TIn, TOut, TMat>
        {
            private readonly int _maximumRequests;
            private readonly int _maximumBuffer;

            private readonly List<string> _debugLog = new List<string>();

            private Script<TIn, TOut> _currentScript;
            private int _remainingDemand;
            private long _pendingRequests;
            private long _outstandingDemand;
            private bool _completed;

            public ScriptRunner(
                Func<Flow<TIn, TIn, NotUsed>, Flow<TIn, TOut, TMat>> op,
                ActorMaterializerSettings settings,
                Script<TIn, TOut> script,
                int maximumOverrun,
                int maximumRequests,
                int maximumBuffer,
                TestKitBase system) : base(op, settings, ToPublisher, system)
            {
                _currentScript = script;
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
                    _remainingDemand -= demand;
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
                var oneMilli = TimeSpan.FromMilliseconds(10);
                var marker = new object();
                var u = Upstream.ReceiveWhile(oneMilli, filter: msg =>
                {
                    if (msg is TestPublisher.RequestMore more)
                    {
                        DebugLog($"Operation requests {more.NrOfElements}");
                        _pendingRequests += more.NrOfElements;
                        return marker;
                    }
                    DebugLog($"Operation received {msg}");
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
                        DebugLog("Operation complete.");
                        _currentScript = _currentScript.Complete();
                    })
                    .With<TestSubscriber.OnError>(error =>
                    {
                        _currentScript = _currentScript.Error(error.Cause);
                    })
                    .WasHandled
                    ? marker
                    : null);

                return u.Concat(d).Any(x => x == marker);
            }

            public void Run()
            {
                try
                {

                    DebugLog($"Running {_currentScript}");
                    Request(GetNextDemand());
                    var idleRounds = 0;
                    while (true)
                    {
   
                        if (idleRounds > 250) throw new Exception("Too many idle rounds");
                        if (_currentScript.Completed)
                            break;

                        idleRounds = ShakeIt() ? 0 : idleRounds + 1;

                        var tieBreak = ThreadLocalRandom.Current.Next(0, 1) == 0;
                        if (MayProvideInput && (!MayRequestMore || tieBreak))
                        {
                            var next = _currentScript.ProvideInput();
                            var input = next.Item1;
                            var nextScript = next.Item2;
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
                                DebugLog("Test environment completes");
                                UpstreamSubscription.SendComplete();
                                _completed = true;
                                return; // don't execute again if completed
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine(
                        $"Steps leading to failure:\n{string.Join("\n", _debugLog)}\nCurrentScript: {_currentScript.Debug()}");
                    throw;
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
            Func<Flow<TIn2, TIn2, NotUsed>, Flow<TIn2, TOut2, TMat2>> op,
            int maximumOverrun = 3, int maximumRequest = 3, int maximumBuffer = 3)
        {
            new ScriptRunner<TIn2, TOut2, TMat2>(op, settings, script, maximumOverrun, maximumRequest, maximumBuffer, this).Run();
        }

        protected static IPublisher<TOut> ToPublisher<TOut>(Source<TOut, NotUsed> source, IMaterializer materializer)
        {
            return source.RunWith(Sink.AsPublisher<TOut>(false), materializer);
        }
    }
}
