using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Akka.TestKit.Hosting.Internals
{
    public class XUnitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _helper;
        private readonly LogLevel _logLevel;

        public XUnitLoggerProvider(ITestOutputHelper helper, LogLevel logLevel)
        {
            _helper = helper;
            _logLevel = logLevel;
        }

        public void Dispose()
        {
            // no-op
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new XUnitLogger(categoryName, _helper, _logLevel);
        }
    }    
}

