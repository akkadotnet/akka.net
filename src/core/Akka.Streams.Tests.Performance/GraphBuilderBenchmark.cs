//-----------------------------------------------------------------------
// <copyright file="GraphBuilderBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using NBench;

namespace Akka.Streams.Tests.Performance
{
    public class GraphBuilderBenchmark
    {
        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a flow with 1 map stage",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Flow_with_1_map() => MaterializationBenchmark.FlowWithMapBuilder(1);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a flow with 10 map stages",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Flow_with_10_map() => MaterializationBenchmark.FlowWithMapBuilder(10);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a flow with 100 map stages",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Flow_with_100_map() => MaterializationBenchmark.FlowWithMapBuilder(100);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a flow with 1000 map stages",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 150)]
        public void Flow_with_1000_map() => MaterializationBenchmark.FlowWithMapBuilder(1000);



        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 1 junction",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_1_junction() => MaterializationBenchmark.GraphWithJunctionsBuilder(1);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 10 junctions",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_10_junction() => MaterializationBenchmark.GraphWithJunctionsBuilder(10);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 100 junctions",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 20)]
        public void Graph_with_100_junction() => MaterializationBenchmark.GraphWithJunctionsBuilder(100);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 1000 junctions",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1000)]
        public void Graph_with_1000_junction() => MaterializationBenchmark.GraphWithJunctionsBuilder(1000);



        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 1 nested import",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_1_nested_imports() => MaterializationBenchmark.GraphWithNestedImportsBuilder(1);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 10 nested imports",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_10_nested_imports() => MaterializationBenchmark.GraphWithNestedImportsBuilder(10);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 100 nested imports",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_100_nested_imports() => MaterializationBenchmark.GraphWithNestedImportsBuilder(100);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 1000 nested imports",
            RunMode = RunMode.Iterations, TestMode = TestMode.Measurement, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_1000_nested_imports() => MaterializationBenchmark.GraphWithNestedImportsBuilder(1000);



        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 1 imported flow",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_1_imported_flow() => MaterializationBenchmark.GraphWithImportedFlowBuilder(1);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 10 imported flows",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_10_imported_flows() => MaterializationBenchmark.GraphWithImportedFlowBuilder(10);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 100 imported flows",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_100_imported_flows() => MaterializationBenchmark.GraphWithImportedFlowBuilder(100);


        [PerfBenchmark(Description = "Test the performance of the GraphBuilder for a graph with 1000 imported flows",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 100)]
        public void Graph_with_1000_imported_flows() => MaterializationBenchmark.GraphWithImportedFlowBuilder(1000);
    }
}
