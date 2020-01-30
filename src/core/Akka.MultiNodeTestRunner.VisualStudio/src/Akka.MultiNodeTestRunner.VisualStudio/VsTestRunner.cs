using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

#if NETCOREAPP
using System.Text;
using Internal.Microsoft.DotNet.PlatformAbstractions;
using Internal.Microsoft.Extensions.DependencyModel;
#endif

namespace Xunit.Runner.VisualStudio
{
    [FileExtension(".msix")]
    [FileExtension(".appx")]
    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(Constants.ExecutorUri)]
    [ExtensionUri(Constants.ExecutorUri)]
#if !WINDOWS_UAP
    [Category("managed")]
#endif
    public class VsTestRunner : ITestDiscoverer, ITestExecutor
    {
        static readonly IRunnerReporter[] NoReporters = new IRunnerReporter[0];
        static int printedHeader = 0;
        public static TestProperty SerializedTestCaseProperty = GetTestProperty();

#if WINDOWS_UAP || NETCOREAPP
        static readonly AppDomainSupport AppDomainDefaultBehavior = AppDomainSupport.Denied;
#else
        static readonly AppDomainSupport AppDomainDefaultBehavior = AppDomainSupport.Required;
#endif

        static readonly HashSet<string> PlatformAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft.visualstudio.testplatform.unittestframework.dll",
            "microsoft.visualstudio.testplatform.core.dll",
            "microsoft.visualstudio.testplatform.testexecutor.core.dll",
            "microsoft.visualstudio.testplatform.extensions.msappcontaineradapter.dll",
            "microsoft.visualstudio.testplatform.objectmodel.dll",
            "microsoft.visualstudio.testplatform.utilities.dll",
            "vstest.executionengine.appcontainer.exe",
            "vstest.executionengine.appcontainer.x86.exe",
            "xunit.execution.desktop.dll",
            "xunit.execution.dotnet.dll",
            "xunit.execution.win8.dll",
            "xunit.execution.universal.dll",
            "xunit.runner.utility.desktop.dll",
            "xunit.runner.utility.dotnet.dll",
            "Akka.MultiNodeTestRunner.VisualStudio.testadapter.dll",
            "Akka.MultiNodeTestRunner.VisualStudio.uwp.dll",
            "Akka.MultiNodeTestRunner.VisualStudio.win81.dll",
            "Akka.MultiNodeTestRunner.VisualStudio.wpa81.dll",
            "xunit.core.dll",
            "xunit.assert.dll",
            "xunit.dll"
        };

        bool cancelled;

        public void Cancel()
        {
            cancelled = true;
        }

        void ITestDiscoverer.DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {

            Guard.ArgumentNotNull("sources", sources);
            Guard.ArgumentNotNull("logger", logger);
            Guard.ArgumentNotNull("discoverySink", discoverySink);
            Guard.ArgumentValid("sources", "AppX/MSIX not supported for discovery", !ContainsAppX(sources));

            var stopwatch = Stopwatch.StartNew();
            var loggerHelper = new LoggerHelper(logger, stopwatch);

            PrintHeader(loggerHelper);

            var runSettings = RunSettings.Parse(discoveryContext?.RunSettings?.SettingsXml);
            if (!runSettings.IsMatchingTargetFramework())
                return;

            var testPlatformContext = new TestPlatformContext
            {
                // Discovery from command line (non designmode) never requires source information
                // since there is no session or command line runner doesn't send back VSTestCase objects
                // back to adapter.
                RequireSourceInformation = runSettings.CollectSourceInformation,

                // Command line runner could request for Discovery in case of running specific tests. We need
                // the XunitTestCase serialized in this scenario.
                RequireXunitTestProperty = true
            };

            DiscoverTests(
                sources, loggerHelper, testPlatformContext, runSettings,
                (source, discoverer, discoveryOptions) => new VsDiscoverySink(source, discoverer, loggerHelper, discoverySink, discoveryOptions, testPlatformContext, () => cancelled)
            );
        }

        static void PrintHeader(LoggerHelper loggerHelper)
        {
            if (Interlocked.Exchange(ref printedHeader, 1) == 0)
            {
#if NETFRAMEWORK
                var platform = $"Desktop .NET {Environment.Version}";
#elif NETCOREAPP
                var platform = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
#else
#error Unknown target platform
#endif

                var versionAttribute = typeof(VsTestRunner).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                loggerHelper.Log($"xUnit.net VSTest Adapter v{versionAttribute.InformationalVersion} ({IntPtr.Size * 8}-bit {platform})");
            }
        }

        void ITestExecutor.RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("sources", sources);

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);

            PrintHeader(logger);

            var runSettings = RunSettings.Parse(runContext?.RunSettings?.SettingsXml);
            if (!runSettings.IsMatchingTargetFramework())
                return;

            // In the context of Run All tests, commandline runner doesn't require source information or
            // serialized xunit test case property
            var testPlatformContext = new TestPlatformContext
            {
                RequireSourceInformation = runSettings.CollectSourceInformation,
                RequireXunitTestProperty = runSettings.DesignMode
            };

            // In this case, we need to go thru the files manually
            if (ContainsAppX(sources))
            {
#if NETCOREAPP
                var sourcePath = Directory.GetCurrentDirectory();
#else
                var sourcePath = Environment.CurrentDirectory;
#endif
                sources = Directory.GetFiles(sourcePath, "*.dll")
                                   .Where(file => !PlatformAssemblies.Contains(Path.GetFileName(file)))
                                   .ToList();

                ((List<string>)sources).AddRange(Directory.GetFiles(sourcePath, "*.exe")
                                       .Where(file => !PlatformAssemblies.Contains(Path.GetFileName(file))));
            }

            RunTests(
                runContext, frameworkHandle, logger, testPlatformContext, runSettings,
                () => sources.Select(source =>
                {
                    var assemblyFileName = GetAssemblyFileName(source);
                    return new AssemblyRunInfo
                    {
                        AssemblyFileName = assemblyFileName,
                        Configuration = LoadConfiguration(assemblyFileName),
                        TestCases = null // PERF: delay the discovery until we actually require it in RunTestsInAssembly
                    };
                }).ToList()
            );
        }

        void ITestExecutor.RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("tests", tests);
            Guard.ArgumentValid("tests", "AppX not supported in this overload", !ContainsAppX(tests.Select(t => t.Source)));

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);
            var runSettings = RunSettings.Parse(runContext?.RunSettings?.SettingsXml);

            PrintHeader(logger);

            // In the context of Run Specific tests, commandline runner doesn't require source information or
            // serialized xunit test case property
            var testPlatformContext = new TestPlatformContext
            {
                RequireSourceInformation = runSettings.CollectSourceInformation,
                RequireXunitTestProperty = runSettings.DesignMode
            };

            RunTests(
                runContext, frameworkHandle, logger, testPlatformContext, runSettings,
                () => tests.GroupBy(testCase => testCase.Source)
                           .Select(group => new AssemblyRunInfo { AssemblyFileName = group.Key, Configuration = LoadConfiguration(group.Key), TestCases = group.ToList() })
                           .ToList()
            );
        }

        // Helpers

        static bool ContainsAppX(IEnumerable<string> sources)
            => sources.Any(s => string.Compare(Path.GetExtension(s), ".appx", StringComparison.OrdinalIgnoreCase) == 0 ||
                                string.Compare(Path.GetExtension(s), ".msix", StringComparison.OrdinalIgnoreCase) == 0);

        void DiscoverTests<TVisitor>(IEnumerable<string> sources,
                                     LoggerHelper logger,
                                     TestPlatformContext testPlatformContext,
                                     RunSettings runSettings,
                                     Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
                                     Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitComplete = null)
            where TVisitor : IVsDiscoverySink, IDisposable
        {
            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                var internalDiagnosticsMessageSink = DiagnosticMessageSink.ForInternalDiagnostics(logger, runSettings.InternalDiagnostics);

                using (AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), MessageSinkAdapter.Wrap(internalDiagnosticsMessageSink)))
                {
                    foreach (var assemblyFileNameCanBeWithoutAbsolutePath in sources)
                    {
                        var assemblyFileName = GetAssemblyFileName(assemblyFileNameCanBeWithoutAbsolutePath);
                        var configuration = LoadConfiguration(assemblyFileName);
                        var fileName = Path.GetFileNameWithoutExtension(assemblyFileName);
                        var shadowCopy = configuration.ShadowCopyOrDefault;
                        var diagnosticSink = DiagnosticMessageSink.ForDiagnostics(logger, fileName, configuration.DiagnosticMessagesOrDefault);

                        using (var framework = new XunitFrontController(AppDomainDefaultBehavior, assemblyFileName, shadowCopy: shadowCopy, diagnosticMessageSink: MessageSinkAdapter.Wrap(diagnosticSink)))
                            if (!DiscoverTestsInSource(framework, logger, testPlatformContext, runSettings, visitorFactory, visitComplete, assemblyFileName, shadowCopy, configuration))
                                break;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Exception discovering tests: {0}", e.Unwrap());
            }
        }

        bool DiscoverTestsInSource<TVisitor>(XunitFrontController framework,
                                                     LoggerHelper logger,
                                                     TestPlatformContext testPlatformContext,
                                                     RunSettings runSettings,
                                                     Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
                                                     Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitComplete,
                                                     string assemblyFileName,
                                                     bool shadowCopy,
                                                     TestAssemblyConfiguration configuration)
            where TVisitor : IVsDiscoverySink, IDisposable
        {
            if (cancelled)
                return false;

            var fileName = "(unknown assembly)";

            try
            {
                var reporterMessageHandler = GetRunnerReporter(logger, runSettings, new[] { assemblyFileName }).CreateMessageHandler(new VisualStudioRunnerLogger(logger));
                var assembly = new XunitProjectAssembly { AssemblyFilename = assemblyFileName };
                fileName = Path.GetFileNameWithoutExtension(assemblyFileName);

                if (!IsXunitTestAssembly(assemblyFileName))
                {
                    if (configuration.DiagnosticMessagesOrDefault)
                        logger.Log("Skipping: {0} (no reference to xUnit.net)", fileName);
                }
                else
                {
                    var targetFramework = framework.TargetFramework;
                    if (targetFramework.StartsWith("MonoTouch", StringComparison.OrdinalIgnoreCase) ||
                        targetFramework.StartsWith("MonoAndroid", StringComparison.OrdinalIgnoreCase) ||
                        targetFramework.StartsWith("Xamarin.iOS", StringComparison.OrdinalIgnoreCase))
                    {
                        if (configuration.DiagnosticMessagesOrDefault)
                            logger.Log("Skipping: {0} (unsupported target framework '{1}')", fileName, targetFramework);
                    }
                    else
                    {
                        var discoveryOptions = TestFrameworkOptions.ForDiscovery(configuration);

                        using (var visitor = visitorFactory(assemblyFileName, framework, discoveryOptions))
                        {
                            var totalTests = 0;
                            var usingAppDomains = framework.CanUseAppDomains && AppDomainDefaultBehavior != AppDomainSupport.Denied;
                            reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting(assembly, usingAppDomains, shadowCopy, discoveryOptions));

                            try
                            {
                                framework.Find(testPlatformContext.RequireSourceInformation, visitor, discoveryOptions);

                                totalTests = visitor.Finish();

                                visitComplete?.Invoke(assemblyFileName, framework, discoveryOptions, visitor);
                            }
                            finally
                            {
                                reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryFinished(assembly, discoveryOptions, totalTests, totalTests));
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                var ex = e.Unwrap();

                if (ex is InvalidOperationException)
                    logger.LogWarning("Skipping: {0} ({1})", fileName, ex.Message);
                else if (ex is FileNotFoundException fileNotFound)
                    logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName));
#if !WINDOWS_UAP
                else if (ex is FileLoadException fileLoad)
                    logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileLoad.FileName));
