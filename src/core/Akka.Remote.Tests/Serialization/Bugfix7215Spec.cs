//-----------------------------------------------------------------------
// <copyright file="Bugfix7215Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;
using Akka.Remote.Serialization;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using static FluentAssertions.FluentActions;

namespace Akka.Remote.Tests.Serialization;

public class Bugfix7215Spec: TestKit.Xunit2.TestKit
{
    public Bugfix7215Spec(ITestOutputHelper output) : base(nameof(Bugfix7215Spec), output)
    {
    }

    [Theory(DisplayName = "Should be able to deserialize InvalidOperationException from all frameworks")]
    [InlineData("Net471")]
    [InlineData("Net481")]
    [InlineData("Net6.0")]
    [InlineData("Net8.0")]
    public void DeserializeNet471Test(string framework)
    {
        var bytes = File.ReadAllBytes($"./test-files/SerializedException-{framework}.bin");
        var helper = new ExceptionSupport((ExtendedActorSystem)Sys);
        
        Exception exception = null;
        Invoking(() => exception = helper.DeserializeException(bytes)).Should().NotThrow();
        exception.Should().BeOfType<InvalidOperationException>();
        exception.Message.Should().Be("You can't do this.");
    }
}
