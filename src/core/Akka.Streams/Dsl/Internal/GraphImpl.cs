using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl.Internal
{
    internal class GraphImpl<TShape, TMat> : IGraph<TShape, TMat> where TShape : Shape
    {
        public GraphImpl(TShape shape, IModule module)
        {
            Shape = shape;
            Module = module;
        }

        public TShape Shape { get; }
        public IModule Module { get; }
        public IGraph<TShape, TMat> WithAttributes(Attributes attributes)
        {
            return new GraphImpl<TShape, TMat>(Shape, Module.WithAttributes(attributes));
        }

        public IGraph<TShape, TMat> AddAttributes(Attributes attributes)
        {
            return WithAttributes(Module.Attributes.And(attributes));
        }

        public IGraph<TShape, TMat> Named(string name)
        {
            return AddAttributes(Attributes.CreateName(name));
        }

        public IGraph<TShape, TMat> Async()
        {
            return AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }
    }
}