//-----------------------------------------------------------------------
// <copyright file="ListPriorityQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Util
{
    /// <summary>
    /// Priority queue implemented using a simple list with binary search for inserts.
    /// This specific implementation is cheap in terms of memory but weak in terms of performance.
    /// See http://visualstudiomagazine.com/articles/2012/11/01/priority-queues-with-c.aspx for original implementation
    /// This specific version is adapted for Envelopes only and calculates a priority of envelope.Message
    /// </summary>
    public class ListPriorityQueue
    {
        private readonly List<Envelope> _data;
        private Func<object, int> _priorityCalculator = message => 1;

        public ListPriorityQueue()
        {
            _data = new List<Envelope>();
        }

        public void SetPriorityCalculator(Func<object, int> priorityCalculator)
        {
            _priorityCalculator = priorityCalculator;
        }

        public void Enqueue(Envelope item)
        {

            _data.Add(item);
            var ci = _data.Count - 1; // child index; start at end
            while (ci > 0)
            {
                var pi = (ci - 1) / 2; // parent index
                if (_priorityCalculator(_data[ci].Message).CompareTo(_priorityCalculator(_data[pi].Message)) >= 0) break; // child item is larger than (or equal) parent so we're done
                var tmp = _data[ci]; _data[ci] = _data[pi]; _data[pi] = tmp;
                ci = pi;
            }
        }

        public Envelope Dequeue()
        {
            // assumes pq is not empty; up to calling code
            var li = _data.Count - 1; // last index (before removal)
            var frontItem = _data[0];   // fetch the front
            _data[0] = _data[li];
            _data.RemoveAt(li);

            --li; // last index (after removal)
            var pi = 0; // parent index. start at front of pq
            while (true)
            {
                var ci = pi * 2 + 1; // left child index of parent
                if (ci > li) break;  // no children so done
                var rc = ci + 1;     // right child
                if (rc <= li && _priorityCalculator(_data[rc].Message).CompareTo(_priorityCalculator(_data[ci].Message)) < 0) // if there is a rc (ci + 1), and it is smaller than left child, use the rc instead
                    ci = rc;
                if (_priorityCalculator(_data[pi].Message).CompareTo(_priorityCalculator(_data[ci].Message)) <= 0) break; // parent is smaller than (or equal to) smallest child so done
                var tmp = _data[pi]; _data[pi] = _data[ci]; _data[ci] = tmp; // swap parent and child
                pi = ci;
            }
            return frontItem;
        }

        public Envelope Peek()
        {
            var frontItem = _data[0];
            return frontItem;
        }

        public int Count()
        {
            return _data.Count;
        }

        public override string ToString()
        {
            var s = "";
            for (var i = 0; i < _data.Count; ++i)
                s += _data[i].ToString() + " ";
            s += "count = " + _data.Count;
            return s;
        }

        public bool IsConsistent()
        {
            // is the heap property true for all data?
            if (_data.Count == 0) return true;
            var li = _data.Count - 1; // last index
            for (var pi = 0; pi < _data.Count; ++pi) // each parent index
            {
                var lci = 2 * pi + 1; // left child index
                var rci = 2 * pi + 2; // right child index

                if (lci <= li && _priorityCalculator(_data[pi].Message).CompareTo(_priorityCalculator(_data[lci].Message)) > 0) return false; // if lc exists and it's greater than parent then bad.
                if (rci <= li && _priorityCalculator(_data[pi].Message).CompareTo(_priorityCalculator(_data[rci].Message)) > 0) return false; // check the right child too.
            }
            return true; // passed all checks
        } // IsConsistent
    } // ListPriorityQueue


}

