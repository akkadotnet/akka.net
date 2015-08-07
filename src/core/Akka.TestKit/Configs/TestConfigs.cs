using Akka.Configuration;

namespace Akka.TestKit.Configs
{
    public static class TestConfigs
    {
        /// <summary>
        /// The default TestKit config
        /// </summary>
        public static Config DefaultConfig
        {
            get { return ConfigurationFactory.FromResource<TestKitBase>("Akka.TestKit.Internal.Reference.conf"); }
        }

        /// <summary>
        /// Configuration for tests that require deterministic control over the AkkaSystem scheduler.
        /// </summary>
        public static Config TestSchedulerConfig
        {
            get { return ConfigurationFactory.FromResource<TestKitBase>("Akka.TestKit.Configs.TestScheduler.conf"); }
        }

    }
}
