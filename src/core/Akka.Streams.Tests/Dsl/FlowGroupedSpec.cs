using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using Xunit;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowGroupedSpec : ScriptedTest
    {
        private ActorMaterializerSettings Settings { get; }

        public FlowGroupedSpec()
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
        }

        private static Random Random = new Random();
        private static ICollection<int> RandomSeq(int n) => Enumerable.Range(1, n).Select(_ => Random.Next()).ToList();

        private static Tuple<ICollection<int>, ICollection<IEnumerable<int>>> RandomTest(int n)
        {
            var s = RandomSeq(n);
            return Tuple.Create<ICollection<int>, ICollection<IEnumerable<int>>>(s, new[] {s});
        }

        [Fact]
        public void A_Grouped_must_group_evenly()
        {
            var testLength = Random.Next(1, 16);
            Func<Script<int, IEnumerable<int>>> script =
                () => Script.Create(RandomTestRange(Sys).Select(_ => RandomTest(testLength)).ToArray());
            RandomTestRange(Sys).ForEach(_=>RunScript(script(),Settings, flow => flow.Grouped(testLength)));
        }

        [Fact]
        public void A_Grouped_must_group_with_rest()
        {
            var testLength = Random.Next(1, 16);
            Func<Script<int, IEnumerable<int>>> script =
                () => Script.Create(RandomTestRange(Sys).Select(_ => RandomTest(testLength)).Concat(RandomTest(1)).ToArray());
            RandomTestRange(Sys).ForEach(_ => RunScript(script(), Settings, flow => flow.Grouped(testLength)));
        }
    }
}
