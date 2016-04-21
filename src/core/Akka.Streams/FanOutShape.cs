//-----------------------------------------------------------------------
// <copyright file="FanOutShape.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Streams
{
    public abstract class FanOutShape<TIn> : Shape
    {
        #region internal classes

        public interface IInit
        {
            Inlet<TIn> Inlet { get; }
            IEnumerable<Outlet> Outlets { get; }
            string Name { get; }
        }

        [Serializable]
        public sealed class InitName : IInit
        {
            public InitName(string name)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException("name");

                Name = name;
                Inlet = new Inlet<TIn>(name + ".in");
                Outlets = Enumerable.Empty<Outlet>();
            }

            public Inlet<TIn> Inlet { get; }
            public IEnumerable<Outlet> Outlets { get; }
            public string Name { get; }
        }

        [Serializable]
        public sealed class InitPorts : IInit
        {
            public InitPorts(Inlet<TIn> inlet, IEnumerable<Outlet> outlets)
            {
                if (outlets == null) throw new ArgumentNullException("outlets");
                if (inlet == null) throw new ArgumentNullException("inlet");

                Inlet = inlet;
                Outlets = outlets;
                Name = "FanOut";
            }

            public Inlet<TIn> Inlet { get; }
            public IEnumerable<Outlet> Outlets { get; }
            public string Name { get; }
        }

        #endregion

        private readonly string _name;
        private ImmutableArray<Outlet> _outlets;
        private readonly IEnumerator<Outlet> _registered;

        protected FanOutShape(Inlet<TIn> inlet, IEnumerable<Outlet> registered, string name)
        {
            In = inlet;
            Inlets = ImmutableArray.Create<Inlet>(inlet);
            _outlets = ImmutableArray<Outlet>.Empty;
            _name = name;
            _registered = registered.GetEnumerator();
        }

        protected FanOutShape(IInit init) : this(init.Inlet, init.Outlets, init.Name) { }

        public Inlet<TIn> In { get; }
        public override ImmutableArray<Outlet> Outlets => _outlets;
        public override ImmutableArray<Inlet> Inlets { get; }

        protected abstract FanOutShape<TIn> Construct(IInit init);

        protected Outlet<T> NewOutlet<T>(string name)
        {
            var p = _registered.MoveNext() ? (Outlet<T>)_registered.Current : new Outlet<T>($"{_name}.{name}");
            _outlets = _outlets.Add(p);
            return p;
        }
        public override Shape DeepCopy()
        {
            return Construct(new InitPorts((Inlet<TIn>)In.CarbonCopy(), _outlets.Select(o => o.CarbonCopy())));
        }

        public sealed override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 1) throw new ArgumentException(
                $"Proposed inlets [{string.Join(", ", inlets)}] do not fit FanOutShape");
            if (outlets.Length != _outlets.Length) throw new ArgumentException(string.Format("Proposed outlets [{0}] do not fit FanOutShape", string.Join(", ", outlets)));

            return Construct(new InitPorts((Inlet<TIn>)inlets[0], outlets));
        }
    }

    public class UniformFanOutShape<TIn, TOut> : FanOutShape<TIn>
    {
        private readonly int _n;
        private readonly Outlet<TOut>[] _out;

        public UniformFanOutShape(int n, IInit init) : base(init)
        {
            _n = n;
            Outs = Enumerable.Range(0, n).Select(i => NewOutlet<TOut>($"out{i}")).ToImmutableList();
        }

        public UniformFanOutShape(int n) : this(n, new InitName("UniformFanOut")) { }
        public UniformFanOutShape(int n, string name) : this(n, new InitName(name)) { }
        public UniformFanOutShape(Inlet<TIn> inlet, params Outlet<TOut>[] outlets) : this(outlets.Length, new InitPorts(inlet, outlets)) { }


        public IImmutableList<Outlet<TOut>> Outs { get; }

        public Outlet<TOut> Out(int n) => Outs[n];

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new UniformFanOutShape<TIn, TOut>(_n, init);
        }
    }
}