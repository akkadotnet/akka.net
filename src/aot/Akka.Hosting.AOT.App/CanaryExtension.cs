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
/// <c>AkkaConfigurationBuilder.WithExtension&lt;CanaryExtensionProvider&gt;()</c>. Since #8649, that
/// builds an <see cref="Akka.Actor.Setup.ExtensionsSetup"/> from the already-constructed provider
/// instance and hands it to <c>ActorSystem.Create</c> - no type name, no <c>akka.extensions</c>
/// HOCON entry, no <c>Type.GetType</c>. <c>ActorSystemImpl.LoadExtensions()</c> reads that setup
/// first, before falling back to the string-based <c>akka.extensions</c> list.
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
/// Public parameterless constructor is still load-bearing, for a different reason than before #8649:
/// <c>WithExtension&lt;T&gt;()</c>'s generic parameter carries
/// <c>[DynamicallyAccessedMembers(PublicParameterlessConstructor)]</c>, and it is that annotation -
/// not a reflection lookup - that keeps this constructor alive for the trimmer.
/// </summary>
internal sealed class CanaryExtensionProvider : ExtensionIdProvider<CanaryExtension>
{
    public override CanaryExtension CreateExtension(ExtendedActorSystem system) => new(system);
}
