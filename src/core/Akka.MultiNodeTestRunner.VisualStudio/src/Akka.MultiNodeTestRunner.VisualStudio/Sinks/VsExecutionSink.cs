using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Xunit.Abstractions;
using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;

namespace Xunit.Runner.VisualStudio
{
    public class VsExecutionSink : TestMessageSink, IExecutionSink, IDisposable
    {
        readonly Func<bool> cancelledThunk;
        readonly ITestFrameworkExecutionOptions executionOptions;
        readonly LoggerHelper logger;
        readonly IMessageSinkWithTypes innerSink;
        readonly ITestExecutionRecorder recorder;
        readonly Dictionary<string, TestCase> testCasesMap;

        public VsExecutionSink(IMessageSinkWithTypes innerSink,
                               ITestExecutionRecorder recorder,
                               LoggerHelper logger,
                               Dictionary<string, TestCase> testCasesMap,
                               ITestFrameworkExecutionOptions executionOptions,
                               Func<bool> cancelledThunk)
        {
            this.innerSink = innerSink;
            this.recorder = recorder;
            this.logger = logger;
            this.testCasesMap = testCasesMap;
            this.executionOptions = executionOptions;
            this.cancelledThunk = cancelledThunk;

            ExecutionSummary = new ExecutionSummary();

            Diagnostics.ErrorMessageEvent += HandleErrorMessage;
            Execution.TestAssemblyCleanupFailureEvent += HandleTestAssemblyCleanupFailure;
            Execution.TestAssemblyFinishedEvent += HandleTestAssemblyFinished;
            Execution.TestCaseCleanupFailureEvent += HandleTestCaseCleanupFailure;
            Execution.TestCaseFinishedEvent += HandleTestCaseFinished;
            Execution.TestCaseStartingEvent += HandleTestCaseStarting;
            Execution.TestClassCleanupFailureEvent += HandleTestClassCleanupFailure;
            Execution.TestCleanupFailureEvent += HandleTestCleanupFailure;
            Execution.TestCollectionCleanupFailureEvent += HandleTestCollectionCleanupFailure;
            Execution.TestFailedEvent += HandleTestFailed;
            Execution.TestMethodCleanupFailureEvent += HandleTestMethodCleanupFailure;
            Execution.TestPassedEvent += HandleTestPassed;
            Execution.TestSkippedEvent += HandleTestSkipped;
        }

        public ExecutionSummary ExecutionSummary { get; private set; }

        public ManualResetEvent Finished { get; } = new ManualResetEvent(initialState: false);

        public override void Dispose()
        {
            ((IDisposable)Finished).Dispose();
            base.Dispose();
        }

        TestCase FindTestCase(ITestCase testCase)
        {
            if (testCasesMap.TryGetValue(testCase.UniqueID, out var result))
                return result;

            logger.LogError(testCase, "Result reported for unknown test case: {0}", testCase.DisplayName);
            return null;
        }

        private void TryAndReport(string actionDescription, ITestCase testCase, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                logger.LogError(testCase, "Error occured while {0} for test case {1}: {2}", actionDescription, testCase.DisplayName, ex);
            }
        }

        void HandleCancellation(MessageHandlerArgs args)
        {
            if (cancelledThunk())
                args.Stop();
        }

        void HandleErrorMessage(MessageHandlerArgs<IErrorMessage> args)
        {
            ExecutionSummary.Errors++;

            logger.LogError("Catastrophic failure: {0}", ExceptionUtility.CombineMessages(args.Message));

            HandleCancellation(args);
        }

        void HandleTestAssemblyFinished(MessageHandlerArgs<ITestAssemblyFinished> args)
        {
            var assemblyFinished = args.Message;

            ExecutionSummary.Failed = assemblyFinished.TestsFailed;
            ExecutionSummary.Skipped = assemblyFinished.TestsSkipped;
            ExecutionSummary.Time = assemblyFinished.ExecutionTime;
            ExecutionSummary.Total = assemblyFinished.TestsRun;

            Finished.Set();

            HandleCancellation(args);
        }

