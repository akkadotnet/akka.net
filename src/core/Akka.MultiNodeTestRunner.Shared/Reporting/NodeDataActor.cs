using System;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Persistence;
using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    /// <summary>
    /// Actor responsible for processing test messages for an individual node within a multi-node test
    /// </summary>
    public class NodeDataActor : ReceiveActor
    {
        /// <summary>
        /// Data that will be processed and aggregated for an individual node
        /// </summary>
        protected NodeData NodeData;

        /// <summary>
        /// The ID of this node in the 0-N index of all nodes for this test.
        /// </summary>
        protected readonly int NodeIndex;

        public NodeDataActor(int nodeIndex)
        {
            NodeIndex = nodeIndex;
            NodeData = new NodeData(nodeIndex);
            SetReceive();
        }

        #region Message-handling

        private void SetReceive()
        {
            Receive<MultiNodeMessage>(message => NodeData.Put(message));


            Receive<EndSpec>(spec =>
            {
                

                //Send NodeData to parent for aggregation purposes
                Sender.Tell(NodeData.Copy());

                //Begin shutdown
                Context.Self.GracefulStop(TimeSpan.FromSeconds(1));
            });
        }

        #endregion
    }
}