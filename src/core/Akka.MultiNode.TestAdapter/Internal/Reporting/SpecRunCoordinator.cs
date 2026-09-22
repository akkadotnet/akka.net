//-----------------------------------------------------------------------
// <copyright file="SpecRunCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Sinks;

namespace Akka.MultiNode.TestAdapter.Internal.Reporting
{
    /// <summary>
    /// Actor responsible for organizing the results of an individual spec
    /// </summary>
    internal class SpecRunCoordinator : ReceiveActor
    {
        public SpecRunCoordinator(string className, string methodName, IList<NodeTest> nodes)
        {
            Nodes = nodes;
            MethodName = methodName;
            ClassName = className;
            FactData = new FactData(string.Format("{0}.{1}", className, methodName));
            _nodeActors = new Dictionary<int, IActorRef>();
            SetReceive();
        }

        public string ClassName { get; private set; }

        public string MethodName { get; private set; }

        public IList<NodeTest> Nodes { get; private set; }

        /// <summary>
        /// All of the data for this individual spec
        /// </summary>
        protected FactData FactData;

        /// <summary>
        /// Internal dictionary used to route messages to their discrete nodes
        /// </summary>
        private readonly Dictionary<int, IActorRef> _nodeActors;

        /// <summary>
        /// The original sender of <see cref="EndSpec"/>, awaiting the aggregated <see cref="FactData"/>.
        /// </summary>
        private IActorRef _endSpecSender;

        /// <summary>
        /// <see cref="NodeData"/> replies collected from the child <see cref="NodeDataActor"/> instances
        /// while handling <see cref="EndSpec"/>.
        /// </summary>
        private readonly List<NodeData> _collectedNodeData = new List<NodeData>();

        #region Actor Lifecycle

        protected override void PreStart()
        {
            //create all of the NodeFactActor instances
            foreach (var node in Nodes)
            {
                var index = node.Node;
                var role = node.Role;
                _nodeActors.Add(index, Context.ActorOf(Props.Create(() => new NodeDataActor(index, role))));
            }
        }

        #endregion

        #region Message-handling

        private void SetReceive()
        {
            Receive<MultiNodeTestRunnerMessage>(message =>
            {
                FactData.Put(message);
            });
            Receive<MultiNodeMessage>(message => RouteToNodeActor(message));
            Receive<EndSpec>(spec => HandleEndSpec(spec));
            Receive<NodeData>(nodeData => HandleNodeData(nodeData));
        }

        /// <summary>
        /// Send a <see cref="MultiNodeMessage"/> to the correct <see cref="NodeDataActor"/> based on the 
        /// <see cref="MultiNodeMessage.NodeIndex"/> property.
        /// </summary>
        private void RouteToNodeActor(MultiNodeMessage message)
        {
            var actor = _nodeActors[message.NodeIndex];
            actor.Tell(message);
        }

        /// <summary>
        /// Ask every child <see cref="NodeDataActor"/> to report its results, then collect the replies.
        /// Each child replies exactly once with its <see cref="NodeData"/>, so aggregation completes
        /// deterministically as soon as all <see cref="Nodes"/> have reported — there is no Ask timeout
        /// to race under dispatcher load.
        /// </summary>
        private void HandleEndSpec(EndSpec endSpec)
        {
            _endSpecSender = Context.Sender;

            // No child actors (e.g. a spec with no nodes) — nothing to collect, reply immediately.
            if (_nodeActors.Count == 0)
            {
                CompleteAndReply();
                return;
            }

            // Tell (not Ask-with-timeout): each child replies to us with its NodeData.Copy().
            foreach (var node in _nodeActors)
                node.Value.Tell(endSpec);
        }

        /// <summary>
        /// Collect a <see cref="NodeData"/> reply from a child <see cref="NodeDataActor"/>. Once every
        /// node has reported, aggregate the results and return the completed <see cref="FactData"/>.
        /// </summary>
        private void HandleNodeData(NodeData nodeData)
        {
            _collectedNodeData.Add(nodeData);

            if (_collectedNodeData.Count == _nodeActors.Count)
                CompleteAndReply();
        }

        /// <summary>
        /// Aggregate every collected <see cref="NodeData"/>, mark the spec complete, return the
        /// <see cref="FactData"/> to the original <see cref="EndSpec"/> sender, and shut down.
        /// </summary>
        private void CompleteAndReply()
        {
            FactData.AddNodes(_collectedNodeData.ToArray());

            //mark this test as complete
            FactData.Complete();

            //Send our FactData back to the original EndSpec sender
            _endSpecSender.Tell(FactData.Copy());

            //Shut ourselves down
            Self.GracefulStop(TimeSpan.FromSeconds(1));
        }

        #endregion

    }
}

