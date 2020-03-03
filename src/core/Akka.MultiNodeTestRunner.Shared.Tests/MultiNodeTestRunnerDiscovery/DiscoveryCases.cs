//-----------------------------------------------------------------------
// <copyright file="DiscoveryCases.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Remote.TestKit;

namespace Akka.MultiNodeTestRunner.Shared.Tests.MultiNodeTestRunnerDiscovery
{
    public class DiscoveryCases
    {
        public class NoAbstractClassesConfig : MultiNodeConfig
        {
            public RoleName Dummy { get; set; }

            public NoAbstractClassesConfig()
            {
                Dummy = Role("dummy");
            }
        }

        public abstract class NoAbstractClassesSpec
        {
            public NoAbstractClassesSpec(NoAbstractClassesConfig config)
            {
            }

            [MultiNodeFact(Skip = "Only for discovery tests")]
            public void Dummy()
            {
            }
        }

        public class DeeplyInheritedConfig : MultiNodeConfig
        {
            public RoleName DeeplyInheritedChildRole { get; set; }

            public DeeplyInheritedConfig()
            {
                DeeplyInheritedChildRole = Role("DeeplyInheritedChildRole");
            }
        }

        public abstract class DeeplyInheritedBaseSpec
        {
            public DeeplyInheritedBaseSpec(DeeplyInheritedConfig config)
            {
            }

            [MultiNodeFact(Skip = "Only for discovery tests")]
            public void Dummy()
            {
            }
        }

        public abstract class DeeplyInheritedMediumSpec : DeeplyInheritedBaseSpec
        {
            public DeeplyInheritedMediumSpec(DeeplyInheritedConfig config) : base(config)
            {
            }
        }

        public class DeeplyInheritedChildSpec : DeeplyInheritedMediumSpec
        {
            public DeeplyInheritedChildSpec(DeeplyInheritedConfig config) : base(config)
            {
            }
        }

        /// <summary>
        /// According to the discovery rules, the concrete class doesn't explicitly need
        /// an instance of its configuration. Only the sub-class does.
        /// </summary>
        public class DefaultConstructorOnDerivedClassSpec : DeeplyInheritedMediumSpec
        {
            public DefaultConstructorOnDerivedClassSpec() : base(new DeeplyInheritedConfig())
            {
                
            }
        }

        public class FloodyConfig : MultiNodeConfig
        {
            public RoleName Role1 { get; set; }
            public RoleName Role2 { get; set; }
            public RoleName Role3 { get; set; }
            public RoleName Role4 { get; set; }
            public RoleName Role5 { get; set; }

            public FloodyConfig()
            {
                Role1 = Role("Role1");
                Role2 = Role("Role2");
                Role3 = Role("Role3");
                Role4 = Role("Role4");
                Role5 = Role("Role5");
            }
        }

        public abstract class FloodyBaseSpec
        {

            public FloodyBaseSpec(FloodyConfig config)
            {
            }

            [MultiNodeFact(Skip = "Only for discovery tests")]
            public void Dummy()
            {
            }
        }

        public class FloodyChildSpec1 : FloodyBaseSpec
        {
            public FloodyChildSpec1(FloodyConfig config) : base(config)
            {
            }
        }

        public class FloodyChildSpec2 : FloodyBaseSpec
        {
            public FloodyChildSpec2(FloodyConfig config) : base(config)
            {
            }
        }

        public class FloodyChildSpec3 : FloodyBaseSpec
        {
            public FloodyChildSpec3(FloodyConfig config) : base(config)
            {
            }
        }

        public class DiverseConfig : MultiNodeConfig
        {
            public RoleName RoleProp { get; set; }
            public readonly RoleName RoleField;
            private RoleName RolePropPriv { get; set; }
            protected readonly RoleName RoleFieldPriv;

            public DiverseConfig()
            {
                RoleProp = Role("RoleProp");
                RoleField = Role("RoleField");
                RolePropPriv = Role("RolePropPriv");
                RoleFieldPriv = Role("RoleFieldPriv");
            }
        }

        public class DiverseSpec
        {
            public DiverseSpec(DiverseConfig config)
            {
            }

            [MultiNodeFact(Skip = "Only for discovery tests")]
            public void Dummy()
            {
            }
        }
    }
}
