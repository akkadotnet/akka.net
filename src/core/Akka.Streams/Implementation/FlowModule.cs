//-----------------------------------------------------------------------
// <copyright file="FlowModule.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

namespace Akka.Streams.Implementation
{
    internal abstract class FlowModule<TIn, TOut> : AtomicModule
    {
        public readonly Inlet<TIn> In = new Inlet<TIn>("Flow.in");
        public readonly Outlet<TOut> Out = new Outlet<TOut>("Flow.out");

        protected FlowModule()
        {
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }
        
        public override Shape Shape { get; }

        public override IModule ReplaceShape(Shape shape)
        {
            if (Shape.Equals(shape))
                return this;
            throw new NotSupportedException("cannot replace the shape of a FlowModule");
        }

        protected virtual string Label => GetType().Name;

        public sealed override string ToString() => $"{Label} [{GetHashCode()}%08x]";
    }

}