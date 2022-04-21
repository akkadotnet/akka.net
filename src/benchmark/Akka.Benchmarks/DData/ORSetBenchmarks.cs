using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using Akka.DistributedData;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.DData
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ORSetBenchmarks
    {
        [Params(25)]
        public int NumElements;

        [Params(10)]
        public int NumNodes;

        [Params(100)]
        public int Iterations;

        private UniqueAddress[] _nodes;
        private string[] _elements;

        private readonly string _user1 = "{\"username\":\"john\",\"password\":\"coltrane\"}";
        private readonly string _user2 = "{\"username\":\"sonny\",\"password\":\"rollins\"}";
        private readonly string _user3 = "{\"username\":\"charlie\",\"password\":\"parker\"}";
        private readonly string _user4 = "{\"username\":\"charles\",\"password\":\"mingus\"}";

        // has data from all nodes
        private ORSet<string> _c1 = ORSet<String>.Empty;

        // has additional items from all nodes
        private ORSet<string> _c2 = ORSet<String>.Empty;

        // has removed items from all nodes
        private ORSet<string> _c3 = ORSet<String>.Empty;

        [GlobalSetup]
        public void Setup()
        {
            var newNodes = new List<UniqueAddress>(NumNodes);
            foreach(var i in Enumerable.Range(0, NumNodes)){
                var address = new Address("akka.tcp", "Sys", "localhost", 2552 + i);
                var uniqueAddress = new UniqueAddress(address, i);
                newNodes.Add(uniqueAddress);
            }
            _nodes = newNodes.ToArray();

            var newElements = new List<string>(NumNodes);
            foreach(var i in Enumerable.Range(0, NumElements)){
                newElements.Add(i.ToString());
            }
            _elements = newElements.ToArray();

            _c1 = ORSet<String>.Empty;
            foreach(var node in _nodes){
                _c1 = _c1.Add(node, _elements[0]);
            }

            // add some data that _c2 doesn't have
            _c2 = _c1;
            foreach(var node in _nodes.Skip(NumNodes/2)){
                _c2 = _c2.Add(node, _elements[1]);
            }

            _c3 = _c1;
            foreach(var node in _nodes.Take(NumNodes/2)){
                _c3 = _c3.Remove(node, _elements[0]);
            }
        }

        [Benchmark]
        public void Should_add_node_to_ORSet()
        {
            for (var i = 0; i < Iterations; i++)
            {
                var init = ORSet<string>.Empty;
                foreach (var node in _nodes)
                {
                    init = init.Add(node, _elements[0]);
                }
            }
            
        }

        [Benchmark]
        public void Should_add_elements_for_Same_node()
        {
            for (var i = 0; i < Iterations; i++)
            {
                var init = ORSet<string>.Empty;
                foreach (var element in _elements)
                {
                    init = init.Add(_nodes[0], element);
                }
            }
        }

        [Benchmark]
        public void Should_merge_in_new_Elements_from_other_nodes(){
            for (var i = 0; i < Iterations; i++)
            {
                var c4 = _c1.Merge(_c2);
            }
            
        }

        [Benchmark]
        public void Should_merge_in_removed_Elements_from_other_nodes(){
            for (var i = 0; i < Iterations; i++)
            {
                var c4 = _c1.Merge(_c3);
            }
            
        }

        [Benchmark]
        public void Should_merge_in_add_and_removed_Elements_from_other_nodes(){
            for (var i = 0; i < Iterations; i++)
            {
                var c4 = _c1.Merge(_c2).Merge(_c3);
            }
        }
    }
}