        void HandleTestFailed(MessageHandlerArgs<ITestFailed> args)
        {
            var testFailed = args.Message;
            var result = MakeVsTestResult(TestOutcome.Failed, testFailed);
            if (result != null)
            {
                result.ErrorMessage = ExceptionUtility.CombineMessages(testFailed);
                result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(testFailed);

                TryAndReport("RecordResult (Fail)", testFailed.TestCase, () => recorder.RecordResult(result));
            }
            else
                logger.LogWarning(testFailed.TestCase, "(Fail) Could not find VS test case for {0} (ID = {1})", testFailed.TestCase.DisplayName, testFailed.TestCase.UniqueID);

            HandleCancellation(args);
        }

        void HandleTestPassed(MessageHandlerArgs<ITestPassed> args)
        {
            var testPassed = args.Message;
            var result = MakeVsTestResult(TestOutcome.Passed, testPassed);
            if (result != null)
                TryAndReport("RecordResult (Pass)", testPassed.TestCase, () => recorder.RecordResult(result));
            else
                logger.LogWarning(testPassed.TestCase, "(Pass) Could not find VS test case for {0} (ID = {1})", testPassed.TestCase.DisplayName, testPassed.TestCase.UniqueID);

            HandleCancellation(args);
        }

        void HandleTestSkipped(MessageHandlerArgs<ITestSkipped> args)
        {
            var testSkipped = args.Message;
            var result = MakeVsTestResult(TestOutcome.Skipped, testSkipped);
            if (result != null)
                TryAndReport("RecordResult (Skip)", testSkipped.TestCase, () => recorder.RecordResult(result));
            else
                logger.LogWarning(testSkipped.TestCase, "(Skip) Could not find VS test case for {0} (ID = {1})", testSkipped.TestCase.DisplayName, testSkipped.TestCase.UniqueID);

            HandleCancellation(args);
        }

        void HandleTestCaseStarting(MessageHandlerArgs<ITestCaseStarting> args)
        {
            var testCaseStarting = args.Message;
            var vsTestCase = FindTestCase(testCaseStarting.TestCase);
            if (vsTestCase != null)
                TryAndReport("RecordStart", testCaseStarting.TestCase, () => recorder.RecordStart(vsTestCase));
            else
                logger.LogWarning(testCaseStarting.TestCase, "(Starting) Could not find VS test case for {0} (ID = {1})", testCaseStarting.TestCase.DisplayName, testCaseStarting.TestCase.UniqueID);

            HandleCancellation(args);
        }

        void HandleTestCaseFinished(MessageHandlerArgs<ITestCaseFinished> args)
        {
            var testCaseFinished = args.Message;
            var vsTestCase = FindTestCase(testCaseFinished.TestCase);
            if (vsTestCase != null)
                TryAndReport("RecordEnd", testCaseFinished.TestCase, () => recorder.RecordEnd(vsTestCase, GetAggregatedTestOutcome(testCaseFinished)));
            else
                logger.LogWarning(testCaseFinished.TestCase, "(Finished) Could not find VS test case for {0} (ID = {1})", testCaseFinished.TestCase.DisplayName, testCaseFinished.TestCase.UniqueID);

            HandleCancellation(args);
        }

        void HandleTestAssemblyCleanupFailure(MessageHandlerArgs<ITestAssemblyCleanupFailure> args)
        {
            ExecutionSummary.Errors++;

            var cleanupFailure = args.Message;
            WriteError($"Test Assembly Cleanup Failure ({cleanupFailure.TestAssembly.Assembly.AssemblyPath})", cleanupFailure, cleanupFailure.TestCases);

            HandleCancellation(args);
        }

        void HandleTestCaseCleanupFailure(MessageHandlerArgs<ITestCaseCleanupFailure> args)
        {
            ExecutionSummary.Errors++;

            var cleanupFailure = args.Message;
            WriteError($"Test Case Cleanup Failure ({cleanupFailure.TestCase.DisplayName})", cleanupFailure, cleanupFailure.TestCases);

            HandleCancellation(args);
        }

        void HandleTestClassCleanupFailure(MessageHandlerArgs<ITestClassCleanupFailure> args)
        {
            ExecutionSummary.Errors++;

            var cleanupFailure = args.Message;
            WriteError($"Test Class Cleanup Failure ({cleanupFailure.TestClass.Class.Name})", cleanupFailure, cleanupFailure.TestCases);

            HandleCancellation(args);
        }

