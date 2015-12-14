using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{
    public class ThreadPoolDispatcherColdThroughputSpec : ColdDispatcherThroughputSpecBase
    {
        protected override MessageDispatcherConfigurator Configurator()
        {
            return new ThreadPoolDispatcherConfigurator(ConfigurationFactory.Empty, null);
        }
    }

    public class ThreadPoolDispatcherWarmThroughputSpec : WarmDispatcherThroughputSpecBase
    {
        protected override MessageDispatcherConfigurator Configurator()
        {
            return new ThreadPoolDispatcherConfigurator(ConfigurationFactory.Empty, null);
        }
    }
}