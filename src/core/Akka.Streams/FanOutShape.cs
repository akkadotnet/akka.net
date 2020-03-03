//-----------------------------------------------------------------------
// <copyright file="FanOutShape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    public abstract class FanOutShape<TIn> : Shape
    {
        #region internal classes

        /// <summary>
        /// TBD
        /// </summary>
        public interface IInit
        {
            /// <summary>
            /// TBD
            /// </summary>
            Inlet<TIn> Inlet { get; }
            /// <summary>
            /// TBD
            /// </summary>
            IEnumerable<Outlet> Outlets { get; }
            /// <summary>
            /// TBD
            /// </summary>
            string Name { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class InitName : IInit
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="name">TBD</param>
            /// <exception cref="ArgumentNullException">TBD</exception>
            public InitName(string name)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

                Name = name;
                Inlet = new Inlet<TIn>(name + ".in");
                Outlets = Enumerable.Empty<Outlet>();
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Inlet<TIn> Inlet { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Outlet> Outlets { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public string Name { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class InitPorts : IInit
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="inlet">TBD</param>
            /// <param name="outlets">TBD</param>
            /// <exception cref="ArgumentNullException">TBD</exception>
            public InitPorts(Inlet<TIn> inlet, IEnumerable<Outlet> outlets)
            {
                if (outlets == null) throw new ArgumentNullException(nameof(outlets));
                if (inlet == null) throw new ArgumentNullException(nameof(inlet));

                Inlet = inlet;
                Outlets = outlets;
                Name = "FanOut";
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Inlet<TIn> Inlet { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public IEnumerable<Outlet> Outlets { get; }
            /// <summary>
            /// TBD
            /// </summary>
            public string Name { get; }
        }

        #endregion

        private readonly string _name;
        private ImmutableArray<Outlet> _outlets;
        private readonly IEnumerator<Outlet> _registered;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="registered">TBD</param>
        /// <param name="name">TBD</param>
        protected FanOutShape(Inlet<TIn> inlet, IEnumerable<Outlet> registered, string name)
        {
            In = inlet;
            Inlets = ImmutableArray.Create<Inlet>(inlet);
            _outlets = ImmutableArray<Outlet>.Empty;
            _name = name;
            _registered = registered.GetEnumerator();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        protected FanOutShape(IInit init) : this(init.Inlet, init.Outlets, init.Name) { }

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets => _outlets;

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected abstract FanOutShape<TIn> Construct(IInit init);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        protected Outlet<T> NewOutlet<T>(string name)
        {
            var p = _registered.MoveNext() ? (Outlet<T>)_registered.Current : new Outlet<T>($"{_name}.{name}");
            _outlets = _outlets.Add(p);
            return p;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy()
            => Construct(new InitPorts((Inlet<TIn>) In.CarbonCopy(), _outlets.Select(o => o.CarbonCopy())));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class UniformFanOutShape<TIn, TOut> : FanOutShape<TIn>
    {
        private readonly int _n;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <param name="init">TBD</param>
        public UniformFanOutShape(int n, IInit init) : base(init)
        {
            _n = n;
            Outs = Enumerable.Range(0, n).Select(i => NewOutlet<TOut>($"out{i}")).ToImmutableList();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        public UniformFanOutShape(int n) : this(n, new InitName("UniformFanOut"))
        {
            
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <param name="name">TBD</param>
        public UniformFanOutShape(int n, string name) : this(n, new InitName(name))
        {
            
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="outlets">TBD</param>
        public UniformFanOutShape(Inlet<TIn> inlet, params Outlet<TOut>[] outlets)
            : this(outlets.Length, new InitPorts(inlet, outlets))
        {
            
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableList<Outlet<TOut>> Outs { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public Outlet<TOut> Out(int n) => Outs[n];

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init) => new UniformFanOutShape<TIn, TOut>(_n, init);
    }
}
