//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolverProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Coordination;

namespace Akka.Cluster.SBR
{
    /// <summary>
    ///     Enabled with configuration:
    ///     {
    ///     akka.cluster.downing-provider-class = "Akka.Cluster.SBR.SplitBrainResolverProvider"
    ///     }
    /// </summary>
    public class SplitBrainResolverProvider : IDowningProvider
    {
        private readonly SplitBrainResolverSettings _settings;
        private readonly ActorSystem _system;
        private readonly Cluster _cluster;

        public SplitBrainResolverProvider(ActorSystem system, Cluster cluster)
        {
            _system = system;
            _settings = new SplitBrainResolverSettings(system.Settings.Config);
            _cluster = cluster;
        }

        public TimeSpan DownRemovalMargin
        {
            get
            {
                // if down-removal-margin is defined we let it trump stable-after to allow
                // for two different values for SBR downing and cluster tool stop/start after downing
#pragma warning disable CS0618 // Type or member is obsolete
                var drm = Cluster.Get(_system).Settings.DownRemovalMargin;
#pragma warning restore CS0618 // Type or member is obsolete
                if (drm != TimeSpan.Zero)
                    return drm;
                return _settings.DowningStableAfter;
            }
        }

        public Props DowningActorProps
        {
            get
            {
                DowningStrategy strategy;
                switch (_settings.DowningStrategy)
                {
                    case SplitBrainResolverSettings.KeepMajorityName:
                        strategy = new KeepMajority(_settings.KeepMajorityRole);
                        break;
                    case SplitBrainResolverSettings.StaticQuorumName:
                        var sqs = _settings.StaticQuorumSettings;
                        strategy = new StaticQuorum(sqs.Size, sqs.Role);
                        break;
                    case SplitBrainResolverSettings.KeepOldestName:
                        var kos = _settings.KeepOldestSettings;
                        strategy = new KeepOldest(kos.DownIfAlone, kos.Role);
                        break;
                    case SplitBrainResolverSettings.DownAllName:
                        strategy = new DownAllNodes();
                        break;
                    case SplitBrainResolverSettings.LeaseMajorityName:
                        var lms = _settings.LeaseMajoritySettings;
                        var leaseOwnerName = Cluster.Get(_system).SelfUniqueAddress.Address.HostPort();

                        var leaseName = lms.SafeLeaseName(_system.Name);
                        var lease = LeaseProvider.Get(_system).GetLease(leaseName, lms.LeaseImplementation, leaseOwnerName);

                        strategy = new LeaseMajority(lms.Role, lease, lms.AcquireLeaseDelayForMinority, lms.ReleaseAfter);
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                return SplitBrainResolver.Props2(_settings.DowningStableAfter, strategy, _cluster);
            }
        }
    }
}
