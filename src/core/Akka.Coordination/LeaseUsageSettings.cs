using System;

namespace Akka.Coordination
{
    public sealed class LeaseUsageSettings
    {
        public string LeaseImplementation { get; }
        public TimeSpan LeaseRetryInterval { get; }

        public LeaseUsageSettings(string leaseImplementation, TimeSpan leaseRetryInterval)
        {
            LeaseImplementation = leaseImplementation;
            LeaseRetryInterval = leaseRetryInterval;
        }

        public override string ToString()
        {
            return $"LeaseUsageSettings({ LeaseImplementation }, { LeaseRetryInterval })";
        }
    }
}
