//-----------------------------------------------------------------------
// <copyright file="ActorSystemSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor.Setup
{
    public class DummySetup : Akka.Actor.Setup.Setup
    {
        public DummySetup(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public class DummySetup2 : Akka.Actor.Setup.Setup
    {
        public DummySetup2(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public class DummySetup3 : Akka.Actor.Setup.Setup
    {
        public DummySetup3(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public class ActorSystemSetupSpec
    {
        [Fact]
        public void ActorSystemSettingsShouldStoreAndRetrieveSetup()
        {
            var setup = new DummySetup("Ardbeg");
            var setups = ActorSystemSetup.Create(setup);

            setups.Get<DummySetup>().Should().Be(new Option<DummySetup>(setup));
            setups.Get<DummySetup2>().Should().Be(Option<DummySetup2>.None);
        }

        [Fact]
        public void ActorSystemSettingsShouldReplaceSetupIfAlreadyDefined()
        {
            var setup1 = new DummySetup("Ardbeg");
            var setup2 = new DummySetup("Ledaig");
            var setups = ActorSystemSetup.Empty.WithSetup(setup1).WithSetup(setup2);

            setups.Get<DummySetup>().Should().Be(new Option<DummySetup>(setup2));
        }

        [Fact]
        public void ActorSystemSettingsShouldProvideFluentInterface()
        {
            var setup1 = new DummySetup("Ardbeg");
            var setup2 = new DummySetup("Ledaig");
            var setup3 = new DummySetup2("Blantons");
            var setups = setup1.And(setup2).And(setup3);

            setups.Get<DummySetup>().Should().Be(new Option<DummySetup>(setup2));
            setups.Get<DummySetup2>().Should().Be(new Option<DummySetup2>(setup3));
        }

        [Fact]
        public void ActorSystemSettingsShouldBeCreatedWithSetOfSetups()
        {
            var setup1 = new DummySetup("Ardbeg");
            var setup2 = new DummySetup2("Ledaig");
            var setups = ActorSystemSetup.Create(setup1, setup2);

            setups.Get<DummySetup>().HasValue.Should().BeTrue();
            setups.Get<DummySetup2>().HasValue.Should().BeTrue();
            setups.Get<DummySetup3>().HasValue.Should().BeFalse();
        }

        [Fact]
        public void ActorSystemSettingsShouldBeAvailableFromExtendedActorSystem()
        {
            ActorSystem system = null;
            try
            {
                var setup = new DummySetup("Eagle Rare");
                system = ActorSystem.Create("name", ActorSystemSetup.Create(setup));

                system.Settings.Setup.Get<DummySetup>().Should().Be(new Option<DummySetup>(setup));
            }
            finally
            {
                system?.Terminate().Wait(TimeSpan.FromSeconds(5));
            }
        }
    }
}
