//-----------------------------------------------------------------------
// <copyright file="TUnitAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Exceptions;

namespace Akka.TestKit.TUnit;

/// <summary>
/// Provides the synchronous assertion operations required by the Akka.NET TestKit using TUnit assertion exceptions.
/// </summary>
public sealed class TUnitAssertions : ITestKitAssertions
{
    /// <inheritdoc />
    public void Fail(string format = "", params object[] args)
        => throw new AssertionException(BuildAssertionMessage(format, args));

    /// <inheritdoc />
    public void AssertTrue(bool condition, string format = "", params object[] args)
    {
        if (!condition)
            Fail(format, args);
    }

    /// <inheritdoc />
    public void AssertFalse(bool condition, string format = "", params object[] args)
    {
        if (condition)
            Fail(format, args);
    }

    /// <inheritdoc />
    public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
    {
        if (!AreEqual(expected, actual))
            throw new AssertionException(BuildEqualityMessage(expected, actual, format, args));
    }

    /// <inheritdoc />
    public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
    {
        if (!comparer(expected, actual))
            throw new AssertionException(BuildEqualityMessage(expected, actual, format, args));
    }

    /// <inheritdoc />
    public Exception AssertThrows(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new AssertionException("Expected an exception, but no exception was thrown.");
    }

    /// <inheritdoc />
    public TException AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw WrongExceptionType<TException>(exception);
        }

        throw NoExceptionThrown<TException>();
    }

    /// <inheritdoc />
    public async Task<Exception> AssertThrowsAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new AssertionException("Expected an exception, but no exception was thrown.");
    }

    /// <inheritdoc />
    public async Task<TException> AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw WrongExceptionType<TException>(exception);
        }

        throw NoExceptionThrown<TException>();
    }

    private static bool AreEqual<T>(T expected, T actual)
    {
        if (ReferenceEquals(expected, actual))
            return true;
        if (expected is null || actual is null)
            return false;

        switch (expected)
        {
            case IEquatable<T> equatable:
                return equatable.Equals(actual);
            case IComparable<T> comparable:
                return comparable.CompareTo(actual) == 0;
            case IComparable comparable:
                return comparable.CompareTo(actual) == 0;
            case IEnumerable expectedEnumerable when actual is IEnumerable actualEnumerable:
                return expectedEnumerable.Cast<object?>()
                    .SequenceEqual(actualEnumerable.Cast<object?>(), ObjectEqualityComparer.Instance);
        }

        if (expected.GetType() != actual.GetType())
            return false;

        return object.Equals(expected, actual);
    }

    private static AssertionException NoExceptionThrown<TException>() where TException : Exception
        => new($"Expected an exception of type {typeof(TException).FullName}, but no exception was thrown.");

    private static AssertionException WrongExceptionType<TException>(Exception actual) where TException : Exception
        => new(
            $"Expected an exception of type {typeof(TException).FullName}, but {actual.GetType().FullName} was thrown.",
            actual);

    private static string BuildEqualityMessage<T>(T expected, T actual, string format, object[] args)
    {
        var userMessage = BuildAssertionMessage(format, args);
        return $"Expected: {FormatValue(expected)}{Environment.NewLine}Actual:   {FormatValue(actual)}"
            + (string.IsNullOrEmpty(userMessage) ? string.Empty : $"{Environment.NewLine}{userMessage}");
    }

    internal static string BuildAssertionMessage(string format, object[] args)
    {
        if (string.IsNullOrEmpty(format) || args.Length == 0)
            return format;

        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return $"[Could not string.Format(\"{format}\", {string.Join(", ", args)})]";
        }
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            IEnumerable items => $"[{string.Join(", ", items.Cast<object?>().Select(FormatValue))}]",
            _ => value.ToString() ?? value.GetType().FullName ?? "<unknown>"
        };

    private sealed class ObjectEqualityComparer : IEqualityComparer<object?>
    {
        public static readonly ObjectEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y)
            => AreEqual(x, y);

        public int GetHashCode(object? obj)
            => obj?.GetHashCode() ?? 0;
    }
}
