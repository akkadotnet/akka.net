using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Akka.NodeTestRunner
{
    public class NodeRunner
    {
        public int Run(NodeRunnerSettings settings, TextWriter consoleOut)
        {
            var configPath = SetupAppConfig(settings);

            using (var controller = new Xunit2(new NullSourceInformationProvider(), settings.AssemblyName, configPath))
            {
                using (var sink = new Sink(settings.NodeIndex, consoleOut))
                {
                    using (var discovery = new Discovery(settings.AssemblyName, settings.TestClass))
                    {
                        try
                        {
                            controller.Find(true, discovery, TestFrameworkOptions.ForDiscovery());
                            discovery.Finished.WaitOne();
                            controller.RunTests(discovery.TestCases, sink, TestFrameworkOptions.ForExecution());
                        }
                        catch (AggregateException ex)
                        {
                            var specFail = new SpecFail(settings.NodeIndex, settings.TestClass);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            foreach (var innerEx in ex.Flatten().InnerExceptions)
                            {
                                specFail.FailureExceptionTypes.Add(innerEx.GetType().ToString());
                                specFail.FailureMessages.Add(innerEx.Message);
                                specFail.FailureStackTraces.Add(innerEx.StackTrace);
                            }
                            consoleOut.WriteLine(specFail);
                            return 1; //signal failure
                        }

                        catch (Exception ex)
                        {
                            var specFail = new SpecFail(settings.NodeIndex, settings.TestClass);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            consoleOut.WriteLine(specFail);
                            return 1; //signal failure
                        }
                        sink.Finished.WaitOne();
                        return sink.Passed ? 0 : 1;
                    }
                }
            }
        }

        private string SetupAppConfig(NodeRunnerSettings settings)
        {
            var info = new FileInfo("config-" + settings.NodeIndex + ".config");

            if (info.Exists)
            {
                info.Delete();
            }

            using (var file = info.OpenWrite())
            {
                var appSettings = new Dictionary<string, string>
                {
                    {"multinode.test-assembly", settings.AssemblyName},
                    {"multinode.test-class", settings.TestClass},
                    {"multinode.test-method", settings.TestMethod},
                    {"multinode.max-nodes", settings.MaxNodes.ToString()},
                    {"multinode.server-host", settings.ServerHost},
                    {"multinode.host", settings.Host},
                    {"multinode.index", settings.NodeIndex.ToString()}
                }
                    .Select(pair => string.Format("<add key=\"{0}\" value=\"{1}\" />", pair.Key, pair.Value))
                    .ToList();

                var config = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<configuration>
<appSettings>
" + string.Join("\r\n", appSettings) + @"
</appSettings>
</configuration>";

                var configBytes = Encoding.ASCII.GetBytes(config);
                file.Write(configBytes, 0, configBytes.Length);
            }

            return info.FullName;
        }
    }
}
