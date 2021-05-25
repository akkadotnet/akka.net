using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.DistributedData;

namespace DDataStressTest
{
    class Program
    {
        public const int NumElements = 25;

        public const int NumNodes = 10;

        public const int Iterations = 10000;

        private const string _user1 = "{\"username\":\"john\",\"password\":\"coltrane\"}";
        private const string _user2 = "{\"username\":\"sonny\",\"password\":\"rollins\"}";
        private const string _user3 = "{\"username\":\"charlie\",\"password\":\"parker\"}";
        private const string _user4 = "{\"username\":\"charles\",\"password\":\"mingus\"}";


        static async Task Main(string[] args)
        {
            UniqueAddress[] _nodes;

            string[] _elements;


            // has data from all nodes
            ORSet<string> _c1 = ORSet<String>.Empty;

            // has additional items from all nodes
            ORSet<string> _c2 = ORSet<String>.Empty;

            // has removed items from all nodes
            ORSet<string> _c3 = ORSet<String>.Empty;

            var newNodes = new List<UniqueAddress>(NumNodes);
            foreach (var i in Enumerable.Range(0, NumNodes))
            {
                var address = new Address("akka.tcp", "Sys", "localhost", 2552 + i);
                var uniqueAddress = new UniqueAddress(address, i);
                newNodes.Add(uniqueAddress);
            }
            _nodes = newNodes.ToArray();

            var newElements = new List<string>(NumNodes);
            foreach (var i in Enumerable.Range(0, NumElements))
            {
                newElements.Add(i.ToString());
            }
            _elements = newElements.ToArray();

            _c1 = ORSet<String>.Empty;
            foreach (var node in _nodes)
            {
                _c1 = _c1.Add(node, _elements[0]);
            }

            // add some data that _c2 doesn't have
            _c2 = _c1;
            foreach (var node in _nodes.Skip(NumNodes / 2))
            {
                _c2 = _c2.Add(node, _elements[1]);
            }

            _c3 = _c1;
            foreach (var node in _nodes.Take(NumNodes / 2))
            {
                _c3 = _c3.Remove(node, _elements[0]);
            }

            var init = ORSet<string>.Empty;

            foreach (var element in _elements)
            {
                foreach (var node in _nodes)
                {
                    init = init.Add(node, element);
                }
            }

            _c1.Merge(init).Merge(_c2).Merge(_c3);

            await Task.Delay(5000);
        }
    }
}
