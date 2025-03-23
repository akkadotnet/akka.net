//-----------------------------------------------------------------------
// <copyright file="XunitAssertionsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Xunit;
using Xunit.Sdk;

namespace Akka.TestKit.Xunit2.Tests;

public class XunitAssertionsSpec
{
    private readonly XunitAssertions _assertions = new();
    
    [Fact]
    public void Assert_does_not_format_message_when_no_arguments_are_specified()
    {
        const string testMessage = "{Value} with different format placeholders {0}";

        var exception = Assert.ThrowsAny<XunitException>(() => _assertions.Fail(testMessage));
        Assert.Contains(testMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertTrue(false, testMessage));
        Assert.Contains(testMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertFalse(true, testMessage));
        Assert.Contains(testMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertEqual(4, 2, testMessage));
        Assert.Contains(testMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertEqual(4, 2, (_, _) => false, testMessage));
        Assert.Contains(testMessage, exception.Message);
    }
    
    [Fact]
    public void Assert_formats_message_when_arguments_are_specified()
    {
        const string testMessage = "Meaning: {0}";
        const string expectedMessage = "Meaning: 42";

        var exception = Assert.ThrowsAny<XunitException>(() => _assertions.Fail(testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertTrue(false, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertFalse(true, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertEqual(4, 2, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertEqual(4, 2, (_, _) => false, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);
    }
    
    [Fact]
    public void Assert_catches_format_exceptions()
    {
        const string testMessage = "Meaning: {0} {1}";
        const string expectedMessage = "Could not string.Format";

        var exception = Assert.ThrowsAny<XunitException>(() => _assertions.Fail(testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertTrue(false, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertFalse(true, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertEqual(4, 2, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);

        exception = Assert.ThrowsAny<XunitException>(() => _assertions.AssertEqual(4, 2, (_, _) => false, testMessage, 42));
        Assert.Contains(expectedMessage, exception.Message);
    }
}
