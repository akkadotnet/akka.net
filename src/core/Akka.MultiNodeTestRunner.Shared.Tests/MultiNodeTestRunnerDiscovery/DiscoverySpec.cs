//-----------------------------------------------------------------------
// <copyright file="DiscoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.MultiNodeTestRunner.Shared.Tests.MultiNodeTestRunnerDiscovery
{
    public class DiscoverySpec
    {
        [Fact(DisplayName = "Abstract classes are not discoverable")]
        public void No_abstract_classes()
        {
            var discoveredSpecs = DiscoverSpecs();
            Assert.False(discoveredSpecs.ContainsKey(KeyFromSpecName(nameof(DiscoveryCases.NoAbstractClassesSpec))));
        }

        [Fact(DisplayName = "Deeply inherited classes are discoverable")]
        public void Deeply_inherited_are_ok()
        {
            var discoveredSpecs = DiscoverSpecs();
            Assert.Equal("DeeplyInheritedChildRole", discoveredSpecs[KeyFromSpecName(nameof(DiscoveryCases.DeeplyInheritedChildSpec))].First().Role);
        }

        [Fact(DisplayName = "Child test class with default constructors are ok")]
        public void Child_class_with_default_constructor_are_ok()
        {
            Action testDelegate = () =>
            {
                var testCase = typeof(DiscoveryCases.DefaultConstructorOnDerivedClassSpec);
                var constuctor = Discovery.FindConfigConstructor(testCase);
                constuctor.Should().NotBeNull();
            };

            testDelegate.ShouldNotThrow();
        }

        [Fact(DisplayName = "One test case per RoleName per Spec declaration with MultiNodeFact")]
        public void Discovered_count_equals_number_of_roles_mult_specs()
        {
            var discoveredSpecs = DiscoverSpecs();
            Assert.Equal(5, discoveredSpecs[KeyFromSpecName(nameof(DiscoveryCases.FloodyChildSpec1))].Count);
            Assert.Equal(5, discoveredSpecs[KeyFromSpecName(nameof(DiscoveryCases.FloodyChildSpec2))].Count);
            Assert.Equal(5, discoveredSpecs[KeyFromSpecName(nameof(DiscoveryCases.FloodyChildSpec3))].Count);
        }

        [Fact(DisplayName = "Only public props and fields are considered when looking for RoleNames")]
        public void Public_props_and_fields_are_considered()
        {
            var discoveredSpecs = DiscoverSpecs();
            Assert.Equal(discoveredSpecs[KeyFromSpecName(nameof(DiscoveryCases.DiverseSpec))].Select(c => c.Role), new[] {"RoleProp", "RoleField"});
        }

        private static Dictionary<string, List<NodeTest>> DiscoverSpecs()
        {
            using (var controller = new XunitFrontController(AppDomainSupport.IfAvailable, new System.Uri(typeof(DiscoveryCases).GetTypeInfo().Assembly.CodeBase).LocalPath))	
            {
                using (var discovery = new Discovery())
                {
                    controller.Find(false, discovery, TestFrameworkOptions.ForDiscovery());
                    discovery.Finished.WaitOne();
                    return discovery.Tests;
                }
            }
        }

        private string KeyFromSpecName(string specName)
        {
            return typeof(DiscoveryCases).FullName + "+" + specName;
        }
    }
}
