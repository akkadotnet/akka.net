//-----------------------------------------------------------------------
// <copyright file="Graph.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Akka.Annotations;
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
        [InternalApi]
        IModule Module { get; }
    }

    /// <summary>
    /// </summary>
    /// <typeparam name="TShape">Type-level accessor for the shape parameter of this graph.</typeparam>
    /// <typeparam name="TMaterialized">TBD</typeparam>
    public interface IGraph<out TShape, out TMaterialized> : IGraph<TShape> where TShape : Shape
    {
        /// <summary>
        /// Change the attributes of this <see cref="IGraph{TShape}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        IGraph<TShape, TMaterialized> WithAttributes(Attributes attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        IGraph<TShape, TMaterialized> AddAttributes(Attributes attributes);

        /// <summary>
        /// Add a name attribute to this Graph.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        IGraph<TShape, TMaterialized> Named(string name);

        /// <summary>
        /// Put an asynchronous boundary around this Graph.
        /// </summary>
        /// <returns>TBD</returns>
        IGraph<TShape, TMaterialized> Async();
    }

    /// <summary>
    /// Allows creating additional API on top of an existing Graph by extending from this class and
    /// accessing the delegate
    /// </summary>
    /// <typeparam name="TShape"></typeparam>
    /// <typeparam name="TMat"></typeparam>
    public abstract class GraphDelegate<TShape, TMat> : IGraph<TShape, TMat>
        where TShape : Shape
    {
        protected readonly IGraph<TShape, TMat> Inner;

        protected GraphDelegate(IGraph<TShape, TMat> inner)
        {
            Inner = inner;
        }

        public TShape Shape
        {
            [MethodImpl((MethodImplOptions.AggressiveInlining))]
            get => Inner.Shape;
        }

        public IModule Module
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Inner.Module;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IGraph<TShape, TMat> WithAttributes(Attributes attributes) => 
            Inner.WithAttributes(attributes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IGraph<TShape, TMat> AddAttributes(Attributes attributes) => Inner.AddAttributes(attributes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IGraph<TShape, TMat> Named(string name) => Inner.Named(name);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IGraph<TShape, TMat> Async() => Inner.Async();
    }
}
