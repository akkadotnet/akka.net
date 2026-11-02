// -----------------------------------------------------------------------
//  <copyright file="SpecEvent.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.MultiNode.TestAdapter.Internal.TrxReporter
{
    internal class SpecEvent<T>
    {
        public SpecEvent(DateTime time, T value)
        {
            Time = time;
            Value = value;
        }

        public DateTime Time { get; }
        public T Value { get; }

        public void Deconstruct(out DateTime time, out T value)
        {
            time = Time;
            value = Value;
        }
    }
}