//-----------------------------------------------------------------------
// <copyright file="NodeDataActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    public class NodeDataActorSpec : AkkaSpec
    {
        [Fact]
        public void NodeData_should_maintain_events_in_time_order() 
        {
            var nodeIndex = 1;
            var nodeRole = NodeMessageHelpers.DummyRoleFor + nodeIndex;
            var nodeDataActor = Sys.ActorOf(Props.Create(() => new NodeDataActor(nodeIndex, nodeRole)));
            var m1 = NodeMessageHelpers.GenerateMessageSequence(nodeIndex, 3);

            foreach(var m in m1)
                nodeDataActor.Tell(m);

            var m2 = NodeMessageHelpers.GenerateMessageSequence(nodeIndex, 4);
            foreach(var m in m2)
                nodeDataActor.Tell(m);

            //union the two sets together
            m1.UnionWith(m2);

            //Kill the node data actor and have it deliver its payload to TestActor
            nodeDataActor.Tell(new EndSpec(), TestActor);

            var nodeData = ExpectMsg<NodeData>();

            Assert.True(m1.SetEquals(nodeData.EventStream));
            Assert.Equal(nodeIndex, nodeData.NodeIndex);
            Assert.Equal(nodeRole, nodeData.NodeRole);
            Assert.False(nodeData.EndTime.HasValue);
            Assert.False(nodeData.Passed.HasValue);
        }

        [Fact]
        public void NodeData_should_mark_as_complete_when_MultiNodeResultMessage_received()
        {
            var nodeIndex = 1;
            var nodeRole = NodeMessageHelpers.DummyRoleFor + nodeIndex;
            var nodeDataActor = Sys.ActorOf(Props.Create(() => new NodeDataActor(nodeIndex, nodeRole)));

            var m1 = NodeMessageHelpers.GenerateMessageSequence(nodeIndex, 3);
            m1.UnionWith(NodeMessageHelpers.GenerateResultMessage(nodeIndex, true));

            foreach (var m in m1)
                nodeDataActor.Tell(m);

            //Kill the node data actor and have it deliver its payload to TestActor
            nodeDataActor.Tell(new EndSpec(), TestActor);

            var nodeData = ExpectMsg<NodeData>();

            Assert.True(m1.SetEquals(nodeData.EventStream));
            Assert.True(nodeData.Passed.Value);
            Assert.True(nodeData.EndTime.HasValue);
            Assert.True(nodeData.EndTime.Value >= nodeData.StartTime);
        }

        [Fact]
        public void NodeData_should_mark_as_failed_when_MultiNodeResultMessage_received()
        {
            var nodeIndex = 1;
            var nodeRole = NodeMessageHelpers.DummyRoleFor + nodeIndex;
            var nodeDataActor = Sys.ActorOf(Props.Create(() => new NodeDataActor(nodeIndex, nodeRole)));

            var m1 = NodeMessageHelpers.GenerateMessageSequence(nodeIndex, 3);
            m1.UnionWith(NodeMessageHelpers.GenerateResultMessage(nodeIndex, false));

            foreach (var m in m1)
                nodeDataActor.Tell(m);

            //Kill the node data actor and have it deliver its payload to TestActor
            nodeDataActor.Tell(new EndSpec(), TestActor);

            var nodeData = ExpectMsg<NodeData>();

            Assert.True(m1.SetEquals(nodeData.EventStream));
            Assert.False(nodeData.Passed.Value);
            Assert.True(nodeData.EndTime.HasValue);
            Assert.True(nodeData.EndTime.Value >= nodeData.StartTime);
        }

        [Fact]
        public void NodeData_should_process_LogMessageFragments_into_timeline()
        {
            var nodeIndex = 1;
            var nodeRole = NodeMessageHelpers.DummyRoleFor + nodeIndex;
            var nodeDataActor = Sys.ActorOf(Props.Create(() => new NodeDataActor(nodeIndex, nodeRole)));
            var m1 = NodeMessageHelpers.GenerateMessageSequence(nodeIndex, 3);

            foreach (var m in m1)
                nodeDataActor.Tell(m);

            var m2 = NodeMessageHelpers.GenerateMessageFragmentSequence(nodeIndex, 4);
            foreach (var m in m2)
                nodeDataActor.Tell(m);

            //union the two sets together
            m1.UnionWith(m2);

            //Kill the node data actor and have it deliver its payload to TestActor
            nodeDataActor.Tell(new EndSpec(), TestActor);

            var nodeData = ExpectMsg<NodeData>();

            Assert.True(m1.SetEquals(nodeData.EventStream));
            Assert.Equal(nodeIndex, nodeData.NodeIndex);
            Assert.Equal(nodeRole, nodeData.NodeRole);
            Assert.False(nodeData.EndTime.HasValue);
            Assert.False(nodeData.Passed.HasValue);
        }
    }
}

