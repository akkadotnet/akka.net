//-----------------------------------------------------------------------
// <copyright file="InterpreterBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Tests.Implementation.Fusing;
using NBench;

namespace Akka.Streams.Tests.Performance
{
    public class InterpreterBenchmark
    {
        [PerfBenchmark(Description = "Test the performance of the graph interpreter with 1 identity",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Graph_interpreter_100k_elements_with_1_identity() => Execute(1);


        [PerfBenchmark(Description = "Test the performance of the graph interpreter with 5 identities",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Skip = "FIXME Port is pulled twice")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1000)]
        public void Graph_interpreter_100k_elements_with_5_identity() => Execute(5);


        [PerfBenchmark(Description = "Test the performance of the graph interpreter with 10 identities",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Skip = "FIXME Port is pulled twice")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1000)]
        public void Graph_interpreter_100k_elements_with_10_identity() => Execute(10);


        private static readonly int[] Data100K = Enumerable.Range(1, 100000).ToArray();

        private static void Execute(int numberOfIdentities)
        {
            new GraphInterpreterSpecKit().WithTestSetup((setup, lastEvents) =>
            {
                var identities =
                    Enumerable.Range(1, numberOfIdentities)
                        .Select(_ => GraphStages.Identity<int>())
                        .Cast<IGraphStageWithMaterializedValue<Shape, object>>()
                        .ToArray();

                var source = new GraphDataSource<int>("source", Data100K);
                var sink = new GraphDataSink<int>("sink", Data100K.Length);

                var b = setup.Builder(identities)
                    .Connect(source, identities[0].Shape.Inlets[0] as Inlet<int>)
                    .Connect(identities.Last().Shape.Outlets[0] as Outlet<int>, sink);

                // FIXME: This should not be here, this is pure setup overhead
                for (var i = 0; i < identities.Length - 1; i++)
                    b.Connect(identities[i].Shape.Outlets[0] as Outlet<int>,
                        identities[i + 1].Shape.Inlets[0] as Inlet<int>);

                b.Init();
                sink.RequestOne();
                setup.Interpreter.Execute(int.MaxValue);
            });
        }
        
        private sealed class GraphDataSource<T> : GraphInterpreter.UpstreamBoundaryStageLogic
        {
            private int _index;
            private readonly string _toString;

            public GraphDataSource(string toString, T[] data)
            {
                _toString = toString;

                // ReSharper disable once PossibleNullReferenceException
                typeof(OutPort).GetField("Id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Out, 0);

                SetHandler(Out, onPull: () =>
                {
                     if (_index < data.Length)
                    {
                        Push(Out, data[_index]);
                        _index++;
                    }
                    else
                        CompleteStage();
                }, onDownstreamFinish: CompleteStage);
                Console.WriteLine("Handler Set");
            }
            
            public override Outlet Out { get; } = new Outlet<T>("out");

            public override string ToString() => _toString;
        }

        private sealed class GraphDataSink<T> : GraphInterpreter.DownstreamBoundaryStageLogic
        {
            private readonly Inlet<T> _inlet = new Inlet<T>("in");
            private readonly string _toString;

            public GraphDataSink(string toString, int expected)
            {
                _toString = toString;
                // ReSharper disable once PossibleNullReferenceException
                typeof(InPort).GetField("Id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_inlet, 0);
                In = _inlet;

                SetHandler(In, onPush: () =>
                {
                    expected--;
                    if(expected > 0)
                        Pull(_inlet);
                    // Otherwise do nothing, it will exit the interpreter
                });
            }

            public override Inlet In { get; }

            public void RequestOne() => Pull(_inlet);

            public override string ToString() => _toString;
        }

        private sealed class NoobBus : LoggingBus
        {
            public static readonly NoobBus Instance = new NoobBus();

            private NoobBus() { }

            public override bool Subscribe(IActorRef subscriber, Type classifier) => true;

            public override void Publish(object @event)
            {
            }

            public override bool Unsubscribe(IActorRef subscriber) => true;

            public override bool Unsubscribe(IActorRef subscriber, Type classifier) => true;
        }
    }
}
