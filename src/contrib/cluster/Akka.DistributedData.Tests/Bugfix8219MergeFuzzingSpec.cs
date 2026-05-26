//-----------------------------------------------------------------------
// <copyright file="Bugfix8219MergeFuzzingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;
using FsCheck.Xunit;

#pragma warning disable xUnit1028
namespace Akka.DistributedData.Tests
{
    /// <summary>
    /// Property-based reproduction test for
    /// https://github.com/akkadotnet/akka.net/issues/8219.
    ///
    /// Subscribers to an <see cref="LWWDictionary{TKey,TValue}"/> are
    /// reported to briefly receive <see cref="Changed"/> events whose entry
    /// count is strictly less than the writer ever wrote, even though the
    /// writer never removes entries and uses <see cref="WriteLocal"/>. The
    /// notification path simply publishes whatever envelope is stored, so a
    /// partial count implies some prior merge stored a state with fewer
    /// entries than the local replica had before.
    ///
    /// This test drives the underlying <see cref="ORDictionary{TKey,TValue}"/>
    /// merge logic through randomized sequences of operations
    /// (<see cref="MergeFuzzingMachine"/>) and asserts the monotonicity
    /// invariant: with no remove operations, a replica's keyset must only
    /// ever grow. FsCheck shrinks any failure to a minimal counterexample
    /// operation list.
    /// </summary>
    public class Bugfix8219MergeFuzzingSpec
    {
        // Skipped pending a structural fix to ORSet/VersionVector pruning.
        // The fuzz machine reliably reproduces the customer's bug, but the
        // fix requires changing how ORSet.Prune rewrites dots so it doesn't
        // pollute VV[collapseInto] via the process-wide AtomicCounter. This
        // is a CRDT-design change with upstream-coordination implications
        // (JVM Akka has the same defect) and is out of scope for the test
        // PR. Tracking: https://github.com/akkadotnet/akka.net/issues/8219.
        [Property(MaxTest = 500, Skip = "Pending structural fix to ORSet.Prune; see #8219")]
        public Property Merge_is_monotonic_for_SetItem_only_workloads()
            => new MergeFuzzingMachine().ToProperty();
    }
}
