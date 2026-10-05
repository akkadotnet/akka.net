//-----------------------------------------------------------------------
// <copyright file="FanOutShape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;

namespace Akka.Streams
{
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output outlet <c>Out3</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// Output outlet <c>Out3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Outlet<T3> Out3;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="out3">The output port carrying <typeparamref name="T3"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output outlet <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output outlet <c>Out4</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// Output outlet <c>Out3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// Output outlet <c>Out4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Outlet<T4> Out4;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="out3">The output port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="out4">The output port carrying <typeparamref name="T4"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output outlet <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output outlet <c>Out4</c>.</typeparam>
    /// <typeparam name="T5">The element type emitted by output outlet <c>Out5</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// Output outlet <c>Out3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// Output outlet <c>Out4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// Output outlet <c>Out5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Outlet<T5> Out5;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
            Out5 = NewOutlet<T5>("out5");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="out3">The output port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="out4">The output port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="out5">The output port carrying <typeparamref name="T5"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output outlet <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output outlet <c>Out4</c>.</typeparam>
    /// <typeparam name="T5">The element type emitted by output outlet <c>Out5</c>.</typeparam>
    /// <typeparam name="T6">The element type emitted by output outlet <c>Out6</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// Output outlet <c>Out3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// Output outlet <c>Out4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// Output outlet <c>Out5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Outlet<T5> Out5;
        /// <summary>
        /// Output outlet <c>Out6</c> with element type <typeparamref name="T6"/>.
        /// </summary>
        public readonly Outlet<T6> Out6;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
            Out5 = NewOutlet<T5>("out5");
            Out6 = NewOutlet<T6>("out6");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="out3">The output port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="out4">The output port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="out5">The output port carrying <typeparamref name="T5"/> elements.</param>
        /// <param name="out6">The output port carrying <typeparamref name="T6"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output outlet <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output outlet <c>Out4</c>.</typeparam>
    /// <typeparam name="T5">The element type emitted by output outlet <c>Out5</c>.</typeparam>
    /// <typeparam name="T6">The element type emitted by output outlet <c>Out6</c>.</typeparam>
    /// <typeparam name="T7">The element type emitted by output outlet <c>Out7</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// Output outlet <c>Out3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// Output outlet <c>Out4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// Output outlet <c>Out5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Outlet<T5> Out5;
        /// <summary>
        /// Output outlet <c>Out6</c> with element type <typeparamref name="T6"/>.
        /// </summary>
        public readonly Outlet<T6> Out6;
        /// <summary>
        /// Output outlet <c>Out7</c> with element type <typeparamref name="T7"/>.
        /// </summary>
        public readonly Outlet<T7> Out7;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
            Out5 = NewOutlet<T5>("out5");
            Out6 = NewOutlet<T6>("out6");
            Out7 = NewOutlet<T7>("out7");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="out3">The output port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="out4">The output port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="out5">The output port carrying <typeparamref name="T5"/> elements.</param>
        /// <param name="out6">The output port carrying <typeparamref name="T6"/> elements.</param>
        /// <param name="out7">The output port carrying <typeparamref name="T7"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6, Outlet<T7> out7) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6, out7 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7>(init);
        }
    }
    /// <summary>
    /// A typed fan-out shape with one input and a fixed set of typed outputs.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port.</typeparam>
    /// <typeparam name="T0">The element type emitted by output outlet <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output outlet <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output outlet <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output outlet <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output outlet <c>Out4</c>.</typeparam>
    /// <typeparam name="T5">The element type emitted by output outlet <c>Out5</c>.</typeparam>
    /// <typeparam name="T6">The element type emitted by output outlet <c>Out6</c>.</typeparam>
    /// <typeparam name="T7">The element type emitted by output outlet <c>Out7</c>.</typeparam>
    /// <typeparam name="T8">The element type emitted by output outlet <c>Out8</c>.</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7, T8> : FanOutShape<TIn>
    {
        /// <summary>
        /// Output outlet <c>Out0</c> with element type <typeparamref name="T0"/>.
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// Output outlet <c>Out1</c> with element type <typeparamref name="T1"/>.
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// Output outlet <c>Out2</c> with element type <typeparamref name="T2"/>.
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// Output outlet <c>Out3</c> with element type <typeparamref name="T3"/>.
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// Output outlet <c>Out4</c> with element type <typeparamref name="T4"/>.
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// Output outlet <c>Out5</c> with element type <typeparamref name="T5"/>.
        /// </summary>
        public readonly Outlet<T5> Out5;
        /// <summary>
        /// Output outlet <c>Out6</c> with element type <typeparamref name="T6"/>.
        /// </summary>
        public readonly Outlet<T6> Out6;
        /// <summary>
        /// Output outlet <c>Out7</c> with element type <typeparamref name="T7"/>.
        /// </summary>
        public readonly Outlet<T7> Out7;
        /// <summary>
        /// Output outlet <c>Out8</c> with element type <typeparamref name="T8"/>.
        /// </summary>
        public readonly Outlet<T8> Out8;

        /// <summary>
        /// Creates the shape with ports supplied by <paramref name="init"/>.
        /// </summary>
        /// <param name="init">The input and any pre-registered outlets used to initialize the shape.</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
            Out5 = NewOutlet<T5>("out5");
            Out6 = NewOutlet<T6>("out6");
            Out7 = NewOutlet<T7>("out7");
            Out8 = NewOutlet<T8>("out8");
        }

        /// <summary>
        /// Creates the shape with a generated input, typed output outlets, and the specified port-name prefix.
        /// </summary>
        /// <param name="name">The name used as the prefix for ports created by this shape.</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// Creates the shape from an input port and its typed output ports.
        /// </summary>
        /// <param name="inlet">The input port carrying <typeparamref name="TIn"/> elements.</param>
        /// <param name="out0">The output port carrying <typeparamref name="T0"/> elements.</param>
        /// <param name="out1">The output port carrying <typeparamref name="T1"/> elements.</param>
        /// <param name="out2">The output port carrying <typeparamref name="T2"/> elements.</param>
        /// <param name="out3">The output port carrying <typeparamref name="T3"/> elements.</param>
        /// <param name="out4">The output port carrying <typeparamref name="T4"/> elements.</param>
        /// <param name="out5">The output port carrying <typeparamref name="T5"/> elements.</param>
        /// <param name="out6">The output port carrying <typeparamref name="T6"/> elements.</param>
        /// <param name="out7">The output port carrying <typeparamref name="T7"/> elements.</param>
        /// <param name="out8">The output port carrying <typeparamref name="T8"/> elements.</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6, Outlet<T7> out7, Outlet<T8> out8) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6, out7, out8 })) { }

        /// <summary>
        /// Creates another shape of this type using the supplied initialized ports.
        /// </summary>
        /// <param name="init">The input and outlets to use for the new shape.</param>
        /// <returns>A fan-out shape with the same number and types of ports.</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7, T8>(init);
        }
    }
}
