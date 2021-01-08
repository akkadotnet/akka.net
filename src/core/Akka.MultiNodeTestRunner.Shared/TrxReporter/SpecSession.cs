//-----------------------------------------------------------------------
// <copyright file="SpecSession.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared.AzureDevOps
{
    public class SpecSession
    {
        private readonly List<SpecEvent<NodeCompletedSpecWithSuccess>> _successes = new List<SpecEvent<NodeCompletedSpecWithSuccess>>();
        private readonly List<SpecEvent<NodeCompletedSpecWithFail>> _fails = new List<SpecEvent<NodeCompletedSpecWithFail>>();
        private readonly List<SpecEvent<LogMessageFragmentForNode>> _messages = new List<SpecEvent<LogMessageFragmentForNode>>();

        public SpecEvent<BeginNewSpec> Begin { get; private set; }
        public IReadOnlyList<SpecEvent<NodeCompletedSpecWithSuccess>> Successes => _successes;
        public IReadOnlyList<SpecEvent<NodeCompletedSpecWithFail>> Fails => _fails;
        public IReadOnlyList<SpecEvent<LogMessageFragmentForNode>> Messages => _messages;
        public SpecEvent<EndSpec> End { get; private set; }

        public void OnBegin(BeginNewSpec value) => Begin = new SpecEvent<BeginNewSpec>(DateTime.UtcNow, value);
        public void OnSuccess(NodeCompletedSpecWithSuccess value) => _successes.Add(new SpecEvent<NodeCompletedSpecWithSuccess>(DateTime.UtcNow, value));
        public void OnFailure(NodeCompletedSpecWithFail value) => _fails.Add(new SpecEvent<NodeCompletedSpecWithFail>(DateTime.UtcNow, value));
        public void OnMessage(LogMessageFragmentForNode value) => _messages.Add(new SpecEvent<LogMessageFragmentForNode>(DateTime.UtcNow, value));
        public void OnEnd(EndSpec value) => End = new SpecEvent<EndSpec>(DateTime.UtcNow, value);
    }
}
