//-----------------------------------------------------------------------
// <copyright file="RepeatAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace Akka.Tests.Shared.Internals;

/// <summary>
/// This is an internal utility to test flaky/racy unit tests. 
/// It allows the test runner to run a single unit test repeatedly to test for flaky situations.
/// 
/// NOTE:
/// Make sure that this attribute are _NOT_ used in the unit test when it is ready to be committed, 
/// because it creates artificial load that can bind the CI/CD PR validation process.
/// </summary>
/// <example>
/// // This will repeatedly run MyUnitTest 500 times
/// // Note that you NEED to use [Theory], and the unit test requires a single integer parameter.
/// [Theory]
/// [Repeat(500)]
/// public void MyUnitTest(int _)
/// { }
/// </example>
public sealed class RepeatAttribute : DataAttribute
{
    private readonly int _count;

    public RepeatAttribute(int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count),
                "Repeat count must be greater than 0.");
        }
        _count = count;
    }

    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        var rows = new List<ITheoryDataRow>(_count);

        for (var i = 1; i <= _count; i++)
            rows.Add(new RepeatTheoryDataRow(i));

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);    }

    public override bool SupportsDiscoveryEnumeration() => true;
}

public class RepeatTheoryDataRow(int count) : TheoryDataRowBase
{
    /// <summary>
    /// Gets the row of data.
    /// </summary>
    public object?[] Data => [count];

    /// <inheritdoc/>
    protected override object?[] GetData() => [count];
}
