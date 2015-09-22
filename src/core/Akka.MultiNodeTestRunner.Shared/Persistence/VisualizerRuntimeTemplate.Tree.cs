// -----------------------------------------------------------------------
//  <copyright file="VisualizerRuntimeTemplate.Tree.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    partial class VisualizerRuntimeTemplate
    {
        private TestRunTree _tree;
        public string Prefix { get; private set; }

        public TestRunTree Tree
        {
            get { return _tree; }
            set
            {
                _tree = value;
                Prefix = LongestCommonPrefix(value.Specs
                    .Select(s => s.FactName)
                    .ToArray());
            }
        }

        public string BuildSpecificationId(FactData spec)
        {
            return spec.FactName.Replace(".", "_");
        }

        public string BuildTimelineItem(FactData spec)
        {
            var messages =
                spec.RunnerMessages
                    .Select(m =>
                        TimelineItem.CreateSpecMessage(m.Message, m.NodeIndex, m.TimeStamp));

            var facts =
                spec.NodeFacts
                    .SelectMany(m =>
                        m.Value.EventStream
                            .Select(e =>
                                TimelineItem.CreateNodeFact(e.Message, e.NodeIndex, e.TimeStamp)));


            var itemStrings = messages.Concat(facts)
                .Select(i => i.ToJavascriptString());

            return string.Join(",\r\n", itemStrings);
        }

        public string BuildGroupItems(FactData spec)
        {
            var groups = spec.NodeFacts
                .Select(nf =>
                    string.Format("{{ id:{0}, content:'Node {0}' }}", nf.Value.NodeIndex))
                .Concat(@"{ id:-1, content:'Misc' }");

            return string.Join(",\r\n", groups);
        }

        private static string LongestCommonPrefix(IReadOnlyList<string> strings)
        {
            if (strings == null || strings.Count == 0)
            {
                return string.Empty;
            }

            var commonPrefix = strings[0];

            for (var i = 1; i < strings.Count; i++)
            {
                var j = 0;
                for (; j < commonPrefix.Length && j < strings[i].Length; j++)
                {
                    if (commonPrefix[j] != strings[i][j])
                    {
                        commonPrefix = commonPrefix.Substring(0, j);
                        break;
                    }
                }

                if (j == strings[i].Length)
                {
                    commonPrefix = strings[i];
                }
            }

            return commonPrefix;
        }
    }
}