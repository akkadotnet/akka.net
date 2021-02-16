//-----------------------------------------------------------------------
// <copyright file="ServiceProviderSetupSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DependencyInjection.Tests
{
    public class ServiceProviderSetupSpecs : AkkaSpec, IClassFixture<AkkaDiFixture>
    {
        public ServiceProviderSetupSpecs(AkkaDiFixture fixture, ITestOutputHelper output) : base(ServiceProviderSetup.Create(fixture.Provider)
            .And(BootstrapSetup.Create().WithConfig(TestKitBase.DefaultConfig)), output)
        {

        }

        [Fact(DisplayName = "DI: Should access Microsoft.Extensions.DependencyInjection.IServiceProvider from ServiceProvider ActorSystem extension")]
        public void ShouldAccessServiceProviderFromActorSystemExtension()
        {
            var sp = ServiceProvider.For(Sys);
            var dep = sp.Provider.GetService<AkkaDiFixture.ITransientDependency>();
            dep.Should().BeOfType<AkkaDiFixture.Transient>();

            var dep2 = sp.Provider.GetService<AkkaDiFixture.ITransientDependency>();
            dep2.Should().NotBe(dep); // the two transient instances should be different

            // scoped services should be the same
            var scoped1 = sp.Provider.GetService<AkkaDiFixture.IScopedDependency>();
            var scoped2 = sp.Provider.GetService<AkkaDiFixture.IScopedDependency>();

            scoped1.Should().Be(scoped2);

            // create a new scope
            using (var newScope = sp.Provider.CreateScope())
            {
                var scoped3 = newScope.ServiceProvider.GetService<AkkaDiFixture.IScopedDependency>();
                scoped1.Should().NotBe(scoped3);
            }

            // singleton services should be the same
            var singleton1 = sp.Provider.GetService<AkkaDiFixture.ISingletonDependency>();
            var singleton2 = sp.Provider.GetService<AkkaDiFixture.ISingletonDependency>();

            singleton1.Should().Be(singleton2);
        }
    }

    public class ServiceProviderFailedSetupSpecs : AkkaSpec
    {
        public ServiceProviderFailedSetupSpecs(ITestOutputHelper output) : base(output)
        {

        }

        [Fact(DisplayName = "DI: Should fail if ServiceProviderSetup was not provided")]
        public void ShouldAccessServiceProviderFromActorSystemExtension()
        {
            Action getSp = () =>
            {
                var sp = ServiceProvider.For(Sys);
            };

            getSp.Should().Throw<ConfigurationException>();
        }
    }
}