#endif
                else
                    logger.LogWarning("Exception discovering tests from {0}: {1}", fileName, ex);
            }

            return true;
        }

        static Stream GetConfigurationStreamForAssembly(string assemblyName)
        {
            // See if there's a directory with the assm name. this might be the case for appx
            if (Directory.Exists(assemblyName))
            {
                if (File.Exists(Path.Combine(assemblyName, $"{assemblyName}.xunit.runner.json")))
                    return File.OpenRead(Path.Combine(assemblyName, $"{assemblyName}.xunit.runner.json"));

                if (File.Exists(Path.Combine(assemblyName, "xunit.runner.json")))
                    return File.OpenRead(Path.Combine(assemblyName, "xunit.runner.json"));
            }

            // Fallback to working dir
            if (File.Exists($"{assemblyName}.xunit.runner.json"))
                return File.OpenRead($"{assemblyName}.xunit.runner.json");

            if (File.Exists("xunit.runner.json"))
                return File.OpenRead("xunit.runner.json");

            return null;
        }

        static TestProperty GetTestProperty()
            => TestProperty.Register("XunitTestCase", "xUnit.net Test Case", typeof(string), typeof(VsTestRunner));

        static bool IsXunitTestAssembly(string assemblyFileName)
        {
            // Don't try to load ourselves (or any test framework assemblies), since we fail (issue #47 in xunit/xunit).
            if (PlatformAssemblies.Contains(Path.GetFileName(assemblyFileName)))
                return false;

#if NETCOREAPP
            return IsXunitPackageReferenced(assemblyFileName);
#else
#if WINDOWS_UAP
            return true; // with .NET Native, all the dependencies are combined, so nothing we can detect
#else
            var assemblyFolder = Path.GetDirectoryName(assemblyFileName);

            return File.Exists(Path.Combine(assemblyFolder, "xunit.dll"))
                || Directory.GetFiles(assemblyFolder, "xunit.execution.*.dll").Length > 0;
#endif
#endif

        }

