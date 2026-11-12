// -----------------------------------------------------------------------
// <copyright file="TestKitBase_ActorSystemFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using Akka.Actor;
using Akka.Actor.Setup;

namespace Akka.TestKit;

public abstract partial class TestKitBase
{
    /// <summary>Creates the test actor system after default configuration and logger setup are applied.</summary>
    /// <param name="name">The actor system name.</param>
    /// <param name="setup">The complete actor system setup.</param>
    /// <returns>The started actor system.</returns>
    /// <remarks>Called during base construction; overrides must not depend on initialized derived fields.</remarks>
    protected virtual ActorSystem CreateActorSystem(string name, ActorSystemSetup setup) => ActorSystem.Create(name, setup);
}
