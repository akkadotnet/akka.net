//-----------------------------------------------------------------------
// <copyright file="ActorSystemSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor.Setup;
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
    }
}
