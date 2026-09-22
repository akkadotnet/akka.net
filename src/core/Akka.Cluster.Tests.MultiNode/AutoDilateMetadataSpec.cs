//-----------------------------------------------------------------------
// <copyright file="AutoDilateMetadataSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using Akka.Cluster.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode;

public class AutoDilateMetadataSpec
{
    [Fact]
    public void MultiNodeClusterSpec_should_mark_automatically_dilated_parameters()
    {
        AssertAutoDilated(nameof(MultiNodeClusterSpec.JoinWithin), "max",
            "joinNode", "max", "interval");
        AssertAutoDilated(nameof(MultiNodeClusterSpec.AwaitMembersUp), "timeout",
            "numbersOfMembers", "canNotBePartOfMemberRing", "timeout");
        AssertAutoDilated(nameof(MultiNodeClusterSpec.AwaitMembersUpAsync), "timeout",
            "numbersOfMembers", "canNotBePartOfMemberRing", "timeout", "cancellationToken");
    }

    [Fact]
    public void MultiNodeClusterSpec_should_not_mark_raw_interval_parameters()
    {
        var parameter = FindParameter(nameof(MultiNodeClusterSpec.JoinWithin), "interval",
            "joinNode", "max", "interval");

        Assert.False(parameter.IsDefined(typeof(AutoDilateAttribute), inherit: false));
    }

    private static void AssertAutoDilated(
        string methodName,
        string parameterName,
        params string[] parameterNames)
    {
        var parameter = FindParameter(methodName, parameterName, parameterNames);
        Assert.True(parameter.IsDefined(typeof(AutoDilateAttribute), inherit: false));
    }

    private static ParameterInfo FindParameter(
        string methodName,
        string parameterName,
        params string[] parameterNames)
    {
        var method = typeof(MultiNodeClusterSpec)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Select(parameter => parameter.Name).SequenceEqual(parameterNames));

        return method.GetParameters().Single(parameter => parameter.Name == parameterName);
    }
}
