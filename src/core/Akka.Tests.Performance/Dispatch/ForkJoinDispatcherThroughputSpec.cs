using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{
    public class ForkJoinDispatcherColdThroughputSpec : ColdDispatcherThroughputSpecBase
    {
        public static Config DispatcherConfiguration => ConfigurationFactory.ParseString(@"
                    dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
                        thread-count = 3 
                        #deadlock-timeout = 3s 
                        threadtype = background 
                    }
        ");

        protected override MessageDispatcherConfigurator Configurator()
        {
            return new ForkJoinDispatcherConfigurator(DispatcherConfiguration,null);
        }
    }

    public class ForkJoinDispatcherWarmThroughputSpec : WarmDispatcherThroughputSpecBase
    {
        public static Config DispatcherConfiguration => ConfigurationFactory.ParseString(@"
                    dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
                        thread-count = 3 
                        #deadlock-timeout = 3s 
                        threadtype = background 
                    }
        ");

        protected override MessageDispatcherConfigurator Configurator()
        {
            return new ForkJoinDispatcherConfigurator(DispatcherConfiguration, null);
        }
    }
}