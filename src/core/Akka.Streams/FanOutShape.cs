//-----------------------------------------------------------------------
// <copyright file="FanOutShape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Streams
{
    /// <summary>
    /// Base class for shapes with one inlet and a dynamically registered set of outlets.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the single inlet.</typeparam>
    public abstract class FanOutShape<TIn> : Shape
    {
        #region internal classes

        /// <summary>
        /// Supplies the inlet, initially registered outlets, and naming prefix used to construct a fan-out shape.
        /// </summary>
        public interface IInit
        {
            /// <summary>
            /// The inlet for the constructed shape.
            /// </summary>
            Inlet<TIn> Inlet { get; }
            /// <summary>
            /// Outlets supplied for reuse while constructing the shape.
            /// </summary>
            IEnumerable<Outlet> Outlets { get; }
            /// <summary>
            /// The prefix used to name newly created ports.
            /// </summary>
            string Name { get; }
        }

        /// <summary>
        /// Initializes a fan-out shape by creating a named inlet and using the name as its port prefix.
        /// </summary>
        [Serializable]
        public sealed class InitName : IInit
        {
            /// <summary>
            /// Creates initialization data that names a fan-out shape and its inlet.
            /// </summary>
            /// <param name="name">The non-empty name used as the prefix for the generated inlet.</param>
            /// <exception cref="ArgumentNullException">The name is <see langword="null"/> or empty.</exception>
            public InitName(string name)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

                Name = name;
                Inlet = new Inlet<TIn>(name + ".in");
                Outlets = Enumerable.Empty<Outlet>();
            }

            /// <summary>
            /// The inlet created from the initialization name.
            /// </summary>
            public Inlet<TIn> Inlet { get; }
            /// <summary>
            /// An empty sequence, since no existing outlets are supplied by name initialization.
            /// </summary>
            public IEnumerable<Outlet> Outlets { get; }
            /// <summary>
            /// The name used as the port prefix.
            /// </summary>
            public string Name { get; }
        }

        /// <summary>
        /// Initializes a fan-out shape with an existing inlet and a sequence of outlets to register.
        /// </summary>
        [Serializable]
        public sealed class InitPorts : IInit
        {
            /// <summary>
            /// Creates initialization data from an existing inlet and outlets.
            /// </summary>
            /// <param name="inlet">The inlet to use for the shape.</param>
            /// <param name="outlets">The outlets to register for reuse while constructing the shape.</param>
            /// <exception cref="ArgumentNullException">The inlet or outlets sequence is <see langword="null"/>.</exception>
            public InitPorts(Inlet<TIn> inlet, IEnumerable<Outlet> outlets)
            {
                Inlet = inlet ?? throw new ArgumentNullException(nameof(inlet));
                Outlets = outlets ?? throw new ArgumentNullException(nameof(outlets));
                Name = "FanOut";
            }

            /// <summary>
            /// The supplied inlet.
            /// </summary>
            public Inlet<TIn> Inlet { get; }
            /// <summary>
            /// The supplied outlets.
            /// </summary>
            public IEnumerable<Outlet> Outlets { get; }
            /// <summary>
            /// The default name prefix <c>FanOut</c> used for any newly created ports.
            /// </summary>
            public string Name { get; }
        }

        #endregion

        private readonly string _name;
        private ImmutableArray<Outlet> _outlets;
        private readonly IEnumerator<Outlet> _registered;

        /// <summary>
        /// Initializes the shape's inlet, available outlets, and port-name prefix.
        /// </summary>
        /// <param name="inlet">The inlet exposed by the shape.</param>
        /// <param name="registered">Outlets available for reuse in the order they are registered.</param>
        /// <param name="name">The prefix used to name any new outlets.</param>
        protected FanOutShape(Inlet<TIn> inlet, IEnumerable<Outlet> registered, string name)
        {
            In = inlet;
            Inlets = ImmutableArray.Create<Inlet>(inlet);
            _outlets = ImmutableArray<Outlet>.Empty;
            _name = name;
            _registered = registered.GetEnumerator();
        }

        /// <summary>
        /// Initializes this shape from the inlet, outlets, and name in the supplied data.
        /// </summary>
        /// <param name="init">The port and naming data used to initialize this shape.</param>
        protected FanOutShape(IInit init) : this(init.Inlet, init.Outlets, init.Name) { }

        /// <summary>
        /// The typed inlet exposed by this fan-out shape.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// The outlets registered with this shape, in construction order.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets => _outlets;

        /// <summary>
        /// The single inlet exposed by this fan-out shape.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Constructs the concrete fan-out shape using replacement port initialization data.
        /// </summary>
        /// <param name="init">The port and naming data for the constructed shape.</param>
        /// <returns>A concrete fan-out shape initialized with <paramref name="init"/>.</returns>
        protected abstract FanOutShape<TIn> Construct(IInit init);

        /// <summary>
        /// Creates an outlet using the next registered outlet when available, or a new outlet named from this shape's prefix.
        /// </summary>
        /// <param name="name">The suffix used when creating a new outlet.</param>
        /// <returns>The registered or newly created outlet, added to this shape's outlet list.</returns>
        protected Outlet<T> NewOutlet<T>(string name)
        {
            var p = _registered.MoveNext() ? (Outlet<T>)_registered.Current : new Outlet<T>($"{_name}.{name}");
            _outlets = _outlets.Add(p);
            return p;
        }

        /// <summary>
        /// Creates a copy of this shape with carbon-copied ports.
        /// </summary>
        /// <returns>A concrete fan-out shape with copied ports.</returns>
        public override Shape DeepCopy()
            => Construct(new InitPorts((Inlet<TIn>) In.CarbonCopy(), _outlets.Select(o => o.CarbonCopy())));

        /// <summary>
        /// Creates this shape's concrete type using replacement ports after validating their counts.
        /// </summary>
        /// <param name="inlets">The replacement inlet, which must match this shape's inlet count and type.</param>
        /// <param name="outlets">The replacement outlets, which must match this shape's outlet count and types.</param>
        /// <exception cref="ArgumentException">The replacement inlet or outlet count does not match this shape.</exception>
        /// <exception cref="InvalidCastException">A replacement port is not compatible with this shape's generic port type.</exception>
        /// <returns>A concrete fan-out shape using the replacement ports.</returns>
        public sealed override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 1) throw new ArgumentException(
                $"Proposed inlets [{string.Join(", ", inlets)}] do not fit FanOutShape");
            if (outlets.Length != _outlets.Length) throw new ArgumentException(
                $"Proposed outlets [{string.Join(", ", outlets)}] do not fit FanOutShape");

            return Construct(new InitPorts((Inlet<TIn>)inlets[0], outlets));
        }
    }

    /// <summary>
    /// A fan-out shape with one inlet and a fixed number of outlets that all emit the same element type.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the inlet.</typeparam>
    /// <typeparam name="TOut">The element type emitted by each outlet.</typeparam>
    public class UniformFanOutShape<TIn, TOut> : FanOutShape<TIn>
    {
        private readonly int _n;

        /// <summary>
        /// Creates a shape with <paramref name="n"/> outlets using the supplied initialization data.
        /// </summary>
        /// <param name="n">The number of outlets to create.</param>
        /// <param name="init">The inlet, any outlets to reuse, and the name prefix for new outlets.</param>
        public UniformFanOutShape(int n, IInit init) : base(init)
        {
            _n = n;
            Outs = Enumerable.Range(0, n).Select(i => NewOutlet<TOut>($"out{i}")).ToImmutableList();
        }

        /// <summary>
        /// Creates a shape with <paramref name="n"/> outlets and the default name prefix.
        /// </summary>
        /// <param name="n">The number of outlets to create.</param>
        public UniformFanOutShape(int n) : this(n, new InitName("UniformFanOut"))
        {
            
        }

        /// <summary>
        /// Creates a shape with <paramref name="n"/> outlets and the supplied name prefix.
        /// </summary>
        /// <param name="n">The number of outlets to create.</param>
        /// <param name="name">The prefix used to name the inlet and newly created outlets.</param>
        public UniformFanOutShape(int n, string name) : this(n, new InitName(name))
        {
            
        }

        /// <summary>
        /// Creates a shape using the supplied inlet and outlet ports.
        /// </summary>
        /// <param name="inlet">The inlet exposed by the shape.</param>
        /// <param name="outlets">The outlet ports used by the shape.</param>
        public UniformFanOutShape(Inlet<TIn> inlet, params Outlet<TOut>[] outlets)
            : this(outlets.Length, new InitPorts(inlet, outlets))
        {
            
        }

        /// <summary>
        /// The outlet ports, in their construction order.
        /// </summary>
        public IImmutableList<Outlet<TOut>> Outs { get; }

        /// <summary>
        /// Gets an outlet by its zero-based index.
        /// </summary>
        /// <param name="n">The zero-based outlet index.</param>
        /// <returns>The outlet at index <paramref name="n"/>.</returns>
        public Outlet<TOut> Out(int n) => Outs[n];

        /// <summary>
        /// Creates a uniform fan-out shape of this type from replacement port data.
        /// </summary>
        /// <param name="init">The inlet, replacement outlets, and name prefix.</param>
        /// <returns>A uniform fan-out shape with the same outlet count and replacement ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init) => new UniformFanOutShape<TIn, TOut>(_n, init);
    }
}
