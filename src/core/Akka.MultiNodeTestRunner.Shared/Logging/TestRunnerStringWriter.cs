using System.IO;
using Akka.Event;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    /// <summary>
    /// Used for standard-out redirection within the individual NodeTestRunner processes,
    /// so we can capture everything logger by <see cref="StandardOutLogger"/>
    /// </summary>
    public class TestRunnerStringWriter : StringWriter
    {
        private readonly ITestRunnerLogger _logger;

        public TestRunnerStringWriter(ITestRunnerLogger logger)
        {
            _logger = logger;
        }

        public override void Write(string value)
        {
            _logger.Write(value);
        }
    }
}
