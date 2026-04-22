//-----------------------------------------------------------------------
// <copyright file="AkkaCleanAmbientContextAttributeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.TestKit.Xunit.Attributes;
using Xunit;

namespace Akka.TestKit.Xunit.Tests;

public class AkkaCleanAmbientContextAttributeSpec
{
    // Concrete subclass used purely as a reflection target. It doesn't run
    // any [Fact] of its own — the point is to verify that the parallel-safe
    // attribute propagates onto user test classes via attribute inheritance.
    private class DerivedTestKit : TestKit { }

    [Fact]
    public void Attribute_is_declared_on_base_TestKit_class()
    {
        var attribute = typeof(TestKit)
            .GetCustomAttributes(typeof(AkkaCleanAmbientContextAttribute), inherit: false)
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public void Attribute_is_inherited_by_derived_test_classes()
    {
        var attribute = typeof(DerivedTestKit)
            .GetCustomAttributes(typeof(AkkaCleanAmbientContextAttribute), inherit: true)
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public void Attribute_is_marked_as_inherited()
    {
        // If Inherited=false, user test classes deriving from TestKit would
        // have to redeclare the attribute to get parallel-safe behavior. Guard
        // against a future edit flipping this flag.
        var usage = typeof(AkkaCleanAmbientContextAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.Inherited);
    }
}
