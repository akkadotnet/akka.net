using Akka.Streams.Implementation;

namespace Akka.Streams
{
    /// <summary>
    /// </summary>
    /// <typeparam name="TShape">Type-level accessor for the shape parameter of this graph.</typeparam>
    /// <typeparam name="TMaterializer"></typeparam>
    public interface IGraph<out TShape, TMaterializer> where TShape : Shape
    {
        /// <summary>
        /// The shape of a graph is all that is externally visible: its inlets and outlets.
        /// </summary>
        TShape Shape { get; }

        /// <summary>
        /// INTERNAL API: Every materializable element must be backed by a stream layout module
        /// </summary>
        IModule Module { get; }

        IGraph<TShape, TMaterializer> WithAttributes(Attributes attributes);
        IGraph<TShape, TMaterializer> Named(string name);
    }
}