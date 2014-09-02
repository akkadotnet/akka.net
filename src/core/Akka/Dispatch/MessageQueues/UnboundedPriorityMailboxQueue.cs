using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Akka.Actor;
#if MONO
using TQueue = Akka.Util.MonoConcurrentQueue<Akka.Actor.Envelope>;
#else
using TQueue = System.Collections.Concurrent.ConcurrentQueue<Akka.Actor.Envelope>;
#endif

namespace Akka.Dispatch.MessageQueues
{

    public class PriorityQueue
    {
        private readonly List<Envelope> _data;
        private Func<object, int> _priorityCalculator = message => 1;

        public PriorityQueue()
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
                if (rc <= li && _priorityCalculator(_data[rc]).CompareTo(_priorityCalculator(_data[ci])) < 0) // if there is a rc (ci + 1), and it is smaller than left child, use the rc instead
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
    } // PriorityQueue

    public class UnboundedPriorityMessageQueue : MessageQueue, UnboundedMessageQueueSemantics
    {
        private readonly TQueue _queue = new TQueue();
        private readonly PriorityQueue _prioQueue = new PriorityQueue();

        public UnboundedPriorityMessageQueue(Func<object, int> priorityCalculator)
        {
            _prioQueue.SetPriorityCalculator(priorityCalculator);
        }


        public void Enqueue(Envelope envelope)
        {
            _queue.Enqueue(envelope);
        }

        public bool HasMessages
        {
            get { return _queue.Count > 0; }
        }

        public int Count
        {
            get { return _queue.Count; }
        }

        public bool TryDequeue(out Envelope envelope)
        {
            //This should only be called from Mailbox.Run, and therefore be protected by Actor concurrency constraints
            //thus, the below non threadsafe code is safe in this context..

            //drain envelopes from ConcurrentQueue into priority queue
            Envelope tmp;
            while (_queue.TryDequeue(out tmp))
            {
                _prioQueue.Enqueue(tmp);
            }

            //return the highest prio message from priority queue
            if (_prioQueue.Count() > 0)
            {
                envelope = _prioQueue.Dequeue();
                return true;
            }
            envelope = default(Envelope);
            return false;
        }
    }
}
