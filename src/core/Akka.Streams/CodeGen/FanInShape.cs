//-----------------------------------------------------------------------
// <copyright file="FanInShape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Immutable;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShapeN<T0, T1, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int N;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ImmutableArray<Inlet<T1>> In1s;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <param name="init">TBD</param>
        public FanInShapeN(int n, IInit init) : base(init)
        {
            N = n;
            In0 = NewInlet<T0>("in0");
            var builder = ImmutableArray.CreateBuilder<Inlet<T1>>(n);
            for (int i = 0; i < n; i++) builder.Add(new Inlet<T1>("in" + i));
            In1s = builder.ToImmutable();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        public FanInShapeN(int n) : this(n, new InitName("FanInShape1N")) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <param name="name">TBD</param>
        public FanInShapeN(int n, string name) : this(n, new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="inlets">TBD</param>
        public FanInShapeN(Outlet<TOut> outlet, Inlet<T0> in0, params Inlet<T1>[] inlets) : this(inlets.Length, new InitPorts(outlet, new Inlet[]{in0}.Concat(inlets))) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="n" /> is less than or equal to zero.
        /// </exception>
        /// <returns>TBD</returns>
        public Inlet<T1> In(int n)
        {
            if (n <= 0) throw new ArgumentException("n must be > 0", nameof(n));
            return In1s[n-1];
        }
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShapeN<T0, T1, TOut>(N, init);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, T3, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T3> In3;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="in3">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T4> In4;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
            In4 = NewInlet<T4>("in4");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="in3">TBD</param>
        /// <param name="in4">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T5> In5;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
            In4 = NewInlet<T4>("in4");
            In5 = NewInlet<T5>("in5");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="in3">TBD</param>
        /// <param name="in4">TBD</param>
        /// <param name="in5">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, T6, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T5> In5;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T6> In6;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
            In4 = NewInlet<T4>("in4");
            In5 = NewInlet<T5>("in5");
            In6 = NewInlet<T6>("in6");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="in3">TBD</param>
        /// <param name="in4">TBD</param>
        /// <param name="in5">TBD</param>
        /// <param name="in6">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T5> In5;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T6> In6;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T7> In7;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
            In4 = NewInlet<T4>("in4");
            In5 = NewInlet<T5>("in5");
            In6 = NewInlet<T6>("in6");
            In7 = NewInlet<T7>("in7");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="in3">TBD</param>
        /// <param name="in4">TBD</param>
        /// <param name="in5">TBD</param>
        /// <param name="in6">TBD</param>
        /// <param name="in7">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6, Inlet<T7> in7) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6, in7 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, TOut>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, T8, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T5> In5;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T6> In6;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T7> In7;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T8> In8;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
            In4 = NewInlet<T4>("in4");
            In5 = NewInlet<T5>("in5");
            In6 = NewInlet<T6>("in6");
            In7 = NewInlet<T7>("in7");
            In8 = NewInlet<T8>("in8");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="in0">TBD</param>
        /// <param name="in1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="in3">TBD</param>
        /// <param name="in4">TBD</param>
        /// <param name="in5">TBD</param>
        /// <param name="in6">TBD</param>
        /// <param name="in7">TBD</param>
        /// <param name="in8">TBD</param>
        /// <returns>TBD</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6, Inlet<T7> in7, Inlet<T8> in8) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6, in7, in8 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, T8, TOut>(init);
        }
    }
}
