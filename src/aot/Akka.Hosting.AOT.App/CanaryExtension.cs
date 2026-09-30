//-----------------------------------------------------------------------
// <copyright file="CanaryExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Hosting.AOT.App;

/// <summary>
/// A trivial custom Akka.NET extension, registered through
/// <c>AkkaConfigurationBuilder.WithExtension&lt;CanaryExtensionProvider&gt;()</c>. Exercises the
/// same load path as a real cluster extension (DistributedPubSub, ClusterBootstrap, ...): the
/// provider's assembly-qualified name round-trips through the <c>akka.extensions</c> HOCON list
/// and is resolved back with <c>Type.GetType</c> + <c>Activator.CreateInstance</c> in
/// <c>ActorSystemImpl.LoadExtensions()</c>.
/// </summary>
internal sealed class CanaryExtension : IExtension
{
    public CanaryExtension(ExtendedActorSystem system)
    {
        System = system;
    }

    public ExtendedActorSystem System { get; }

    /// <summary>Flipped once, so the canary can prove this specific instance is the one in use.</summary>
    public bool Marked { get; set; }
}

/// <summary>
/// Public parameterless constructor is load-bearing: <c>ActorSystemImpl.LoadExtensions()</c> calls
/// <c>Activator.CreateInstance(extensionType)</c> on the type it resolves from the HOCON string, with
/// no <c>DynamicallyAccessedMembers</c> annotation naming what it needs.
/// </summary>
internal sealed class CanaryExtensionProvider : ExtensionIdProvider<CanaryExtension>
{
    public override CanaryExtension CreateExtension(ExtendedActorSystem system) => new(system);
}
