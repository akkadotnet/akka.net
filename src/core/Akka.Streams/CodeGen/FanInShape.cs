//-----------------------------------------------------------------------
// <copyright file="FanInShape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Immutable;

namespace Akka.Streams
{
    /// <summary>
    /// A typed fan-in shape with one fixed input, a variable number of additional inputs, and one output.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by the fixed <see cref="In0"/> inlet.</typeparam>
    /// <typeparam name="T1">The element type accepted by each inlet in <see cref="In1s"/>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShapeN<T0, T1, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// The number of additional <typeparamref name="T1"/> inlet objects in <see cref="In1s"/>.
        /// </summary>
        public readonly int N;
        /// <summary>
        /// The fixed input inlet with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// The additional input inlets with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly ImmutableArray<Inlet<T1>> In1s;
        /// <summary>
        /// Creates the shape with <paramref name="n"/> additional inlet objects. The outlet and, when supplied, the first inlet from <paramref name="init"/> are used; remaining inlets are ignored and the additional inlet objects are not registered in <see cref="FanInShape{TOut}.Inlets"/>.
        /// </summary>
        /// <param name="n">The number of additional <typeparamref name="T1"/> inlets.</param>
        /// <param name="init">The outlet and optional inlet sequence. Its first inlet initializes <see cref="In0"/> when present; further inlets are ignored.</param>
        public FanInShapeN(int n, IInit init) : base(init)
        {
            N = n;
            In0 = NewInlet<T0>("in0");
            var builder = ImmutableArray.CreateBuilder<Inlet<T1>>(n);
            for (int i = 0; i < n; i++) builder.Add(new Inlet<T1>("in" + i));
            In1s = builder.ToImmutable();
        }

        /// <summary>
        /// Creates the shape with <paramref name="n"/> additional inlets and a generated default name.
        /// </summary>
        /// <param name="n">The number of additional <typeparamref name="T1"/> inlets.</param>
        public FanInShapeN(int n) : this(n, new InitName("FanInShape1N")) { }
        /// <summary>
        /// Creates the shape with <paramref name="n"/> additional inlet objects and the specified name for the outlet and fixed inlet.
        /// </summary>
        /// <param name="n">The number of additional <typeparamref name="T1"/> inlets.</param>
        /// <param name="name">The name used for the outlet and as a prefix for the fixed inlet; the additional inlet objects are named without this prefix.</param>
        public FanInShapeN(int n, string name) : this(n, new InitName(name)) { }
        /// <summary>
        /// Creates the shape using the supplied output, fixed input, and repeated inputs.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The fixed input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="inlets">The additional input ports carrying <typeparamref name="T1"/> elements.</param>
        public FanInShapeN(Outlet<TOut> outlet, Inlet<T0> in0, params Inlet<T1>[] inlets) : this(inlets.Length, new InitPorts(outlet, new Inlet[]{in0}.Concat(inlets))) { }

        /// <summary>
        /// Gets an additional inlet by its one-based position in <see cref="In1s"/>.
        /// </summary>
        /// <param name="n">The one-based position of the requested additional inlet.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="n" /> is less than or equal to zero.
        /// </exception>
        /// <returns>The inlet at position <paramref name="n"/> in <see cref="In1s"/>.</returns>
        public Inlet<T1> In(int n)
        {
            if (n <= 0) throw new ArgumentException("n must be > 0", nameof(n));
            return In1s[n-1];
        }
        
