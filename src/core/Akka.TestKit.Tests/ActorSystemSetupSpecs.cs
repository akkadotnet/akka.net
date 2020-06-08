//-----------------------------------------------------------------------
// <copyright file="ActorSystemSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Setup;
using Xunit;

namespace Akka.TestKit.Tests
{
    /// <summary>
    /// Validate that an <see cref="ActorSystem"/> inside the testkit
    /// can be configured using an <see cref="ActorSystemSetup"/> instance.
    /// </summary>
    public class ActorSystemSetupSpecs : AkkaSpec
    {
        public static readonly ActorSystemSetup Setup = ActorSystemSetup.Create().WithSetup(BootstrapSetup.Create().WithConfig(@"akka.hi = true"));

        public ActorSystemSetupSpecs() : base(Setup) { }

        [Fact]
        public void ShouldReadConfigFromActorSystemSetup()
        {
            Assert.True(Sys.Settings.Config.HasPath("akka.hi"));
        }
    }
}
