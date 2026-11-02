using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.MultiNode.TestAdapter.Internal;
using Akka.MultiNode.TestAdapter.NodeRunner;
using Akka.MultiNode.TestAdapter.SampleTests;
using Akka.MultiNode.TestAdapter.SampleTests.Metadata;
using Akka.MultiNode.TestAdapter.Tests.Helpers;
using Akka.Remote.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Tests
{
    [Collection(TestCollections.MultiNode)]
    public class MultiNodeTestExecutorSpec
    {
        private readonly string _sampleTestsAssemblyPath;

        public MultiNodeTestExecutorSpec()
        {
            _sampleTestsAssemblyPath = Path.GetFullPath(SampleTestsMetadata.AssemblyFileName);
            File.Exists(_sampleTestsAssemblyPath).Should().BeTrue($"Assemblies with samples should exist at {_sampleTestsAssemblyPath}");
            CommandLine.Initialize(new []{"-Dmultinode.test-runner=\"multinode\""});
        }

        [Fact]
        public async Task Should_run_tests_and_report_results()
        {
            var assembly = Assembly.LoadFrom(_sampleTestsAssemblyPath);
            var testAssembly = new XunitTestAssembly(assembly);
            var collectionFactory = new CollectionPerSessionTestCollectionFactory(testAssembly);
            var discoverer = new XunitTestFrameworkDiscoverer(testAssembly, collectionFactory);

            // Discover tests
            var testCases = new List<IXunitTestCase>();
            await discoverer.Find(async testCase =>
            {
                if (testCase is MultiNodeTestCase mnTestCase)
                    testCases.Add(mnTestCase);
                return true;
            }, new SimpleDiscoveryOptions());

            testCases.Count.Should().BeGreaterThan(0, "Should discover test cases");

            // Execute tests
            using (var sink = new TestSink())
            {
                // Register test cases for later lookup
                foreach (var tc in testCases.OfType<MultiNodeTestCase>())
                    sink.RegisterTestCase(tc);

                var executor = new MultiNodeTestFrameworkExecutor(testAssembly);
                await executor.RunTestCases(
                    testCases,
                    sink,
                    new SimpleExecutionOptions(),
                    CancellationToken.None);

                sink.Finished.WaitOne(TimeSpan.FromMinutes(10)).Should().BeTrue("Test execution timed out");

                sink.TestResults.Count.Should().Be(5);

                Should_report_passes(sink);
                Should_report_failures(sink);
                Should_report_failures_for_one_node(sink);
                Should_report_skipped_specs(sink);
                Should_ignore_specs_with_bad_config(sink);
            }
        }

        private void Should_report_passes(TestSink sink)
        {
            var passed = sink.TestResults.FirstOrDefault(r => r.DisplayName
                .Contains(nameof(SampleMultiNodeSpec)));
            passed.Should().NotBeNull();
            passed.Passed.Should().BeTrue("Should report passed spec result");
            passed.RunSummary.Total.Should().Be(2);
            passed.RunSummary.Failed.Should().Be(0);
            passed.RunSummary.Skipped.Should().Be(0);
        }

        private void Should_report_failures(TestSink sink)
        {
            var failed = sink.TestResults.FirstOrDefault(r => r.DisplayName
                .Contains($".{nameof(FailedMultiNodeSpec)}"));
            failed.Should().NotBeNull();
            failed.Failed.Should().BeTrue("Should report failed spec result");
            failed.RunSummary.Total.Should().Be(2);
            failed.RunSummary.Failed.Should().Be(2);
        }

        private void Should_report_failures_for_one_node(TestSink sink)
        {
            var oneFailed = sink.TestResults.FirstOrDefault(r => r.DisplayName
                .Contains(nameof(OneNodeFailedMultiNodeSpec)));

            oneFailed.Should().NotBeNull();
            oneFailed.Failed.Should().BeTrue();
            oneFailed.RunSummary.Total.Should().Be(2);
            oneFailed.RunSummary.Failed.Should().Be(1, "Should report failed spec result when only one node failed");
            oneFailed.RunSummary.Skipped.Should().Be(0, "Should still contain not-failed results");
        }

        private void Should_report_skipped_specs(TestSink sink)
        {
            var skipped = sink.TestResults.FirstOrDefault(r => r.DisplayName
                .Contains(nameof(SkippedMultiNodeSpec)));

            skipped.Should().NotBeNull();
            skipped.RunSummary.Total.Should().Be(2);
            skipped.RunSummary.Skipped.Should().Be(2, "When skipped, all nodes should be skipped");
            skipped.Skipped.Should().BeTrue("Should report skipped spec result");
        }

        private void Should_ignore_specs_with_bad_config(TestSink sink)
        {
            var badConfig = sink.TestResults.FirstOrDefault(r => r.DisplayName
                .Contains(nameof(BadConfigMultiNodeSpec)));

            badConfig.Should().NotBeNull();
            badConfig.RunSummary.Total.Should().Be(1);
            badConfig.RunSummary.Skipped.Should().Be(1, "Should skip specs with bad configuration - because can not build configuration");

            var testCase = sink.GetTestCase(badConfig);
            testCase.Should().NotBeNull();
            testCase.Nodes[0].DisplayName.Should().Be("ERRORED");
        }
    }
}
