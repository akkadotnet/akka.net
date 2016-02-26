using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Streams
{
    public abstract class FanInShape<TOut> : Shape
    {
        #region internal classes

        public interface IInit
        {
            Outlet<TOut> Outlet { get; }
            IEnumerable<Inlet> Inlets { get; }
            string Name { get; }
        }

        [Serializable]
        public sealed class InitName : IInit
        {
            private readonly string _name;
            private readonly Outlet<TOut> _outlet;

            public InitName(string name)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentNullException("name");

                _name = name;
                _outlet = new Outlet<TOut>(name + ".out");
            }

            public Outlet<TOut> Outlet { get { return _outlet; } }
            public IEnumerable<Inlet> Inlets { get { return Enumerable.Empty<Inlet>(); } }
            public string Name { get { return _name; } }
        }

        [Serializable]
        public sealed class InitPorts : IInit
        {
            private readonly Outlet<TOut> _outlet;
            private readonly IEnumerable<Inlet> _inlets;

            public InitPorts(Outlet<TOut> outlet, IEnumerable<Inlet> inlets)
            {
                if (outlet == null) throw new ArgumentNullException("outlet");
                if (inlets == null) throw new ArgumentNullException("inlets");

                _outlet = outlet;
                _inlets = inlets;
            }

            public Outlet<TOut> Outlet { get { return _outlet; } }
            public IEnumerable<Inlet> Inlets { get { return _inlets; } }
            public string Name { get { return "FanIn"; } }
        }

        #endregion

        private ImmutableArray<Inlet> _inlets = new ImmutableArray<Inlet>();
        private readonly IEnumerator<Inlet> _registered;
        private readonly string _name;

        protected FanInShape(Outlet<TOut> outlet, IEnumerable<Inlet> registered, string name)
        {
            Out = outlet;
            Outlets = ImmutableArray.Create<Outlet>(outlet);
            _name = name;

            _registered = registered.GetEnumerator();
        }

        protected FanInShape(IInit init) : this(init.Outlet, init.Inlets, init.Name) { }

        protected abstract FanInShape<TOut> Construct(IInit init);

        protected Inlet<T> NewInlet<T>(string name)
        {
            var p = _registered.MoveNext() ? Inlet.Create<T>(_registered.Current) : new Inlet<T>(_name + "." + name);
            _inlets = _inlets.Add(p);
            return p;
        }

        public override ImmutableArray<Inlet> Inlets => _inlets;
        public override ImmutableArray<Outlet> Outlets { get; }
        public Outlet<TOut> Out { get; }
        public override Shape DeepCopy()
        {
            return Construct(new InitPorts(Out, _inlets.Select(Inlet.Create<TOut>)));
        }

        public sealed override Shape CopyFromPorts(ImmutableArray<Inlet> i, ImmutableArray<Outlet> o)
        {
            if (o.Length != 1) throw new ArgumentException(string.Format("Proposed outlets [{0}] do not fit FanInShape", string.Join(", ", o)));
            if (i.Length != Inlets.Length) throw new ArgumentException(string.Format("Proposed inlets [{0}] do not fit FanInShape", string.Join(", ", i)));

            return Construct(new InitPorts(Outlet.Create<TOut>(o[0]), i));
        }
    }

    public class UniformFanInShape<TIn, TOut> : FanInShape<TOut>
    {
        public readonly int N;
        private readonly Inlet<TIn>[] _inSeq;

        public UniformFanInShape(int n, IInit init) : base(init)
        {
            N = n;
            _inSeq = Enumerable.Range(0, n).Select(i => new Inlet<TIn>("in" + i)).ToArray();
        }

        public UniformFanInShape(int n) : this(n, new InitName("UniformFanIn")) { }
        public UniformFanInShape(int n, string name) : this(n, new InitName(name)) { }
        public UniformFanInShape(Outlet<TOut> outlet, params Inlet<TIn>[] inlets) : this(inlets.Length, new InitPorts(outlet, inlets)) { }

        public Inlet<TIn> In(int n)
        {
            return _inSeq[n];
        }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new UniformFanInShape<TIn, TOut>(N, init);
        }
    }
}