        /// <summary>
        /// Creates another shape of this type using the supplied outlet and fixed inlet. Additional inlet objects are recreated, and any remaining supplied inlets are ignored.
        /// </summary>
        /// <param name="init">The outlet and inlet sequence whose first inlet initializes the fixed input.</param>
        /// <returns>A shape with the same outlet and fixed input plus newly created additional inlet objects, which are not registered in the shape's inlet collection.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShapeN<T0, T1, TOut>(N, init);
        }
    }

    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
        }

        /// <summary>
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
        }

        /// <summary>
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="T3">The element type accepted by input inlet <c>In3</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, T3, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// Input inlet <c>In3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Inlet<T3> In3;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
        }

        /// <summary>
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="in3">The input port carrying <typeparamref name="T3"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="T3">The element type accepted by input inlet <c>In3</c>.</typeparam>
    /// <typeparam name="T4">The element type accepted by input inlet <c>In4</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// Input inlet <c>In3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// Input inlet <c>In4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Inlet<T4> In4;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
        public FanInShape(IInit init) : base(init)
        {
            In0 = NewInlet<T0>("in0");
            In1 = NewInlet<T1>("in1");
            In2 = NewInlet<T2>("in2");
            In3 = NewInlet<T3>("in3");
            In4 = NewInlet<T4>("in4");
        }

        /// <summary>
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="in3">The input port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="in4">The input port carrying <typeparamref name="T4"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="T3">The element type accepted by input inlet <c>In3</c>.</typeparam>
    /// <typeparam name="T4">The element type accepted by input inlet <c>In4</c>.</typeparam>
    /// <typeparam name="T5">The element type accepted by input inlet <c>In5</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// Input inlet <c>In3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// Input inlet <c>In4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// Input inlet <c>In5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Inlet<T5> In5;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
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
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="in3">The input port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="in4">The input port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="in5">The input port carrying <typeparamref name="T5"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="T3">The element type accepted by input inlet <c>In3</c>.</typeparam>
    /// <typeparam name="T4">The element type accepted by input inlet <c>In4</c>.</typeparam>
    /// <typeparam name="T5">The element type accepted by input inlet <c>In5</c>.</typeparam>
    /// <typeparam name="T6">The element type accepted by input inlet <c>In6</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, T6, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// Input inlet <c>In3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// Input inlet <c>In4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// Input inlet <c>In5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Inlet<T5> In5;
        /// <summary>
        /// Input inlet <c>In6</c> with element type <typeparamref name="T6"/>.
        /// </summary>
        public readonly Inlet<T6> In6;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
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
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="in3">The input port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="in4">The input port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="in5">The input port carrying <typeparamref name="T5"/> elements.</param>
        /// <param name="in6">The input port carrying <typeparamref name="T6"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="T3">The element type accepted by input inlet <c>In3</c>.</typeparam>
    /// <typeparam name="T4">The element type accepted by input inlet <c>In4</c>.</typeparam>
    /// <typeparam name="T5">The element type accepted by input inlet <c>In5</c>.</typeparam>
    /// <typeparam name="T6">The element type accepted by input inlet <c>In6</c>.</typeparam>
    /// <typeparam name="T7">The element type accepted by input inlet <c>In7</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// Input inlet <c>In3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// Input inlet <c>In4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// Input inlet <c>In5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Inlet<T5> In5;
        /// <summary>
        /// Input inlet <c>In6</c> with element type <typeparamref name="T6"/>.
        /// </summary>
        public readonly Inlet<T6> In6;
        /// <summary>
        /// Input inlet <c>In7</c> with element type <typeparamref name="T7"/>.
        /// </summary>
        public readonly Inlet<T7> In7;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
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
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="in3">The input port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="in4">The input port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="in5">The input port carrying <typeparamref name="T5"/> elements.</param>
        /// <param name="in6">The input port carrying <typeparamref name="T6"/> elements.</param>
        /// <param name="in7">The input port carrying <typeparamref name="T7"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6, Inlet<T7> in7) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6, in7 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, TOut>(init);
        }
    }
    /// <summary>
    /// A typed fan-in shape with one output and a fixed set of typed inputs.
    /// </summary>
    /// <typeparam name="T0">The element type accepted by input inlet <c>In0</c>.</typeparam>
    /// <typeparam name="T1">The element type accepted by input inlet <c>In1</c>.</typeparam>
    /// <typeparam name="T2">The element type accepted by input inlet <c>In2</c>.</typeparam>
    /// <typeparam name="T3">The element type accepted by input inlet <c>In3</c>.</typeparam>
    /// <typeparam name="T4">The element type accepted by input inlet <c>In4</c>.</typeparam>
    /// <typeparam name="T5">The element type accepted by input inlet <c>In5</c>.</typeparam>
    /// <typeparam name="T6">The element type accepted by input inlet <c>In6</c>.</typeparam>
    /// <typeparam name="T7">The element type accepted by input inlet <c>In7</c>.</typeparam>
    /// <typeparam name="T8">The element type accepted by input inlet <c>In8</c>.</typeparam>
    /// <typeparam name="TOut">The element type emitted by the output port.</typeparam>
    public class FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, T8, TOut> : FanInShape<TOut>
    {
        /// <summary>
        /// Input inlet <c>In0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Inlet<T0> In0;
        /// <summary>
        /// Input inlet <c>In1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Inlet<T1> In1;
        /// <summary>
        /// Input inlet <c>In2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Inlet<T2> In2;
        /// <summary>
        /// Input inlet <c>In3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Inlet<T3> In3;
        /// <summary>
        /// Input inlet <c>In4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Inlet<T4> In4;
        /// <summary>
        /// Input inlet <c>In5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Inlet<T5> In5;
        /// <summary>
        /// Input inlet <c>In6</c> with element type <typeparamref name="T6"/>.
        /// </summary>
        public readonly Inlet<T6> In6;
        /// <summary>
        /// Input inlet <c>In7</c> with element type <typeparamref name="T7"/>.
        /// </summary>
        public readonly Inlet<T7> In7;
        /// <summary>
        /// Input inlet <c>In8</c> with element type <typeparamref name="T8"/>.
        /// </summary>
        public readonly Inlet<T8> In8;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The outlet and any pre-registered inlets used to initialize the shape.</param>
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
        /// Creates the shape with a generated set of typed inlets and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanInShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an output port and its typed input ports.
        /// </summary>
        /// <param name="outlet">The output port carrying <typeparamref name="TOut"/> elements.</param>
        /// <param name="in0">The input port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="in1">The input port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="in2">The input port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="in3">The input port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="in4">The input port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="in5">The input port carrying <typeparamref name="T5"/> elements.</param>
        /// <param name="in6">The input port carrying <typeparamref name="T6"/> elements.</param>
        /// <param name="in7">The input port carrying <typeparamref name="T7"/> elements.</param>
        /// <param name="in8">The input port carrying <typeparamref name="T8"/> elements.</param>
        /// <returns>A shape that exposes the supplied ports.</returns>
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6, Inlet<T7> in7, Inlet<T8> in8) 
            : this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6, in7, in8 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The outlet and inlets to use for the new shape.</param>
        /// <returns>A fan-in shape with the same number and types of ports.</returns>
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, T8, TOut>(init);
        }
    }
}
