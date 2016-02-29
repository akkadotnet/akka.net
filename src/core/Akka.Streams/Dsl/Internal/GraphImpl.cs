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
            return new GraphImpl<TShape, TMat>(Shape, Module.WithAttributes(attributes).Nest());
        }

        public IGraph<TShape, TMat> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }
}