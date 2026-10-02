using Microsoft.Extensions.Logging;

namespace Akka.Hosting.TestKit.Internals
{
    public class XUnitLoggerProvider : ILoggerProvider
    {
        private readonly XunitTestOutputHelper _helper;
        private readonly LogLevel _logLevel;

        public XUnitLoggerProvider(XunitTestOutputHelper helper, LogLevel logLevel)
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
