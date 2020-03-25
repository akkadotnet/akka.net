using System;
using Akka.Configuration;

namespace Akka.Coordination
{
    public sealed class LeaseSettings
    {
        public static LeaseSettings Create(Config config, string leaseName, string ownerName)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<LeaseSettings>();

            return new LeaseSettings(leaseName, ownerName, TimeoutSettings.Create(config), config);
        }

        public string LeaseName { get; }
        public string OwnerName { get; }
        public TimeoutSettings TimeoutSettings { get; }
        public Config LeaseConfig { get; }

        public Type LeaseType { get; }

        public LeaseSettings(string leaseName, string ownerName, TimeoutSettings timeoutSettings, Config leaseConfig)
        {
            LeaseName = leaseName;
            OwnerName = ownerName;
            TimeoutSettings = timeoutSettings;
            LeaseConfig = leaseConfig;

            var leaseClassName = leaseConfig.GetString("lease-class", null);
            if (string.IsNullOrEmpty(leaseClassName))
                throw new ArgumentException("lease-class must not be empty");

            LeaseType = Type.GetType(leaseClassName, true);
        }

        public LeaseSettings WithTimeoutSettings(TimeoutSettings timeoutSettings)
        {
            return new LeaseSettings(LeaseName, OwnerName, timeoutSettings, LeaseConfig);
        }

        public override string ToString()
        {
            return $"LeaseSettings({ LeaseName }, { OwnerName }, { TimeoutSettings })";
        }
    }
}
