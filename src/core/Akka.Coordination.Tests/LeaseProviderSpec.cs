//-----------------------------------------------------------------------
// <copyright file="LeaseProviderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Coordination.Tests
{
    public class LeaseProviderSpec : AkkaSpec
    {
        #region Infrastructure

        public class LeaseA : Lease
        {
            public LeaseA(LeaseSettings settings)
                : base(settings)
            {

            }

            public override Task<bool> Acquire()
            {
                return Task.FromResult(false);
            }

            public override Task<bool> Release()
            {
                return Task.FromResult(false);
            }

            public override Task<bool> Acquire(Action<Exception> leaseLostCallback)
            {
                return Task.FromResult(false);
            }

            public override bool CheckLease()
            {
                return false;
            }
        }

        public class LeaseB : Lease
        {
            public LeaseB(LeaseSettings settings)
                : base(settings)
            {

            }

            public override Task<bool> Acquire()
            {
                return Task.FromResult(false);
            }

            public override Task<bool> Release()
            {
                return Task.FromResult(false);
            }

            public override Task<bool> Acquire(Action<Exception> leaseLostCallback)
            {
                return Task.FromResult(false);
            }

            public override bool CheckLease()
            {
                return false;
            }
        }

        public class LeaseC : Lease
        {
            public LeaseC()
                : base(null)
            {
            }

            public override Task<bool> Acquire()
            {
                return Task.FromResult(false);
            }

            public override Task<bool> Release()
            {
                return Task.FromResult(false);
            }

            public override Task<bool> Acquire(Action<Exception> leaseLostCallback)
            {
                return Task.FromResult(false);
            }

            public override bool CheckLease()
            {
                return false;
            }
        }

        #endregion

        #region Test Config
        public static Config LeaseConfiguration
        {
            get { return ConfigurationFactory.ParseString(@"

lease-a {
    lease-class = ""Akka.Coordination.Tests.LeaseProviderSpec+LeaseA, Akka.Coordination.Tests""
    key1 = value1
    heartbeat-timeout = 100s
    heartbeat-interval = 1s
    lease-operation-timeout = 2s
  }

  lease-b {
    lease-class = ""Akka.Coordination.Tests.LeaseProviderSpec+LeaseB, Akka.Coordination.Tests""
    key2 = value2
    heartbeat-timeout = 120s
    heartbeat-interval = 1s
    lease-operation-timeout = 2s
  }

  lease-missing {
  }

  lease-unknown {
    lease-class = ""foo.wrong.ClassName""
    heartbeat-timeout = 120s
    heartbeat-interval = 1s
    lease-operation-timeout = 2s
  }

  lease-missing-constructor {
    lease-class = ""Akka.Coordination.Tests.LeaseProviderSpec+LeaseC, Akka.Coordination.Tests""
    heartbeat-timeout = 120s
    heartbeat-interval = 1s
    lease-operation-timeout = 2s
  }

  lease-fallback-to-defaults {
    lease-class = ""Akka.Coordination.Tests.LeaseProviderSpec+LeaseA, Akka.Coordination.Tests""
  }
            "); }
        }

        #endregion

        public LeaseProviderSpec(ITestOutputHelper helper) : base(LeaseConfiguration, helper) { }

        #region Tests

        [Fact]
        public void LeaseProvider_must_load_lease_implementation()
        {
            var leaseA = LeaseProvider.Get(Sys).GetLease("a", "lease-a", "owner1");
            leaseA.Should().BeOfType<LeaseA>();
            leaseA.Settings.LeaseName.ShouldBe("a");
            leaseA.Settings.OwnerName.ShouldBe("owner1");
            leaseA.Settings.LeaseConfig.GetString("key1").ShouldBe("value1");
            leaseA.Settings.TimeoutSettings.HeartbeatTimeout.ShouldBe(TimeSpan.FromSeconds(100));
            leaseA.Settings.TimeoutSettings.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(1));
            leaseA.Settings.TimeoutSettings.OperationTimeout.ShouldBe(TimeSpan.FromSeconds(2));

            var leaseB = LeaseProvider.Get(Sys).GetLease("b", "lease-b", "owner2");
            leaseB.Should().BeOfType<LeaseB>();
            leaseB.Settings.LeaseName.ShouldBe("b");
            leaseB.Settings.OwnerName.ShouldBe("owner2");
            leaseB.Settings.LeaseConfig.GetString("key2").ShouldBe("value2");
        }

        [Fact]
        public void LeaseProvider_must_load_defaults_for_timeouts_if_not_specified()
        {
            var defaults = LeaseProvider.Get(Sys).GetLease("a", "lease-fallback-to-defaults", "owner1");
            defaults.Settings.TimeoutSettings.OperationTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            defaults.Settings.TimeoutSettings.HeartbeatTimeout.ShouldBe(TimeSpan.FromSeconds(120));
            defaults.Settings.TimeoutSettings.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(12));
        }

        [Fact]
        public void LeaseProvider_must_return_same_instance_for_same_leaseName_configPath_and_owner()
        {
            var leaseA1 = LeaseProvider.Get(Sys).GetLease("a2", "lease-a", "owner1");
            var leaseA2 = LeaseProvider.Get(Sys).GetLease("a2", "lease-a", "owner1");
            leaseA1.Should().BeSameAs(leaseA2);
        }

        [Fact]
        public void LeaseProvider_must_return_different_instance_for_different_leaseName()
        {
            var leaseA1 = LeaseProvider.Get(Sys).GetLease("a3", "lease-a", "owner1");
            var leaseA2 = LeaseProvider.Get(Sys).GetLease("a3b", "lease-a", "owner1");
            leaseA1.Should().NotBeSameAs(leaseA2);
        }

        [Fact]
        public void LeaseProvider_must_return_different_instance_for_different_ownerName()
        {
            var leaseA1 = LeaseProvider.Get(Sys).GetLease("a4", "lease-a", "owner1");
            var leaseA2 = LeaseProvider.Get(Sys).GetLease("a4", "lease-a", "owner2");
            leaseA1.Should().NotBeSameAs(leaseA2);
        }

        [Fact]
        public void LeaseProvider_must_throw_if_missing_lease_class_config()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                LeaseProvider.Get(Sys).GetLease("x", "lease-missing", "owner1");
            }).Message.Should().Contain("lease-class must not be empty");
        }

        [Fact]
        public void LeaseProvider_must_throw_if_unknown_lease_class_config()
        {
            Assert.Throws<TypeLoadException>(() =>
            {
                LeaseProvider.Get(Sys).GetLease("x", "lease-unknown", "owner1");
            });
        }

        [Fact]
        public void LeaseProvider_must_throw_if_missing_lease_class_constructor()
        {
            EventFilter.Exception<MissingMethodException>(contains: "Invalid lease configuration for leaseName").ExpectOne(() =>
            {
                Assert.Throws<MissingMethodException>(() =>
                {
                    LeaseProvider.Get(Sys).GetLease("x", "lease-missing-constructor", "owner1");
                });
            });
        }

        #endregion
    }
}