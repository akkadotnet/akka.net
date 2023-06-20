//-----------------------------------------------------------------------
// <copyright file="FlowGroupedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowGroupedSpec : ScriptedTest
    {
        
        private ActorMaterializerSettings Settings { get; }

        public FlowGroupedSpec(ITestOutputHelper output = null) : base(output)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
        }

        private readonly Random _random = new Random(12345);
        private ICollection<int> RandomSeq(int n) => Enumerable.Range(1, n).Select(_ => _random.Next()).ToList();

        private (ICollection<int>, ICollection<IEnumerable<int>>) RandomTest(int n)
        {
            var s = RandomSeq(n);
            return (s, new[] {s});
        }

        // No need to use AssertAllStagesStoppedAsync, it is encapsulated in RunScriptAsync
        [Fact]
        public async Task A_Grouped_must_group_evenly()
        {
            var testLength = _random.Next(1, 16);
            var script = Script.Create(RandomTestRange(Sys).Select(_ => RandomTest(testLength)).ToArray());
            foreach (var _ in RandomTestRange(Sys))
            {
                await RunScriptAsync(script, Settings, flow => flow.Grouped(testLength));
            }
        }

        // No need to use AssertAllStagesStoppedAsync, it is encapsulated in RunScriptAsync
        [Fact]
        public async Task A_Grouped_must_group_with_rest()
        {
            var testLength = _random.Next(1, 16);
            var script = Script.Create(RandomTestRange(Sys).Select(_ => RandomTest(testLength)).Concat(RandomTest(1)).ToArray());
            foreach (var _ in RandomTestRange(Sys))
            {
                await RunScriptAsync(script, Settings, flow => flow.Grouped(testLength));
            }
        }
    }
}
