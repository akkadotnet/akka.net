using System;

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
            if (Shape.Equals(shape)) return this;
            throw new NotSupportedException("cannot replace the shape of a FlowModule");
        }
    }

}