#if NETCOREAPP
        static bool IsXunitPackageReferenced(string assemblyFileName)
        {
            var depsFile = assemblyFileName.Replace(".dll", ".deps.json");
            if (!File.Exists(depsFile))
                return false;

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(depsFile))))
                {
                    var context = new DependencyContextJsonReader().Read(stream);
                    var xunitLibrary = context.RuntimeLibraries.Where(lib => lib.Name.Equals("xunit") || lib.Name.Equals("xunit.core")).FirstOrDefault();
                    return xunitLibrary != null;
                }
            }
            catch
            {
                return false;
            }
        }
#endif

        static string GetAssemblyFileName(string source)
        {
#if !WINDOWS_UAP
            return Path.GetFullPath(source);
#else
            return source;
#endif
        }

        static TestAssemblyConfiguration LoadConfiguration(string assemblyName)
        {
#if WINDOWS_UAP
            var stream = GetConfigurationStreamForAssembly(assemblyName);
            return stream == null ? new TestAssemblyConfiguration() : ConfigReader.Load(stream);
#else
            return ConfigReader.Load(assemblyName);
#endif
        }

        void RunTests(IRunContext runContext,
                      IFrameworkHandle frameworkHandle,
                      LoggerHelper logger,
                      TestPlatformContext testPlatformContext,
                      RunSettings runSettings,
                      Func<List<AssemblyRunInfo>> getRunInfos)
        {
            Guard.ArgumentNotNull("runContext", runContext);
            Guard.ArgumentNotNull("frameworkHandle", frameworkHandle);

            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                cancelled = false;

                var runInfos = getRunInfos();
                var parallelizeAssemblies = !runSettings.DisableParallelization && runInfos.All(runInfo => runInfo.Configuration.ParallelizeAssemblyOrDefault);
                var reporterMessageHandler = MessageSinkWithTypesAdapter.Wrap(GetRunnerReporter(logger, runSettings, runInfos.Select(ari => ari.AssemblyFileName))
                                                                        .CreateMessageHandler(new VisualStudioRunnerLogger(logger)));
                var internalDiagnosticsMessageSink = DiagnosticMessageSink.ForInternalDiagnostics(logger, runSettings.InternalDiagnostics);

                using (AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), MessageSinkAdapter.Wrap(internalDiagnosticsMessageSink)))
                {
                    if (parallelizeAssemblies)
                        runInfos
                            .Select(runInfo => RunTestsInAssemblyAsync(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo))
                            .ToList()
                            .ForEach(@event => @event.WaitOne());
                    else
                        runInfos
                            .ForEach(runInfo => RunTestsInAssembly(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo));
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Catastrophic failure: {0}", ex);
            }
        }

        void RunTestsInAssembly(IRunContext runContext,
                                IFrameworkHandle frameworkHandle,
                                LoggerHelper logger,
                                TestPlatformContext testPlatformContext,
                                RunSettings runSettings,
                                IMessageSinkWithTypes reporterMessageHandler,
                                AssemblyRunInfo runInfo)
        {
            if (cancelled)
                return;

            var assemblyDisplayName = "(unknown assembly)";

            try
            {
                var assembly = new XunitProjectAssembly { AssemblyFilename = runInfo.AssemblyFileName };
                var assemblyFileName = runInfo.AssemblyFileName;
                assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
                var configuration = runInfo.Configuration;
                var shadowCopy = configuration.ShadowCopyOrDefault;

                var appDomain = assembly.Configuration.AppDomain ?? AppDomainDefaultBehavior;
                var longRunningSeconds = assembly.Configuration.LongRunningTestSecondsOrDefault;

                if (runSettings.DisableAppDomain)
                    appDomain = AppDomainSupport.Denied;

#if WINDOWS_UAP
                // For AppX Apps, use the package location
                assemblyFileName = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(assemblyFileName));
#endif

                var diagnosticSink = DiagnosticMessageSink.ForDiagnostics(logger, assemblyDisplayName, runInfo.Configuration.DiagnosticMessagesOrDefault);
                var diagnosticMessageSink = MessageSinkAdapter.Wrap(diagnosticSink);
                using (var controller = new XunitFrontController(appDomain, assemblyFileName, shadowCopy: shadowCopy, diagnosticMessageSink: diagnosticMessageSink))
                {
                    var testCasesMap = new Dictionary<string, TestCase>();
                    var testCases = new List<ITestCase>();
                    if (runInfo.TestCases == null || !runInfo.TestCases.Any())
                    {
                        // Discover tests
                        var assemblyDiscoveredInfo = new AssemblyDiscoveredInfo();
                        DiscoverTestsInSource(controller, logger, testPlatformContext, runSettings,
                            (source, discoverer, discoveryOptions) => new VsExecutionDiscoverySink(() => cancelled),
                            (source, discoverer, discoveryOptions, visitor) =>
                            {
                                if (discoveryOptions.GetInternalDiagnosticMessagesOrDefault())
                                    foreach (var testCase in visitor.TestCases)
                                        logger.Log(testCase, "Discovered [execution] test case '{0}' (ID = '{1}')",
                                            testCase.DisplayName, testCase.UniqueID);

                                assemblyDiscoveredInfo = new AssemblyDiscoveredInfo
                                {
                                    AssemblyFileName = source,
                                    DiscoveredTestCases = GetVsTestCases(source, discoverer, visitor, logger, testPlatformContext)
                                };
                            },
                            assemblyFileName,
                            shadowCopy,
                            configuration
                        );

                        if (assemblyDiscoveredInfo.DiscoveredTestCases == null || !assemblyDiscoveredInfo.DiscoveredTestCases.Any())
                        {
                            if (configuration.InternalDiagnosticMessagesOrDefault)
                                logger.LogWarning("Skipping '{0}' since no tests were found during discovery [execution].", assemblyDiscoveredInfo.AssemblyFileName);

                            return;
                        }

                        // Filter tests
                        var traitNames = new HashSet<string>(assemblyDiscoveredInfo.DiscoveredTestCases.SelectMany(testCase => testCase.TraitNames));
                        var filter = new TestCaseFilter(runContext, logger, assemblyDiscoveredInfo.AssemblyFileName, traitNames);
                        var filteredTestCases = assemblyDiscoveredInfo.DiscoveredTestCases.Where(dtc => filter.MatchTestCase(dtc.VSTestCase)).ToList();

                        foreach (var filteredTestCase in filteredTestCases)
                        {
                            var uniqueID = filteredTestCase.UniqueID;
                            if (testCasesMap.ContainsKey(uniqueID))
                                logger.LogWarning(filteredTestCase.TestCase, "Skipping test case with duplicate ID '{0}' ('{1}' and '{2}')", uniqueID, testCasesMap[uniqueID].DisplayName, filteredTestCase.VSTestCase.DisplayName);
                            else
                            {
                                testCasesMap.Add(uniqueID, filteredTestCase.VSTestCase);
                                testCases.Add(filteredTestCase.TestCase);
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            var serializations = runInfo.TestCases
                                                        .Select(tc => tc.GetPropertyValue<string>(SerializedTestCaseProperty, null))
                                                        .ToList();

                            if (configuration.InternalDiagnosticMessagesOrDefault)
                                logger.LogWithSource(runInfo.AssemblyFileName, "Deserializing {0} test case(s):{1}{2}",
                                                                                serializations.Count,
                                                                                Environment.NewLine,
                                                                                string.Join(Environment.NewLine, serializations.Select(x => $"  {x}")));

                            var deserializedTestCasesByUniqueId = controller.BulkDeserialize(serializations);

                            if (deserializedTestCasesByUniqueId == null)
                                logger.LogErrorWithSource(assemblyFileName, "Received null response from BulkDeserialize");
                            else
                            {
                                for (var idx = 0; idx < runInfo.TestCases.Count; ++idx)
                                {
                                    try
                                    {
                                        var kvp = deserializedTestCasesByUniqueId[idx];
                                        var vsTestCase = runInfo.TestCases[idx];

                                        if (kvp.Value == null)
                                        {
                                            logger.LogErrorWithSource(assemblyFileName, "Test case {0} failed to deserialize: {1}", vsTestCase.DisplayName, kvp.Key);
                                        }
                                        else
                                        {
                                            testCasesMap.Add(kvp.Key, vsTestCase);
                                            testCases.Add(kvp.Value);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogErrorWithSource(assemblyFileName, "Catastrophic error deserializing item #{0}: {1}", idx, ex);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarningWithSource(assemblyFileName, "Catastrophic error during deserialization: {0}", ex);
                        }
                    }

                    // Execute tests
                    var executionOptions = TestFrameworkOptions.ForExecution(runInfo.Configuration);
                    if (runSettings.DisableParallelization)
                    {
                        executionOptions.SetSynchronousMessageReporting(true);
                        executionOptions.SetDisableParallelization(true);
                    }

                    reporterMessageHandler.OnMessage(new TestAssemblyExecutionStarting(assembly, executionOptions));

                    using (var vsExecutionSink = new VsExecutionSink(reporterMessageHandler, frameworkHandle, logger, testCasesMap, executionOptions, () => cancelled))
                    {
                        IExecutionSink resultsSink = vsExecutionSink;
                        if (longRunningSeconds > 0)
                            resultsSink = new DelegatingLongRunningTestDetectionSink(resultsSink, TimeSpan.FromSeconds(longRunningSeconds), diagnosticSink);

                        controller.RunTests(testCases, resultsSink, executionOptions);
                        resultsSink.Finished.WaitOne();

                        reporterMessageHandler.OnMessage(new TestAssemblyExecutionFinished(assembly, executionOptions, resultsSink.ExecutionSummary));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("{0}: Catastrophic failure: {1}", assemblyDisplayName, ex);
            }
        }

        ManualResetEvent RunTestsInAssemblyAsync(IRunContext runContext,
                                                 IFrameworkHandle frameworkHandle,
                                                 LoggerHelper logger,
                                                 TestPlatformContext testPlatformContext,
                                                 RunSettings runSettings,
                                                 IMessageSinkWithTypes reporterMessageHandler,
                                                 AssemblyRunInfo runInfo)
        {
            var @event = new ManualResetEvent(initialState: false);
            void handler()
            {
                try
                {
                    RunTestsInAssembly(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo);
                }
                finally
                {
                    @event.Set();
                }
            }

            ThreadPool.QueueUserWorkItem(_ => handler());

            return @event;
        }

        public static IRunnerReporter GetRunnerReporter(LoggerHelper logger, RunSettings runSettings, IEnumerable<string> assemblyFileNames)
        {
            var reporter = default(IRunnerReporter);
            var availableReporters = new Lazy<IReadOnlyList<IRunnerReporter>>(() => GetAvailableRunnerReporters(assemblyFileNames));

            try
            {
                if (!string.IsNullOrEmpty(runSettings.ReporterSwitch))
                {
                    reporter = availableReporters.Value.FirstOrDefault(r => string.Equals(r.RunnerSwitch, runSettings.ReporterSwitch, StringComparison.OrdinalIgnoreCase));
                    if (reporter is null)
                        logger.LogWarning("Could not find requested reporter '{0}'", runSettings.ReporterSwitch);
                }

                if (reporter is null && !runSettings.NoAutoReporters)
                    reporter = availableReporters.Value.FirstOrDefault(r => r.IsEnvironmentallyEnabled);
            }
            catch { }

            return reporter ?? new DefaultRunnerReporterWithTypes();
        }

        static IReadOnlyList<IRunnerReporter> GetAvailableRunnerReporters(IEnumerable<string> sources)
        {
#if WINDOWS_UAP
            // No reporters on UWP
            return NoReporters;
#elif NETCOREAPP
            // Combine all input libs and merge their contexts to find the potential reporters
            var result = new List<IRunnerReporter>();
            var dcjr = new DependencyContextJsonReader();
            var deps = sources.Select(Path.GetFullPath)
                              .Select(s => s.Replace(".dll", ".deps.json"))
                              .Where(File.Exists)
                              .Select(f => new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(f))))
                              .Select(dcjr.Read);
            var ctx = deps.Aggregate(DependencyContext.Default, (context, dependencyContext) => context.Merge(dependencyContext));
            dcjr.Dispose();

            var depsAssms = ctx.GetRuntimeAssemblyNames(RuntimeEnvironment.GetRuntimeIdentifier())
                               .ToList();

            // Make sure to also check assemblies within the directory of the sources
            var dllsInSources = sources.Select(Path.GetFullPath)
                                       .Select(Path.GetDirectoryName)
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .SelectMany(p => Directory.GetFiles(p, "*.dll").Select(f => Path.Combine(p, f)))
                                       .Select(f => new AssemblyName { Name = Path.GetFileNameWithoutExtension(f) })
                                       .ToList();

            foreach (var assemblyName in depsAssms.Concat(dllsInSources))
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    foreach (var type in assembly.DefinedTypes)
                    {
#pragma warning disable CS0618
                        if (type == null || type.IsAbstract || type == typeof(DefaultRunnerReporter).GetTypeInfo() || type == typeof(DefaultRunnerReporterWithTypes).GetTypeInfo() || type.ImplementedInterfaces.All(i => i != typeof(IRunnerReporter)))
                            continue;
#pragma warning restore CS0618

                        var ctor = type.DeclaredConstructors.FirstOrDefault(c => c.GetParameters().Length == 0);
                        if (ctor == null)
                        {
                            ConsoleHelper.SetForegroundColor(ConsoleColor.Yellow);
                            Console.WriteLine($"Type {type.FullName} in assembly {assembly} appears to be a runner reporter, but does not have an empty constructor.");
                            ConsoleHelper.ResetColor();
                            continue;
                        }

                        result.Add((IRunnerReporter)ctor.Invoke(new object[0]));
                    }
                }
                catch
                {
                    continue;
                }
            }

            return result;
#elif NETFRAMEWORK
            var result = new List<IRunnerReporter>();
            var runnerPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetLocalCodeBase());
            var runnerReporterInterfaceAssemblyFullName = typeof(IRunnerReporter).Assembly.GetName().FullName;

            foreach (var dllFile in Directory.GetFiles(runnerPath, "*.dll").Select(f => Path.Combine(runnerPath, f)))
            {
                Type[] types;

                try
                {
                    var assembly = Assembly.LoadFile(dllFile);

                    // Calling Assembly.GetTypes can be very expensive, while Assembly.GetReferencedAssemblies
                    // is relatively cheap.  We can avoid loading types for assemblies that couldn't possibly
                    // reference IRunnerReporter.
                    if (!assembly.GetReferencedAssemblies().Where(name => name.FullName == runnerReporterInterfaceAssemblyFullName).Any())
                        continue;

                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
#pragma warning disable CS0618
                    if (type == null || type.IsAbstract || type == typeof(DefaultRunnerReporter) || type == typeof(DefaultRunnerReporterWithTypes) || !type.GetInterfaces().Any(t => t == typeof(IRunnerReporter)))
                        continue;
#pragma warning restore CS0618

                    var ctor = type.GetConstructor(new Type[0]);
                    if (ctor == null)
                    {
                        ConsoleHelper.SetForegroundColor(ConsoleColor.Yellow);
                        Console.WriteLine($"Type {type.FullName} in assembly {dllFile} appears to be a runner reporter, but does not have an empty constructor.");
                        ConsoleHelper.ResetColor();
                        continue;
                    }

                    result.Add((IRunnerReporter)ctor.Invoke(new object[0]));
                }
            }

            return result;
#endif
        }

        IList<DiscoveredTestCase> GetVsTestCases(string source, ITestFrameworkDiscoverer discoverer, VsExecutionDiscoverySink visitor, LoggerHelper logger, TestPlatformContext testPlatformContext)
        {
            var descriptorProvider = (discoverer as ITestCaseDescriptorProvider) ?? new DefaultTestCaseDescriptorProvider(discoverer);
            var testCases = visitor.TestCases;
            var descriptors = descriptorProvider.GetTestCaseDescriptors(testCases, false);
            var results = new List<DiscoveredTestCase>(descriptors.Count);

            for (var idx = 0; idx < descriptors.Count; ++idx)
            {
                var testCase = new DiscoveredTestCase(source, descriptors[idx], testCases[idx], logger, testPlatformContext);
                if (testCase.VSTestCase != null)
                    results.Add(testCase);
            }

            return results;
        }

        class AssemblyDiscoveredInfo
        {
            public string AssemblyFileName;
            public IList<DiscoveredTestCase> DiscoveredTestCases;
        }

        class DiscoveredTestCase
        {
            public string Name { get; }

            public IEnumerable<string> TraitNames { get; }

            public TestCase VSTestCase { get; }

            public ITestCase TestCase { get; }

            public string UniqueID { get; }

            public DiscoveredTestCase(string source, TestCaseDescriptor descriptor, ITestCase testCase, LoggerHelper logger, TestPlatformContext testPlatformContext)
            {
                Name = $"{descriptor.ClassName}.{descriptor.MethodName} ({descriptor.UniqueID})";
                TestCase = testCase;
                UniqueID = descriptor.UniqueID;
                VSTestCase = VsDiscoverySink.CreateVsTestCase(source, descriptor, logger, testPlatformContext);
                TraitNames = descriptor.Traits.Keys;
            }
        }
    }
}
