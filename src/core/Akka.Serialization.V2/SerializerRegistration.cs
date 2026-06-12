//-----------------------------------------------------------------------
// <copyright file="SerializerRegistration.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Serialization.V2;

/// <summary>
/// A generated, AOT-safe serializer registration that can be composed explicitly by users.
/// </summary>
public sealed class SerializerRegistration
{
    private readonly Func<ExtendedActorSystem, global::Akka.Serialization.SerializerV2> _createSerializer;

    private SerializerRegistration(
        string alias,
        Func<ExtendedActorSystem, global::Akka.Serialization.SerializerV2> createSerializer,
        ImmutableHashSet<Type> useFor)
    {
        Alias = alias;
        _createSerializer = createSerializer;
        UseFor = useFor;
    }

    public string Alias { get; }

    public ImmutableHashSet<Type> UseFor { get; }

    public static SerializerRegistration Create(
        string alias,
        Func<ExtendedActorSystem, global::Akka.Serialization.SerializerV2> createSerializer,
        ImmutableHashSet<Type> useFor)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Serializer alias must be non-empty.", nameof(alias));

        return new SerializerRegistration(alias, createSerializer ?? throw new ArgumentNullException(nameof(createSerializer)), useFor);
    }

    public global::Akka.Serialization.SerializerDetails CreateDetails(ExtendedActorSystem system)
    {
        return global::Akka.Serialization.SerializerDetails.Create(Alias, _createSerializer(system), UseFor);
    }

    public global::Akka.Serialization.SerializationSetup CreateSetup()
    {
        return global::Akka.Serialization.SerializationSetup.Create(system => ImmutableHashSet.Create(CreateDetails(system)));
    }

    public static global::Akka.Serialization.SerializationSetup CreateSetup(params SerializerRegistration[] registrations)
    {
        return global::Akka.Serialization.SerializationSetup.Create(system =>
        {
            var builder = ImmutableHashSet.CreateBuilder<global::Akka.Serialization.SerializerDetails>();
            foreach (var registration in registrations)
            {
                builder.Add(registration.CreateDetails(system));
            }

            return builder.ToImmutable();
        });
    }
}
