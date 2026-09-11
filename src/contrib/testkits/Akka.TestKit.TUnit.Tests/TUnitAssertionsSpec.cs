//-----------------------------------------------------------------------
// <copyright file="TUnitAssertionsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace Akka.TestKit.TUnit.Tests;

public sealed class TUnitAssertionsSpec
{
    private readonly TUnitAssertions _assertions = new();

    [Test]
    public async Task Should_preserve_unformatted_messages_without_arguments()
    {
        const string message = "{Value} with a non-numeric placeholder {0}";

        var exception = await Assert.ThrowsAsync<AssertionException>(
            () => Task.Run(() => _assertions.AssertTrue(false, message)));

        await Assert.That(exception!.Message).IsEqualTo(message);
    }

    [Test]
    public async Task Should_format_messages_with_arguments()
    {
        var exception = await Assert.ThrowsAsync<AssertionException>(
            () => Task.Run(() => _assertions.AssertFalse(true, "Meaning: {0}", 42)));

        await Assert.That(exception!.Message).IsEqualTo("Meaning: 42");
    }

    [Test]
    public async Task Should_report_malformed_format_strings()
    {
        var exception = await Assert.ThrowsAsync<AssertionException>(
            () => Task.Run(() => _assertions.AssertTrue(false, "Missing: {0} {1}", 42)));

        await Assert.That(exception!.Message).StartsWith("[Could not string.Format");
    }

    [Test]
    public async Task Should_compare_nested_sequences_by_value()
    {
        _assertions.AssertEqual(
            new[] { new[] { 1, 2 }, new[] { 3, 4 } },
            new[] { new[] { 1, 2 }, new[] { 3, 4 } });

        var exception = await Assert.ThrowsAsync<AssertionException>(
            () => Task.Run(
                () => _assertions.AssertEqual(
                    new[] { new[] { 1, 2 }, new[] { 3, 4 } },
                    new[] { new[] { 1, 2 }, new[] { 3, 5 } })));

        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task Should_report_the_actual_exception_type()
    {
        var exception = await Assert.ThrowsAsync<AssertionException>(
            () => Task.Run(
                () => _assertions.AssertThrows<InvalidOperationException>(
                    () => throw new ArgumentException("wrong type"))));

        await Assert.That(exception!.Message).Contains(typeof(InvalidOperationException).FullName!);
        await Assert.That(exception.Message).Contains(typeof(ArgumentException).FullName!);
    }

    [Test]
    public void Should_use_custom_comparers()
        => _assertions.AssertEqual("AKKA", "akka", StringComparer.OrdinalIgnoreCase.Equals);

    [Test]
    public void Should_honor_comparable_equality()
        => _assertions.AssertEqual(new ComparableOnly(42), new ComparableOnly(42));

    private sealed class ComparableOnly(int value) : IComparable<ComparableOnly>
    {
        private int Value => value;

        public int CompareTo(ComparableOnly? other)
            => other is null ? 1 : value.CompareTo(other.Value);
    }
}
