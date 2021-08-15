//-----------------------------------------------------------------------
// <copyright file="ActorSystemSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public sealed class DummyExtension1 : IExtension
    {
        public DummyExtension1(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public sealed class DummyExtension2 : IExtension
    {
        public DummyExtension2(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public sealed class DummyExtensionIdProvider1 : ExtensionIdProvider<DummyExtension1>
    {
        readonly string _name;

        public DummyExtensionIdProvider1(string name)
        {
            _name = name;
        }

        public override DummyExtension1 CreateExtension(ExtendedActorSystem system)
        {
            return new DummyExtension1(_name);
        }
    }

    public sealed class DummyExtensionIdProvider2 : ExtensionIdProvider<DummyExtension2>
    {
        readonly string _name;

        public DummyExtensionIdProvider2(string name)
        {
            _name = name;
        }

        public override DummyExtension2 CreateExtension(ExtendedActorSystem system)
        {
            return new DummyExtension2(_name);
        }
    }

    public class BootstrapSpec
    {
        [Fact]
        public void ActorSystemShouldBeCreatedWithBootstrapSetupExtensions()
        {
            ActorSystem system = null;
            try
            {
                var ep1 = new DummyExtensionIdProvider1("heri");
                var ep2 = new DummyExtensionIdProvider2("dewf");
                var setup = BootstrapSetup.Create().WithExtensions(ep1, ep2);
                system = ActorSystem.Create("name", ActorSystemSetup.Create(setup));

                var exten1 = system.GetExtension<DummyExtension1>();
                var exten2 = system.GetExtension<DummyExtension2>();

                exten1.Should().NotBeNull();
                exten1.Name.ShouldBe("heri");

                exten2.Should().NotBeNull();
                exten2.Name.ShouldBe("dewf");
            }
            finally
            {
                system?.Terminate().Wait(TimeSpan.FromSeconds(5));
            }
        }

    }
}
