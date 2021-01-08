//-----------------------------------------------------------------------
// <copyright file="TestRunTree.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    /// <summary>
    /// The top of the tree - represents an entire test run.
    /// </summary>
    public class TestRunTree : IEquatable<TestRunTree>
    {
        private readonly List<FactData> _specs;

        public TestRunTree(long startTime) : this(startTime, new List<FactData>())
        {
        }

        public TestRunTree(long startTime, List<FactData> specs) : this(startTime, specs, null, null)
        {
    
        }

        [JsonConstructor]
        public TestRunTree(long startTime, List<FactData> specs, long? endTime, bool? passed)
        {
            StartTime = startTime;
            _specs = specs;
            EndTime = endTime;
            Passed = passed;
        }

        /// <summary>
        /// The absolute time tests began for this individual node
        /// </summary>
        public long StartTime { get; private set; }

        /// <summary>
        /// The absolute time tests ended for this individual node
        /// </summary>
        public long? EndTime { get; set; }

        /// <summary>
        /// Whether or not this test has acquired a result yet
        /// </summary>
        public bool? Passed { get; set; }

        public IEnumerable<FactData> Specs { get { return _specs; } }

        public TimeSpan Elapsed
        {
            get
            {
                return ((EndTime.HasValue ? new DateTime(EndTime.Value) : DateTime.UtcNow) - new DateTime(StartTime));
            }
        }

        public void AddSpec(FactData spec)
        {
            _specs.Add(spec);
        }

        public void Complete()
        {
            var passes = _specs.Select(x => x.Passed);
            if (passes.All(x => x.HasValue))
            {
                Passed = passes.All(x => x.Value);
                EndTime = DateTime.UtcNow.Ticks;
            }
        }

        /// <summary>
        /// Returns a deep copy of the current tree.
        /// </summary>
        public TestRunTree Copy(bool? passed = null)
        {
            var specs = new FactData[_specs.Count];
            _specs.CopyTo(specs);

            return new TestRunTree(StartTime, specs.ToList(), EndTime, passed ?? Passed);
        }

        #region Equality

        /// <inheritdoc/>
        public bool Equals(TestRunTree other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return 
                _specs.SequenceEqual(other._specs) 
                && StartTime == other.StartTime 
                && EndTime == other.EndTime 
                && Passed.Equals(other.Passed);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TestRunTree) obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (_specs != null ? _specs.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ StartTime.GetHashCode();
                hashCode = (hashCode*397) ^ EndTime.GetHashCode();
                hashCode = (hashCode*397) ^ Passed.GetHashCode();
                return hashCode;
            }
        }

        #endregion
    }

    /// <summary>
    /// A collection of data about a particular test
    /// </summary>
    public class FactData : IEquatable<FactData>
    {
        private readonly Dictionary<int, NodeData> _nodes;

        /// <summary>
        /// Messages sent by the test runner for this spec, rather than any individual nodes
        /// </summary>
        private readonly SortedSet<MultiNodeMessage> _testRunnerTimeLine;

        public FactData(string factName) 
            : this(factName, DateTime.UtcNow.Ticks, new Dictionary<int, NodeData>(), new  List<MultiNodeMessage>()) { }

        public FactData(string factName, long startTime, Dictionary<int, NodeData> nodes, IEnumerable<MultiNodeMessage> testRunnerTimeLine)
            : this(factName, startTime, nodes, testRunnerTimeLine, null, null)
        {
         
        }

        public FactData(string factName, long startTime, Dictionary<int, NodeData> nodes, IEnumerable<MultiNodeMessage> testRunnerTimeLine, long? endTime, bool? passed)
        {
            _nodes = nodes;
            _testRunnerTimeLine = new SortedSet<MultiNodeMessage>(testRunnerTimeLine ?? new List<MultiNodeMessage>());
            StartTime = startTime;
            FactName = factName;
            EndTime = endTime;
            Passed = passed;
        }

        [JsonConstructor]
        public FactData(string factName, long startTime, IEnumerable<NodeData> nodes, IEnumerable<MultiNodeMessage> testRunnerTimeLine, long? endTime, bool? passed)
            : this(factName, startTime, 
            nodes == null ? new Dictionary<int, NodeData>() : 
            nodes.ToDictionary(key => key.NodeIndex, data => data), 
            testRunnerTimeLine, endTime, passed)
        {
        }


        public string FactName { get; private set; }

        public IEnumerable<MultiNodeMessage> RunnerMessages { get { return _testRunnerTimeLine; } }

        public Dictionary<int, NodeData> NodeFacts { get { return _nodes; } }

        /// <summary>
        /// The absolute time tests began for this individual node
        /// </summary>
        public long StartTime { get; private set; }

        /// <summary>
        /// The absolute time tests ended for this individual node
        /// </summary>
        public long? EndTime { get; set; }

        /// <summary>
        /// Whether or not this test has acquired a result yet
        /// </summary>
        public bool? Passed { get; set; }

        public TimeSpan Elapsed
        {
            get
            {
                return ((EndTime.HasValue ? new DateTime(EndTime.Value) : DateTime.UtcNow) - new DateTime(StartTime));
            }
        }

        public void Complete()
        {
            var passes = _nodes.Select(x => x.Value.Passed);
            if (passes.All(x => x.HasValue))
            {
                Passed = passes.All(x => x.Value);
                EndTime = DateTime.UtcNow.Ticks;
            }
        }

        public void AddNodes(NodeData[] nodeDatum)
        {
            foreach(var node in nodeDatum)
                AddNode(node);
        }

        public void AddNode(NodeData nodeData)
        {
            _nodes[nodeData.NodeIndex] = nodeData;
        }

        public void Put(MultiNodeMessage message)
        {
            _testRunnerTimeLine.Add(message);
        }

        /// <summary>
        /// Creates a deep copy of the current <see cref="FactData"/> object.
        /// </summary>
        public FactData Copy()
        {
            var nodeData = new Dictionary<int, NodeData>(_nodes.Count);
            foreach (var node in _nodes)
            {
                //make a copy of the NodeData too
                nodeData[node.Key] = node.Value.Copy();
            }

            var messageTimeline = new MultiNodeMessage[_testRunnerTimeLine.Count];
            _testRunnerTimeLine.CopyTo(messageTimeline);

            return new FactData(FactName, StartTime, nodeData, messageTimeline, EndTime, Passed);
        }

        #region Equality

        /// <inheritdoc/>
        public bool Equals(FactData other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return _nodes.SequenceEqual(other._nodes)
                && _testRunnerTimeLine.SetEquals(other._testRunnerTimeLine)
                && string.Equals(FactName, other.FactName) 
                && StartTime == other.StartTime 
                && EndTime == other.EndTime 
                && Passed.Equals(other.Passed);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((FactData) obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (FactName.GetHashCode() * 397) ^ StartTime.GetHashCode();
            }
        }

        #endregion
    }

    /// <summary>
    /// A collection of data about the status of a particular node
    /// </summary>
    public class NodeData : IEquatable<NodeData>
    {
        private readonly SortedSet<MultiNodeMessage> _eventTimeLine;

        public NodeData(int nodeIndex, string nodeRole) : this(nodeIndex, nodeRole, DateTime.UtcNow.Ticks, new List<MultiNodeMessage>()) { }

        public NodeData(int nodeIndex, string nodeRole, long startTime, IEnumerable<MultiNodeMessage> eventTimeLine) 
            : this(nodeIndex, nodeRole, startTime, eventTimeLine, null, null)
        {
          
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        [JsonConstructor]
        public NodeData(int nodeIndex, string nodeRole, long startTime, IEnumerable<MultiNodeMessage> eventTimeLine, long? endTime,
            bool? passed)
        {
            NodeIndex = nodeIndex;
            NodeRole = nodeRole;
            StartTime = startTime;
            _eventTimeLine = new SortedSet<MultiNodeMessage>(eventTimeLine ?? new List<MultiNodeMessage>());
            EndTime = endTime;
            Passed = passed;
        }

        /// <summary>
        /// The position of this node in the 0...N index of all nodes in the set.
        /// </summary>
        public int NodeIndex { get; private set; }

        /// <summary>
        /// The Role of this node.
        /// </summary>
        public string NodeRole { get; private set; }

        /// <summary>
        /// The absolute time tests began for this individual node
        /// </summary>
        public long StartTime { get; private set; }

        /// <summary>
        /// The absolute time tests ended for this individual node
        /// </summary>
        public long? EndTime { get; set; }

        /// <summary>
        /// Whether or not this test has acquired a result yet
        /// </summary>
        public bool? Passed { get; set; }

        /// <summary>
        /// Filter all of the result messages to the top
        /// </summary>
        public SortedSet<MultiNodeResultMessage> ResultMessages
        {
            get
            {
                return new SortedSet<MultiNodeResultMessage>(_eventTimeLine.Where(x => x.GetType() == typeof(MultiNodeResultMessage)).Cast<MultiNodeResultMessage>());
            }
        }

        public TimeSpan Elapsed
        {
            get
            {
                return ((EndTime.HasValue ? new DateTime(EndTime.Value) : DateTime.UtcNow) - new DateTime(StartTime));
            }
        }

        /// <summary>
        /// All of the events that occurred for this node - time sequenced.
        /// </summary>
        public IEnumerable<MultiNodeMessage> EventStream
        {
            get { return _eventTimeLine; }
        }

        /// <summary>
        /// Pushes a new message onto the <see cref="EventStream"/> for this node.
        /// </summary>
        public void Put(MultiNodeMessage message)
        {
            //Check for passed messages
            if (!Passed.HasValue && message is MultiNodeResultMessage)
            {
                var resultMessage = message as MultiNodeResultMessage;
                Passed = resultMessage.Passed;
                EndTime = DateTime.UtcNow.Ticks;
            }

            _eventTimeLine.Add(message);
        }

        /// <summary>
        /// Creates a deep copy of the current <see cref="NodeData"/> object
        /// </summary>
        public NodeData Copy()
        {
            var events = new MultiNodeMessage[_eventTimeLine.Count];
            _eventTimeLine.CopyTo(events);

            return new NodeData(NodeIndex, NodeRole, StartTime, events, EndTime, Passed);
        }

        #region Equality

        /// <inheritdoc/>
        public bool Equals(NodeData other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return _eventTimeLine.SetEquals(other._eventTimeLine)
                && NodeIndex == other.NodeIndex 
                && String.Equals(NodeRole, other.NodeRole, StringComparison.Ordinal)
                && StartTime == other.StartTime 
                && EndTime == other.EndTime 
                && Passed.Equals(other.Passed);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((NodeData) obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (NodeIndex * 397) ^ (NodeRole.GetHashCode() * 397) ^ StartTime.GetHashCode();
            }
        }

        #endregion
   
    }
}