        void HandleTestCollectionCleanupFailure(MessageHandlerArgs<ITestCollectionCleanupFailure> args)
        {
            ExecutionSummary.Errors++;

            var cleanupFailure = args.Message;
            WriteError($"Test Collection Cleanup Failure ({cleanupFailure.TestCollection.DisplayName})", cleanupFailure, cleanupFailure.TestCases);

            HandleCancellation(args);
        }

        void HandleTestCleanupFailure(MessageHandlerArgs<ITestCleanupFailure> args)
        {
            ExecutionSummary.Errors++;

            var cleanupFailure = args.Message;
            WriteError($"Test Cleanup Failure ({cleanupFailure.Test.DisplayName})", cleanupFailure, cleanupFailure.TestCases);

            HandleCancellation(args);
        }

        void HandleTestMethodCleanupFailure(MessageHandlerArgs<ITestMethodCleanupFailure> args)
        {
            ExecutionSummary.Errors++;

            var cleanupFailure = args.Message;
            WriteError($"Test Method Cleanup Failure ({cleanupFailure.TestMethod.Method.Name})", cleanupFailure, cleanupFailure.TestCases);

            HandleCancellation(args);
        }

        void WriteError(string failureName, IFailureInformation failureInfo, IEnumerable<ITestCase> testCases)
        {
            foreach (var testCase in testCases)
            {
                var result = MakeVsTestResult(TestOutcome.Failed, testCase, testCase.DisplayName);
                if (result != null)
                {
                    result.ErrorMessage = $"[{failureName}]: {ExceptionUtility.CombineMessages(failureInfo)}";
                    result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(failureInfo);

                    TryAndReport("RecordEnd (Failure)", testCase, () => recorder.RecordEnd(result.TestCase, result.Outcome));
                    TryAndReport("RecordResult (Failure)", testCase, () => recorder.RecordResult(result));
                }
                else
                    logger.LogWarning(testCase, "(Failure) Could not find VS test case for {0} (ID = {1})", testCase.DisplayName, testCase.UniqueID);
            }
        }

        VsTestResult MakeVsTestResult(TestOutcome outcome, ITestResultMessage testResult)
            => MakeVsTestResult(outcome, testResult.TestCase, testResult.Test.DisplayName, (double)testResult.ExecutionTime, testResult.Output);

        VsTestResult MakeVsTestResult(TestOutcome outcome, ITestSkipped skippedResult)
            => MakeVsTestResult(outcome, skippedResult.TestCase, skippedResult.Test.DisplayName, (double)skippedResult.ExecutionTime, skippedResult.Reason);

        VsTestResult MakeVsTestResult(TestOutcome outcome, ITestCase testCase, string displayName, double executionTime = 0.0, string output = null)
        {
            var vsTestCase = FindTestCase(testCase);
            if (vsTestCase == null)
                return null;

            var result = new VsTestResult(vsTestCase)
            {
#if !WINDOWS_UAP
                ComputerName = Environment.MachineName,
#endif
                DisplayName = displayName,
                Duration = TimeSpan.FromSeconds(executionTime),
                Outcome = outcome,
            };

            // Work around VS considering a test "not run" when the duration is 0
            if (result.Duration.TotalMilliseconds == 0)
                result.Duration = TimeSpan.FromMilliseconds(1);

            if (!string.IsNullOrEmpty(output))
                result.Messages.Add(new VsTestResultMessage(VsTestResultMessage.StandardOutCategory, output));

            return result;
        }

        TestOutcome GetAggregatedTestOutcome(ITestCaseFinished testCaseFinished)
        {
            if (testCaseFinished.TestsRun == 0)
                return TestOutcome.NotFound;
            else if (testCaseFinished.TestsFailed > 0)
                return TestOutcome.Failed;
            else if (testCaseFinished.TestsSkipped > 0)
                return TestOutcome.Skipped;
            else
                return TestOutcome.Passed;
        }

        public override bool OnMessageWithTypes(IMessageSinkMessage message, HashSet<string> messageTypes)
        {
            var result = innerSink.OnMessageWithTypes(message, messageTypes);
            return base.OnMessageWithTypes(message, messageTypes) && result;
        }
    }
}
