using Akka.Streams.Implementation;

namespace Akka.Streams
{
    /// <summary>
    /// </summary>
    /// <typeparam name="TShape">Type-level accessor for the shape parameter of this graph.</typeparam>
    public interface IGraph<out TShape> where TShape : Shape
    {
        /// <summary>
        /// The shape of a graph is all that is externally visible: its inlets and outlets.
        /// </summary>
        TShape Shape { get; }

        /// <summary>
        /// INTERNAL API: Every materializable element must be backed by a stream layout module
        /// </summary>
        IModule Module { get; }
    }

    /// <summary>
    /// </summary>
    /// <typeparam name="TShape">Type-level accessor for the shape parameter of this graph.</typeparam>
    /// <typeparam name="TMaterializer"></typeparam>
    public interface IGraph<out TShape, TMaterializer> : IGraph<TShape> where TShape : Shape
    {
        /// <summary>
        /// Change the attributes of this <see cref="IGraph{TShape}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<TShape, TMaterializer> WithAttributes(Attributes attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<TShape, TMaterializer> AddAttributes(Attributes attributes);

        /// <summary>
        /// Add a 'name' attribute to this Flow.
        /// </summary>
        IGraph<TShape, TMaterializer> Named(string name);

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        IGraph<TShape, TMaterializer> Async();
    }
}