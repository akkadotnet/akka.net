//-----------------------------------------------------------------------
// <copyright file="FanInShape.cs" company="Akka.NET Project">
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
    /// Base class for shapes with one outlet and a dynamically registered set of inlets.
    /// </summary>
    /// <typeparam name="TOut">The element type emitted by the single outlet.</typeparam>
    public abstract class FanInShape<TOut> : Shape
    {
        #region internal classes

        /// <summary>
        /// Supplies the outlet, initially registered inlets, and naming prefix used to construct a fan-in shape.
        /// </summary>
        public interface IInit
        {
            /// <summary>
            /// The outlet for the constructed shape.
            /// </summary>
            Outlet<TOut> Outlet { get; }
            /// <summary>
            /// Inlets supplied for reuse while constructing the shape.
            /// </summary>
            IEnumerable<Inlet> Inlets { get; }
            /// <summary>
            /// The prefix used to name newly created ports.
            /// </summary>
            string Name { get; }
        }

        /// <summary>
        /// Initializes a fan-in shape by creating a named outlet and using the name as its port prefix.
        /// </summary>
        [Serializable]
        public sealed class InitName : IInit
        {
            private readonly string _name;
            private readonly Outlet<TOut> _outlet;

            /// <summary>
            /// Creates initialization data that names a fan-in shape and its outlet.
            /// </summary>
            /// <param name="name">The non-empty name used as the prefix for the generated outlet.</param>
            /// <exception cref="ArgumentNullException">The name is <see langword="null"/> or empty.</exception>
            public InitName(string name)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

                _name = name;
                _outlet = new Outlet<TOut>(name + ".out");
            }

            /// <summary>
            /// The outlet created from the initialization name.
            /// </summary>
            public Outlet<TOut> Outlet => _outlet;
            /// <summary>
            /// An empty sequence, since no existing inlets are supplied by name initialization.
            /// </summary>
            public IEnumerable<Inlet> Inlets => Enumerable.Empty<Inlet>();
            /// <summary>
            /// The name used as the port prefix.
            /// </summary>
            public string Name => _name;
        }

        /// <summary>
        /// Initializes a fan-in shape with an existing outlet and a sequence of inlets to register.
        /// </summary>
        [Serializable]
        public sealed class InitPorts : IInit
        {
            private readonly Outlet<TOut> _outlet;
            private readonly IEnumerable<Inlet> _inlets;

            /// <summary>
            /// Creates initialization data from an existing outlet and inlets.
            /// </summary>
            /// <param name="outlet">The outlet to use for the shape.</param>
            /// <param name="inlets">The inlets to register for reuse while constructing the shape.</param>
            public InitPorts(Outlet<TOut> outlet, IEnumerable<Inlet> inlets)
            {
                _outlet = outlet ?? throw new ArgumentNullException(nameof(outlet));
                _inlets = inlets ?? throw new ArgumentNullException(nameof(inlets));
            }

            /// <summary>
            /// The supplied outlet.
            /// </summary>
            public Outlet<TOut> Outlet => _outlet;
            /// <summary>
            /// The supplied inlets.
            /// </summary>
            public IEnumerable<Inlet> Inlets => _inlets;
            /// <summary>
            /// The default name prefix <c>FanIn</c> used for any newly created ports.
            /// </summary>
            public string Name => "FanIn";
        }

        #endregion

        private ImmutableArray<Inlet> _inlets;
        private readonly IEnumerator<Inlet> _registered;
        private readonly string _name;

        /// <summary>
        /// Initializes the shape's outlet, available inlets, and port-name prefix.
        /// </summary>
        /// <param name="outlet">The outlet exposed by the shape.</param>
        /// <param name="registered">Inlets available for reuse in the order they are registered.</param>
        /// <param name="name">The prefix used to name any new inlets.</param>
        protected FanInShape(Outlet<TOut> outlet, IEnumerable<Inlet> registered, string name)
        {
            Out = outlet;
            Outlets = ImmutableArray.Create<Outlet>(outlet);
            _inlets = ImmutableArray<Inlet>.Empty;
            _name = name;

            _registered = registered.GetEnumerator();
        }

        /// <summary>
        /// Initializes this shape from the outlet, inlets, and name in the supplied data.
        /// </summary>
        /// <param name="init">The port and naming data used to initialize this shape.</param>
        protected FanInShape(IInit init) : this(init.Outlet, init.Inlets, init.Name) { }

        /// <summary>
        /// Constructs the concrete fan-in shape using replacement port initialization data.
        /// </summary>
        /// <param name="init">The port and naming data for the constructed shape.</param>
        /// <returns>A concrete fan-in shape initialized with <paramref name="init"/>.</returns>
        protected abstract FanInShape<TOut> Construct(IInit init);

        /// <summary>
        /// Creates an inlet using the next registered inlet when available, or a new inlet named from this shape's prefix.
        /// </summary>
        /// <typeparam name="T">The element type accepted by the inlet.</typeparam>
        /// <param name="name">The suffix used when creating a new inlet.</param>
        /// <returns>The registered or newly created inlet, added to this shape's inlet list.</returns>
        protected Inlet<T> NewInlet<T>(string name)
        {
            var p = _registered.MoveNext() ? (Inlet<T>)_registered.Current : new Inlet<T>($"{_name}.{name}");
            _inlets = _inlets.Add(p);
            return p;
        }

        /// <summary>
        /// The inlets registered with this shape, in construction order.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets => _inlets;

        /// <summary>
        /// The single outlet exposed by this fan-in shape.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// The typed outlet exposed by this fan-in shape.
        /// </summary>
        public Outlet<TOut> Out { get; }

        /// <summary>
        /// Creates a copy of this shape with carbon-copied ports.
        /// </summary>
        /// <returns>A concrete fan-in shape with copied ports.</returns>
        public override Shape DeepCopy()
            => Construct(new InitPorts((Outlet<TOut>) Out.CarbonCopy(), _inlets.Select(i => i.CarbonCopy())));

        /// <summary>
        /// Creates this shape's concrete type using replacement ports after validating their counts.
        /// </summary>
        /// <param name="inlets">The replacement inlets, which must match this shape's inlet count and types.</param>
        /// <param name="outlets">The replacement outlet, which must match this shape's outlet count and type.</param>
        /// <exception cref="ArgumentException">The replacement inlet or outlet count does not match this shape.</exception>
        /// <exception cref="InvalidCastException">A replacement port is not compatible with this shape's generic port type.</exception>
        /// <returns>A concrete fan-in shape using the replacement ports.</returns>
        public sealed override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (outlets.Length != 1) throw new ArgumentException($"Proposed outlets [{string.Join(", ", outlets)}] do not fit FanInShape");
            if (inlets.Length != Inlets.Length) throw new ArgumentException($"Proposed inlets [{string.Join(", ", inlets)}] do not fit FanInShape");

            return Construct(new InitPorts((Outlet<TOut>)outlets[0], inlets));
        }
    }

    /// <summary>
    /// A fan-in shape with a fixed number of inlets that all accept the same element type and one typed outlet.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by each inlet.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the outlet.</typeparam>
    public class UniformFanInShape<TIn, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// The number of inlets in this shape.
        /// </summary>
        public readonly int N;

        /// <summary>
        /// Creates a shape with <paramref name="n"/> inlets using the supplied initialization data.
        /// </summary>
        /// <param name="n">The number of inlets to create.</param>
        /// <param name="init">The outlet, any inlets to reuse, and the name prefix for new inlets.</param>
        public UniformFanInShape(int n, IInit init) : base(init)
        {
            N = n;
            Ins = Enumerable.Range(0, n).Select(i => NewInlet<TIn>($"in{i}")).ToImmutableList();
        }

        /// <summary>
        /// Creates a shape with <paramref name="n"/> inlets and the default name prefix.
        /// </summary>
        /// <param name="n">The number of inlets to create.</param>
        public UniformFanInShape(int n) : this(n, new InitName("UniformFanIn"))
        {
            
        }

        /// <summary>
        /// Creates a shape with <paramref name="n"/> inlets and the supplied name prefix.
        /// </summary>
        /// <param name="n">The number of inlets to create.</param>
        /// <param name="name">The prefix used to name the outlet and newly created inlets.</param>
        public UniformFanInShape(int n, string name) : this(n, new InitName(name))
        {
            
        }

        /// <summary>
        /// Creates a shape using the supplied outlet and inlet ports.
        /// </summary>
        /// <param name="outlet">The outlet exposed by the shape.</param>
        /// <param name="inlets">The inlet ports used by the shape.</param>
        public UniformFanInShape(Outlet<TOut> outlet, params Inlet<TIn>[] inlets)
            : this(inlets.Length, new InitPorts(outlet, inlets))
        {
            
        }

        /// <summary>
        /// The inlet ports, in their construction order.
        /// </summary>
        public IImmutableList<Inlet<TIn>> Ins { get; }

        /// <summary>
        /// Gets an inlet by its zero-based index.
        /// </summary>
        /// <param name="n">The zero-based inlet index.</param>
        /// <returns>The inlet at index <paramref name="n"/>.</returns>
        public Inlet<TIn> In(int n) => Ins[n];

        /// <summary>
        /// Creates a uniform fan-in shape of this type from replacement port data.
        /// </summary>
        /// <param name="init">The outlet, replacement inlets, and name prefix.</param>
        /// <returns>A uniform fan-in shape with the same inlet count and replacement ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init) => new UniformFanInShape<TIn, TOut>(N, init);
    }
}
