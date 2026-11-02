// //-----------------------------------------------------------------------
// // <copyright file="TestSpecLog.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.MultiNode.TestAdapter.Internal.Sinks
{
    /// <summary>
    /// SpecLog
    /// </summary>
    internal class SpecLog
    {
        /// <summary>
        /// Aggregated timeline logs for all notes in spec
        /// </summary>
        public List<string> AggregatedTimelineLog { get; set; }
        /// <summary>
        /// Timelines per each node
        /// </summary>
        public List<(int NodeIndex, string NodeRole, List<string> Log)> NodeLogs { get; set; }
        
        public static SpecLog Empty => new SpecLog()
        {
            AggregatedTimelineLog = new List<string>(),
            NodeLogs = new List<(int NodeIndex, string NodeRole, List<string> Log)>()
        };
    }
}