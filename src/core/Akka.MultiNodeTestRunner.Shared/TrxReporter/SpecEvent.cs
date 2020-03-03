//-----------------------------------------------------------------------
// <copyright file="SpecEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;

namespace Akka.MultiNodeTestRunner.Shared.AzureDevOps
{
    public class SpecEvent<T>
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
