//-----------------------------------------------------------------------
// <copyright file="SpecLog.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    /// <summary>
    /// SpecLog
    /// </summary>
    public class SpecLog
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
