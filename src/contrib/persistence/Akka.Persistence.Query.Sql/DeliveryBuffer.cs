//-----------------------------------------------------------------------
// <copyright file="DeliveryBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akka.Persistence.Query.Sql
{
    internal class DeliveryBuffer<T>
    {
        public ImmutableArray<T> Buffer { get; private set; } = ImmutableArray<T>.Empty;
        public bool IsEmpty => Buffer.IsEmpty;
        public int Length => Buffer.Length;
        
        private readonly Action<T> _onNext;

        public DeliveryBuffer(Action<T> onNext)
        {
            _onNext = onNext;
        }

        public void Add(T element)
        {
            Buffer = Buffer.Add(element);
        }
        public void AddRange(IEnumerable<T> elements)
        {
            Buffer = Buffer.AddRange(elements);
        }

        public void DeliverBuffer(long demand)
        {
            if (!Buffer.IsEmpty && demand > 0)
            {
                var totalDemand = Math.Min((int) demand, Buffer.Length);
                if (Buffer.Length == 1)
                {
                    // optimize for this common case
                    _onNext(Buffer[0]);
                    Buffer = ImmutableArray<T>.Empty;
                }
                else if (demand <= int.MaxValue)
                {
                    for (var i = 0; i < totalDemand; i++)
                        _onNext(Buffer[i]);

                    Buffer = Buffer.RemoveRange(0, totalDemand);
                }
                else
                {
                    foreach (var element in Buffer)
                        _onNext(element);

                    Buffer = ImmutableArray<T>.Empty;
                }
            }
        }

    }
}
