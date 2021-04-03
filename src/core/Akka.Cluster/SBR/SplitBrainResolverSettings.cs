//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolverSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Configuration;
using Akka.Util.Internal;

namespace Akka.Cluster.SBR
{
    public sealed class SplitBrainResolverSettings
    {
        public const string KeepMajorityName = "keep-majority";
        public const string LeaseMajorityName = "lease-majority";
        public const string StaticQuorumName = "static-quorum";
        public const string KeepOldestName = "keep-oldest";
        public const string DownAllName = "down-all";

        public static readonly ImmutableHashSet<string> AllStrategyNames = ImmutableHashSet.Create(KeepMajorityName,
            LeaseMajorityName, StaticQuorumName, KeepOldestName, DownAllName);

        private readonly Lazy<string> lazyKeepMajorityRole;
        private readonly Lazy<KeepOldestSettings> lazyKeepOldestSettings;
        private readonly Lazy<LeaseMajoritySettings> lazyLeaseMajoritySettings;
        private readonly Lazy<StaticQuorumSettings> lazyStaticQuorumSettings;

        public SplitBrainResolverSettings(Config config)
        {
            var cc = config.GetConfig("akka.cluster.split-brain-resolver");
            if (cc.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<SplitBrainResolverSettings>(
                    "akka.cluster.split-brain-resolver");

            DowningStableAfter = cc.GetTimeSpan("stable-after");
            if (DowningStableAfter <= TimeSpan.Zero)
                throw new ConfigurationException("'split-brain-resolver.stable-after' must be  >= 0s");


            DowningStrategy = cc.GetString("active-strategy")?.ToLowerInvariant();
            if (!AllStrategyNames.Contains(DowningStrategy))
                throw new ConfigurationException(
                    $"Unknown downing strategy 'split-brain-resolver.active-strategy'=[{DowningStrategy}]. Select one of [{string.Join(", ", AllStrategyNames)}]");

            {
                var key = "down-all-when-unstable";
                switch (cc.GetString(key)?.ToLowerInvariant())
                {
                    case "on":
                        // based on stable-after
                        DownAllWhenUnstable =
                            TimeSpan.FromSeconds(4).Max(new TimeSpan(DowningStableAfter.Ticks * 3 / 4));
                        break;
                    case "off":
                        // disabled
                        DownAllWhenUnstable = TimeSpan.Zero;
                        break;
                    default:
                        DownAllWhenUnstable = cc.GetTimeSpan(key);
                        if (DowningStableAfter <= TimeSpan.Zero)
                            throw new ConfigurationException(
                                $"'split-brain-resolver.{key}' must be  >= 0s or 'off' to disable");
                        break;
                }
            }

            // the individual sub-configs below should only be called when the strategy has been selected

            Config StrategyConfig(string strategyName)
            {
                return cc.GetConfig(strategyName);
            }

            string Role(Config c)
            {
                var r = c.GetString("role");
                if (string.IsNullOrEmpty(r))
                    return null;
                return r;
            }

            lazyKeepMajorityRole = new Lazy<string>(() => { return Role(StrategyConfig(KeepMajorityName)); });

            lazyStaticQuorumSettings = new Lazy<StaticQuorumSettings>(() =>
            {
                var c = StrategyConfig(StaticQuorumName);
                var size = c.GetInt("quorum-size");
                if (size < 1)
                    throw new ConfigurationException(
                        $"'split-brain-resolver.{StaticQuorumName}.quorum-size' must be  >= 1");

                return new StaticQuorumSettings(size, Role(c));
            });

            lazyKeepOldestSettings = new Lazy<KeepOldestSettings>(() =>
            {
                var c = StrategyConfig(KeepOldestName);
                var downIfAlone = c.GetBoolean("down-if-alone");

                return new KeepOldestSettings(downIfAlone, Role(c));
            });

            lazyLeaseMajoritySettings = new Lazy<LeaseMajoritySettings>(() =>
            {
                var c = StrategyConfig(LeaseMajorityName);
                var leaseImplementation = c.GetString("lease-implementation");
                if (string.IsNullOrEmpty(leaseImplementation))
                    throw new ConfigurationException(
                        $"'split-brain-resolver.{LeaseMajorityName}.lease-implementation' must be defined");

                var acquireLeaseDelayForMinority = c.GetTimeSpan("acquire-lease-delay-for-minority");

                var leaseName = c.GetString("lease-name").Trim();
                if (string.IsNullOrEmpty(leaseName))
                    leaseName = null;

                return new LeaseMajoritySettings(leaseImplementation, acquireLeaseDelayForMinority, Role(c), leaseName);
            });
        }

        public TimeSpan DowningStableAfter { get; }

        public string DowningStrategy { get; }

        public TimeSpan DownAllWhenUnstable { get; }

        public string KeepMajorityRole => lazyKeepMajorityRole.Value;

        public StaticQuorumSettings StaticQuorumSettings => lazyStaticQuorumSettings.Value;

        public KeepOldestSettings KeepOldestSettings => lazyKeepOldestSettings.Value;

        public LeaseMajoritySettings LeaseMajoritySettings => lazyLeaseMajoritySettings.Value;
    }

    public sealed class StaticQuorumSettings
    {
        public StaticQuorumSettings(int size, string role)
        {
            Size = size;
            Role = role;
        }

        public int Size { get; }

        public string Role { get; }
    }

    public sealed class KeepOldestSettings
    {
        public KeepOldestSettings(bool downIfAlone, string role)
        {
            DownIfAlone = downIfAlone;
            Role = role;
        }

        public bool DownIfAlone { get; }

        public string Role { get; }
    }

    public sealed class LeaseMajoritySettings
    {
        public LeaseMajoritySettings(string leaseImplementation, TimeSpan acquireLeaseDelayForMinority, string role)
            : this(leaseImplementation, acquireLeaseDelayForMinority, role, null)
        {
        }

        public LeaseMajoritySettings(string leaseImplementation, TimeSpan acquireLeaseDelayForMinority, string role, string leaseName)
        {
            LeaseImplementation = leaseImplementation;
            AcquireLeaseDelayForMinority = acquireLeaseDelayForMinority;
            Role = role;
            LeaseName = leaseName;
        }

        public string SafeLeaseName(string systemName)
        {
            return LeaseName ?? $"{systemName}-akka-sbr";
        }

        public string LeaseImplementation { get; }

        public TimeSpan AcquireLeaseDelayForMinority { get; }

        public string Role { get; }
      
        public string LeaseName { get; }
    }
}
