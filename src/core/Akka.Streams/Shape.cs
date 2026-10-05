//-----------------------------------------------------------------------
// <copyright file="Shape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Util;

namespace Akka.Streams
{
    /// <summary>
    /// An input port of a <see cref="IModule"/>. This type logically belongs
    /// into the impl package but must live here due to how sealed works.
    /// It is also used in the Java DSL for "untyped Inlets" as a work-around
    /// for otherwise unreasonable existential types.
    /// </summary>
    public abstract class InPort
    {
        /// <summary>
        /// Module-local identifier used to index this input port.
        /// </summary>
        internal int Id = -1;
    }

    /// <summary>
    /// An output port of a StreamLayout.Module. This type logically belongs
    /// into the impl package but must live here due to how sealed works.
    /// It is also used in the Java DSL for "untyped Outlets" as a work-around
    /// for otherwise unreasonable existential types.
    /// </summary>
    public abstract class OutPort 
    {
        /// <summary>
        /// Module-local identifier used to index this output port.
        /// </summary>
        internal int Id = -1;
    }

    /// <summary>
    /// An Inlet is a typed input to a Shape. Its partner in the Module view 
    /// is the InPort(which does not bear an element type because Modules only 
    /// express the internal structural hierarchy of stream topologies).
    /// </summary>
    public abstract class Inlet : InPort
    {
        /// <summary>
        /// Creates a typed inlet from an inlet. An inlet already of the requested type is returned; otherwise, a new inlet with the same name is created.
        /// </summary>
        /// <typeparam name="T">Element type of the resulting inlet.</typeparam>
        /// <param name="inlet">Inlet to convert.</param>
        /// <returns>An inlet with the requested element type and the same name.</returns>
        public static Inlet<T> Create<T>(Inlet inlet) => inlet as Inlet<T> ?? new Inlet<T>(inlet.Name);

        /// <summary>
        /// Creates an inlet with the specified name.
        /// </summary>
        /// <param name="name">Name assigned to the inlet.</param>
        /// <exception cref="ArgumentException">The name is <see langword="null"/>.</exception>
        protected Inlet(string name) => Name = name ?? throw new ArgumentException("Inlet name must be defined");

        /// <summary>
        /// Name assigned to this inlet.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Creates a carbon copy of this inlet.
        /// </summary>
        /// <returns>A new inlet of the same type and with the same name.</returns>
        public abstract Inlet CarbonCopy();

        /// <summary>
        /// INTERNAL API
        /// <para>
        /// Builds the <see cref="ActorGraphInterpreter.BoundarySubscriber{T}"/> that feeds this port from
        /// another island. <see cref="Inlet{T}"/> builds it for its own element type; this base version
        /// only serves ports implemented outside Akka.Streams (#8731).
        /// </para>
        /// </summary>
        internal virtual IUntypedSubscriber CreateBoundarySubscriber(IActorRef parent, GraphInterpreterShell shell, int id)
        {
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw RuntimeGenerics.NotSupported(this);

            var elementType = RuntimeGenerics.FirstGenericArgument(this);
            var subscriber = RuntimeGenerics.Instantiate(typeof(ActorGraphInterpreter.BoundarySubscriber<>), elementType, parent, shell, id);
            return (IUntypedSubscriber)RuntimeGenerics.Instantiate(typeof(UntypedSubscriberImpl<>), elementType, subscriber);
        }

        public sealed override string ToString() => Name;
    }

    /// <summary>
    /// A typed input port that accepts elements of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Element type accepted by the inlet.</typeparam>
    public sealed class Inlet<T> : Inlet
    {
        /// <summary>
        /// Creates a typed inlet with the specified name.
        /// </summary>
        /// <param name="name">Name assigned to the inlet.</param>
        public Inlet(string name) : base(name) { }

        /// <summary>
        /// Creates an inlet view with a different element type and the same name.
        /// </summary>
        /// <typeparam name="TOther">Element type of the resulting inlet.</typeparam>
        /// <returns>An inlet with element type <typeparamref name="TOther"/> and this inlet’s name.</returns>
        internal Inlet<TOther> As<TOther>() => Create<TOther>(this);

        /// <summary>
        /// Creates a copy of this inlet.
        /// </summary>
        /// <returns>A new <see cref="Inlet{T}"/> with the same name.</returns>
        public override Inlet CarbonCopy() => new Inlet<T>(Name);

        internal override IUntypedSubscriber CreateBoundarySubscriber(IActorRef parent, GraphInterpreterShell shell, int id)
            => UntypedSubscriber.FromTyped(new ActorGraphInterpreter.BoundarySubscriber<T>(parent, shell, id));
    }

    /// <summary>
    /// An Outlet is a typed output to a Shape. Its partner in the Module view
    /// is the OutPort(which does not bear an element type because Modules only
    /// express the internal structural hierarchy of stream topologies).
    /// </summary>
    public abstract class Outlet : OutPort
    {
        /// <summary>
        /// Creates a typed outlet from an outlet. An outlet already of the requested type is returned; otherwise, a new outlet with the same name is created.
        /// </summary>
        /// <typeparam name="T">Element type of the resulting outlet.</typeparam>
        /// <param name="outlet">Outlet to convert.</param>
        /// <returns>An outlet with the requested element type and the same name.</returns>
        public static Outlet<T> Create<T>(Outlet outlet) => outlet as Outlet<T> ?? new Outlet<T>(outlet.Name);

        /// <summary>
        /// Creates an outlet with the specified name.
        /// </summary>
        /// <param name="name">Name assigned to the outlet.</param>
        protected Outlet(string name) => Name = name;

        /// <summary>
        /// Name assigned to this outlet.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Creates a carbon copy of this outlet.
        /// </summary>
        /// <returns>A new outlet of the same type and with the same name.</returns>
        public abstract Outlet CarbonCopy();

        // INTERNAL API. The three factories below build the types that need this port's element type.
        // Outlet<T> builds them directly; these base versions only serve ports implemented outside
        // Akka.Streams (#8731).

        /// <summary>
        /// INTERNAL API: builds the <see cref="ActorGraphInterpreter.BoundaryPublisher{T}"/> that exposes
        /// this port to another island, and the wrapper the materializer wires it up through.
        /// </summary>
        internal virtual IUntypedPublisher CreateBoundaryPublisher(IActorRef parent, GraphInterpreterShell shell, int id, out IActorPublisher actorPublisher)
        {
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw RuntimeGenerics.NotSupported(this);

            var elementType = RuntimeGenerics.FirstGenericArgument(this);
            var publisher = RuntimeGenerics.Instantiate(typeof(ActorGraphInterpreter.BoundaryPublisher<>), elementType, parent, shell, id);
            actorPublisher = (IActorPublisher)publisher;
            return (IUntypedPublisher)RuntimeGenerics.Instantiate(typeof(UntypedPublisherImpl<>), elementType, publisher);
        }

        /// <summary>
        /// INTERNAL API: builds the <see cref="ActorGraphInterpreter.ActorOutputBoundary{T}"/> that drains
        /// this port out of its island.
        /// </summary>
        internal virtual ActorGraphInterpreter.IActorOutputBoundary CreateActorOutputBoundary(IActorRef actor, GraphInterpreterShell shell, int id)
        {
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw RuntimeGenerics.NotSupported(this);

            return (ActorGraphInterpreter.IActorOutputBoundary)RuntimeGenerics.Instantiate(
                typeof(ActorGraphInterpreter.ActorOutputBoundary<>), RuntimeGenerics.FirstGenericArgument(this), actor, shell, id);
        }

        /// <summary>
        /// INTERNAL API: builds a <see cref="MaterializedValueSource{T}"/> on this port for
        /// <paramref name="computation"/>.
        /// </summary>
        internal virtual IMaterializedValueSource CreateMaterializedValueSource(StreamLayout.IMaterializedValueNode computation)
        {
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw RuntimeGenerics.NotSupported(this);

            return (IMaterializedValueSource)RuntimeGenerics.Instantiate(
                typeof(MaterializedValueSource<>), RuntimeGenerics.FirstGenericArgument(this), computation, this);
        }

        public sealed override string ToString() => Name;
    }

    /// <summary>
    /// A typed output port that emits elements of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Element type emitted by the outlet.</typeparam>
    public sealed class Outlet<T> : Outlet
    {
        /// <summary>
        /// Creates a typed outlet with the specified name.
        /// </summary>
        /// <param name="name">Name assigned to the outlet.</param>
        public Outlet(string name) : base(name) { }

        /// <summary>
        /// Creates an outlet view with a different element type and the same name.
        /// </summary>
        /// <typeparam name="TOther">Element type of the resulting outlet.</typeparam>
        /// <returns>An outlet with element type <typeparamref name="TOther"/> and this outlet’s name.</returns>
        internal Outlet<TOther> As<TOther>() => Create<TOther>(this);

        /// <summary>
        /// Creates a copy of this outlet.
        /// </summary>
        /// <returns>A new <see cref="Outlet{T}"/> with the same name.</returns>
        public override Outlet CarbonCopy() => new Outlet<T>(Name);

        internal override IUntypedPublisher CreateBoundaryPublisher(IActorRef parent, GraphInterpreterShell shell, int id, out IActorPublisher actorPublisher)
        {
            var publisher = new ActorGraphInterpreter.BoundaryPublisher<T>(parent, shell, id);
            actorPublisher = publisher;
            return UntypedPublisher.FromTyped(publisher);
        }

        internal override ActorGraphInterpreter.IActorOutputBoundary CreateActorOutputBoundary(IActorRef actor, GraphInterpreterShell shell, int id)
            => new ActorGraphInterpreter.ActorOutputBoundary<T>(actor, shell, id);

        internal override IMaterializedValueSource CreateMaterializedValueSource(StreamLayout.IMaterializedValueNode computation)
            => new MaterializedValueSource<T>(computation, this);
    }

    /// <summary>
    /// A Shape describes the inlets and outlets of a <see cref="IGraph{TShape}"/>. In keeping with the
    /// philosophy that a Graph is a freely reusable blueprint, everything that
    /// matters from the outside are the connections that can be made with it,
    /// otherwise it is just a black box.
    /// </summary>
    public abstract class Shape : ICloneable
    {
        /// <summary>
        /// Gets list of all input ports.
        /// </summary>
        public abstract ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Gets list of all output ports.
        /// </summary>
        public abstract ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// Creates a shape of the same kind with carbon copies of its ports.
        /// </summary>
        /// <returns>A shape containing carbon copies of this shape’s ports.</returns>
        public abstract Shape DeepCopy();

        /// <summary>
        /// Creates a shape of the same kind from the supplied ports.
        /// </summary>
        /// <param name="inlets">Input ports for the copied shape.</param>
        /// <param name="outlets">Output ports for the copied shape.</param>
        /// <returns>A shape of this type containing the supplied ports.</returns>
        public abstract Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets);

        /// <summary>
        /// Compares the input and output port sets without considering their order.
        /// </summary>
        /// <param name="shape">Shape whose ports are compared with this shape.</param>
        /// <returns><see langword="true"/> if both port sets contain the same ports; otherwise, <see langword="false"/>.</returns>
        public bool HasSamePortsAs(Shape shape)
        {
            var inlets = new HashSet<Inlet>(Inlets);
            var outlets = new HashSet<Outlet>(Outlets);

            return inlets.SetEquals(shape.Inlets) && outlets.SetEquals(shape.Outlets);
        }

        /// <summary>
        /// Compares the inlet and outlet arrays using their equality implementations.
        /// </summary>
        /// <param name="shape">Shape whose inlet and outlet arrays are compared with this shape.</param>
        /// <returns><see langword="true"/> if both arrays compare equal; otherwise, <see langword="false"/>.</returns>
        public bool HasSamePortsAndShapeAs(Shape shape) => Inlets.Equals(shape.Inlets) && Outlets.Equals(shape.Outlets);

        /// <summary>
        /// Creates a clone by calling <see cref="DeepCopy"/>.
        /// </summary>
        /// <returns>The result of <see cref="DeepCopy"/> for this shape.</returns>
        public object Clone() => DeepCopy();

        
        public sealed override string ToString() => $"{GetType().Name}([{string.Join(", ", Inlets)}] [{string.Join(", ", Outlets)}])";
    }

    /// <summary>
    /// Shape for a graph with no inlets or outlets.
    /// </summary>
    public class ClosedShape : Shape
    {
        /// <summary>
        /// The singleton closed shape.
        /// </summary>
        public static readonly ClosedShape Instance = new();
        
        private ClosedShape() { }

        /// <summary>
        /// Gets an empty array because a closed shape has no input ports.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets => ImmutableArray<Inlet>.Empty;

        /// <summary>
        /// Gets an empty array because a closed shape has no output ports.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets => ImmutableArray<Outlet>.Empty;

        /// <summary>
        /// Returns this singleton shape.
        /// </summary>
        /// <returns>This <see cref="ClosedShape"/> instance.</returns>
        public override Shape DeepCopy() => this;

        /// <summary>
        /// Validates that no ports are supplied.
        /// </summary>
        /// <param name="inlets">Input ports; must be empty.</param>
        /// <param name="outlets">Output ports; must be empty.</param>
        /// <exception cref="ArgumentException">The supplied inlet or outlet array is not empty.</exception>
        /// <returns>This <see cref="ClosedShape"/> instance.</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Any())
                throw new ArgumentException("Proposed inlets do not fit ClosedShape", nameof(inlets));
            if (outlets.Any())
                throw new ArgumentException("Proposed outlets do not fit ClosedShape", nameof(outlets));

            return this;
        }
    }

    /// <summary>
    /// This type of <see cref="Shape"/> can express any number of inputs and outputs at the
    /// expense of forgetting about their specific types. It is used mainly in the
    /// implementation of the <see cref="IGraph{TShape,TMaterializer}"/> builders and typically replaced by a more
    /// meaningful type of Shape when the building is finished.
    /// </summary>
    public class AmorphousShape : Shape
    {
        /// <summary>
        /// Creates a shape from untyped inlet and outlet collections.
        /// </summary>
        /// <param name="inlets">Input ports in shape order.</param>
        /// <param name="outlets">Output ports in shape order.</param>
        public AmorphousShape(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            Inlets = inlets;
            Outlets = outlets;
        }

        /// <summary>
        /// Gets the input ports supplied to the constructor.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Gets the output ports supplied to the constructor.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// Creates an amorphous shape with carbon copies of its ports.
        /// </summary>
        /// <returns>A new shape with copied inlet and outlet ports.</returns>
        public override Shape DeepCopy()
            => new AmorphousShape(Inlets.Select(i => i.CarbonCopy()).ToImmutableArray(),Outlets.Select(o => o.CarbonCopy()).ToImmutableArray());

        /// <summary>
        /// Creates an amorphous shape using the supplied ports.
        /// </summary>
        /// <param name="inlets">Input ports for the resulting shape.</param>
        /// <param name="outlets">Output ports for the resulting shape.</param>
        /// <returns>A new amorphous shape containing the supplied ports.</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
            => new AmorphousShape(inlets, outlets);
    }

    /// <summary>
    /// A source shape with one output port and no input ports.
    /// </summary>
    /// <typeparam name="TOut">Element type emitted by the source.</typeparam>
    public sealed class SourceShape<TOut> : Shape
    {
        /// <summary>
        /// Creates a source shape with the specified output.
        /// </summary>
        /// <param name="outlet">Output port of the source shape.</param>
        /// <exception cref="ArgumentNullException"><paramref name="outlet"/> is <see langword="null"/>.</exception>
        public SourceShape(Outlet<TOut> outlet)
        {
            Outlet = outlet ?? throw new ArgumentNullException(nameof(outlet));
            Outlets = ImmutableArray.Create<Outlet>(outlet);
        }

        /// <summary>
        /// Gets the output port of this source shape.
        /// </summary>
        public readonly Outlet<TOut> Outlet;

        /// <summary>
        /// Gets an empty array because a source shape has no input ports.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets => ImmutableArray<Inlet>.Empty;

        /// <summary>
        /// Gets the output port as an untyped outlet array.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// Creates a source shape with a carbon copy of its output port.
        /// </summary>
        /// <returns>A new source shape with a copied output port.</returns>
        public override Shape DeepCopy() => new SourceShape<TOut>((Outlet<TOut>) Outlet.CarbonCopy());

        /// <summary>
        /// Creates a source shape from exactly one output port and no input ports.
        /// </summary>
        /// <param name="inlets">Input ports; must be empty.</param>
        /// <param name="outlets">Output ports; must contain one <see cref="Outlet{TOut}"/>.</param>
        /// <exception cref="ArgumentException">The number of supplied ports does not match a source shape.</exception>
        /// <exception cref="ArgumentNullException">The output port is not an <see cref="Outlet{TOut}"/>.</exception>
        /// <returns>A source shape using the supplied output port.</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 0)
                throw new ArgumentException("Proposed inlets do not fit SourceShape", nameof(inlets));
            if (outlets.Length != 1)
                throw new ArgumentException("Proposed outlets do not fit SourceShape", nameof(outlets));

            return new SourceShape<TOut>(outlets[0] as Outlet<TOut>);
        }

       
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;

            return obj is SourceShape<TOut> shape && Equals(shape);
        }

        /// <inheritdoc/>
        private bool Equals(SourceShape<TOut> other) => Outlet.Equals(other.Outlet);

       
        public override int GetHashCode() => Outlet.GetHashCode();
    }

    /// <summary>
    /// Untyped access to the single input and output ports of a flow shape.
    /// </summary>
    public interface IFlowShape
    {
        /// <summary>
        /// Gets the input port.
        /// </summary>
        Inlet Inlet { get; }
        /// <summary>
        /// Gets the output port.
        /// </summary>
        Outlet Outlet { get; }
    }

    /// <summary>
    /// A flow shape with one typed input and one typed output.
    /// </summary>
    /// <typeparam name="TIn">Element type accepted by the input port.</typeparam>
    /// <typeparam name="TOut">Element type emitted by the output port.</typeparam>
    public sealed class FlowShape<TIn, TOut> : Shape, IFlowShape
    {
        /// <summary>
        /// Creates a flow shape from its input and output ports.
        /// </summary>
        /// <param name="inlet">Input port of the flow shape.</param>
        /// <param name="outlet">Output port of the flow shape.</param>
        /// <exception cref="ArgumentNullException">Either supplied port is <see langword="null"/>.</exception>
        public FlowShape(Inlet<TIn> inlet, Outlet<TOut> outlet)
        {
            Inlet = inlet ?? throw new ArgumentNullException(nameof(inlet), "FlowShape expected non-null inlet");
            Outlet = outlet ?? throw new ArgumentNullException(nameof(outlet), "FlowShape expected non-null outlet");
            Inlets = ImmutableArray.Create<Inlet>(inlet);
            Outlets = ImmutableArray.Create<Outlet>(outlet);
        }

        Inlet IFlowShape.Inlet => Inlet;

        Outlet IFlowShape.Outlet => Outlet;

        /// <summary>
        /// Gets the typed input port.
        /// </summary>
        public Inlet<TIn> Inlet { get; }

        /// <summary>
        /// Gets the typed output port.
        /// </summary>
        public Outlet<TOut> Outlet { get; }

        /// <summary>
        /// Gets the input port as an untyped inlet array.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Gets the output port as an untyped outlet array.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// Creates a flow shape with carbon copies of its input and output ports.
        /// </summary>
        /// <returns>A new flow shape with copied ports.</returns>
        public override Shape DeepCopy()
            => new FlowShape<TIn, TOut>((Inlet<TIn>) Inlet.CarbonCopy(), (Outlet<TOut>) Outlet.CarbonCopy());

        /// <summary>
        /// Creates a flow shape from exactly one input and one output port.
        /// </summary>
        /// <param name="inlets">Input ports; must contain one <see cref="Inlet{TIn}"/>.</param>
        /// <param name="outlets">Output ports; must contain one <see cref="Outlet{TOut}"/>.</param>
        /// <exception cref="ArgumentException">The number of supplied ports does not match a flow shape.</exception>
        /// <returns>A flow shape using the supplied ports.</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 1)
                throw new ArgumentException("Proposed inlets do not fit FlowShape", nameof(inlets));
            if (outlets.Length != 1)
                throw new ArgumentException("Proposed outlets do not fit FlowShape", nameof(outlets));

            return new FlowShape<TIn, TOut>(inlets[0] as Inlet<TIn>, outlets[0] as Outlet<TOut>);
        }
    }

    /// <summary>
    /// A sink shape with one typed input and no output ports.
    /// </summary>
    /// <typeparam name="TIn">Element type accepted by the input port.</typeparam>
    public sealed class SinkShape<TIn> : Shape
    {
        /// <summary>
        /// Gets the input port of this sink shape.
        /// </summary>
        public readonly Inlet<TIn> Inlet;

        /// <summary>
        /// Creates a sink shape with the specified input port.
        /// </summary>
        /// <param name="inlet">Input port of the sink shape.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inlet"/> is <see langword="null"/>.</exception>
        public SinkShape(Inlet<TIn> inlet)
        {
            Inlet = inlet ?? throw new ArgumentNullException(nameof(inlet), "SinkShape expected non-null inlet");
            Inlets = ImmutableArray.Create<Inlet>(inlet);
        }

        /// <summary>
        /// Gets the input port as an untyped inlet array.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Gets an empty array because a sink shape has no output ports.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets => ImmutableArray<Outlet>.Empty;

        /// <summary>
        /// Creates a sink shape with a carbon copy of its input port.
        /// </summary>
        /// <returns>A new sink shape with a copied input port.</returns>
        public override Shape DeepCopy() => new SinkShape<TIn>((Inlet<TIn>) Inlet.CarbonCopy());

        /// <summary>
        /// Creates a sink shape from exactly one input port and no output ports.
        /// </summary>
        /// <param name="inlets">Input ports; must contain one <see cref="Inlet{TIn}"/>.</param>
        /// <param name="outlets">Output ports; must be empty.</param>
        /// <exception cref="ArgumentException">The number of supplied ports does not match a sink shape.</exception>
        /// <returns>A sink shape using the supplied input port.</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (outlets.Length != 0)
                throw new ArgumentException("Proposed outlets do not fit SinkShape", nameof(outlets));
            if (inlets.Length != 1)
                throw new ArgumentException("Proposed inlets do not fit SinkShape", nameof(inlets));

            return new SinkShape<TIn>(inlets[0] as Inlet<TIn>);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;

            return obj is SinkShape<TIn> shape && Equals(shape);
        }

        private bool Equals(SinkShape<TIn> other) => Equals(Inlet, other.Inlet);

        
        public override int GetHashCode() => Inlet.GetHashCode();
    }

    /// <summary>
    /// A bidirectional shape with two typed input ports and two typed output ports.
    /// </summary>
    /// <typeparam name="TIn1">Element type accepted by the first input.</typeparam>
    /// <typeparam name="TOut1">Element type emitted by the first output.</typeparam>
    /// <typeparam name="TIn2">Element type accepted by the second input.</typeparam>
    /// <typeparam name="TOut2">Element type emitted by the second output.</typeparam>
    public sealed class BidiShape<TIn1, TOut1, TIn2, TOut2> : Shape
    {
        /// <summary>
        /// Gets the first input port.
        /// </summary>
        public readonly Inlet<TIn1> Inlet1;
        /// <summary>
        /// Gets the second input port.
        /// </summary>
        public readonly Inlet<TIn2> Inlet2;
        /// <summary>
        /// Gets the first output port.
        /// </summary>
        public readonly Outlet<TOut1> Outlet1;
        /// <summary>
        /// Gets the second output port.
        /// </summary>
        public readonly Outlet<TOut2> Outlet2;

        /// <summary>
        /// Creates a bidirectional shape from two input and two output ports.
        /// </summary>
        /// <param name="in1">First input port.</param>
        /// <param name="out1">First output port.</param>
        /// <param name="in2">Second input port.</param>
        /// <param name="out2">Second output port.</param>
        /// <exception cref="ArgumentNullException">Any supplied port is <see langword="null"/>.</exception>
        public BidiShape(Inlet<TIn1> in1, Outlet<TOut1> out1, Inlet<TIn2> in2, Outlet<TOut2> out2)
        {
            Inlet1 = in1 ?? throw new ArgumentNullException(nameof(in1));
            Inlet2 = in2 ?? throw new ArgumentNullException(nameof(in2));
            Outlet1 = out1 ?? throw new ArgumentNullException(nameof(out1));
            Outlet2 = out2 ?? throw new ArgumentNullException(nameof(out2));

            Inlets = ImmutableArray.Create<Inlet>(Inlet1, Inlet2);
            Outlets = ImmutableArray.Create<Outlet>(Outlet1, Outlet2);
        }

        /// <summary>
        /// Creates a bidirectional shape from top and bottom flow shapes.
        /// </summary>
        /// <param name="top">Flow shape used for the first input and output.</param>
        /// <param name="bottom">Flow shape used for the second input and output.</param>
        public BidiShape(FlowShape<TIn1, TOut1> top, FlowShape<TIn2, TOut2> bottom)
            : this(top.Inlet, top.Outlet, bottom.Inlet, bottom.Outlet)
        {
        }

        /// <summary>
        /// Gets both input ports in shape order.
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Gets both output ports in shape order.
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// Creates a bidirectional shape with carbon copies of all four ports.
        /// </summary>
        /// <returns>A new bidirectional shape with copied ports.</returns>
        public override Shape DeepCopy()
        {
            return new BidiShape<TIn1, TOut1, TIn2, TOut2>(
                (Inlet<TIn1>) Inlet1.CarbonCopy(),
                (Outlet<TOut1>) Outlet1.CarbonCopy(),
                (Inlet<TIn2>) Inlet2.CarbonCopy(),
                (Outlet<TOut2>) Outlet2.CarbonCopy());
        }

        /// <summary>
        /// Creates a bidirectional shape from exactly two input and two output ports.
        /// </summary>
        /// <param name="inlets">Input ports in shape order; must contain two ports of the corresponding types.</param>
        /// <param name="outlets">Output ports in shape order; must contain two ports of the corresponding types.</param>
        /// <exception cref="ArgumentException">Either array does not contain exactly two ports.</exception>
        /// <returns>A bidirectional shape using the supplied ports.</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 2) throw new ArgumentException($"Proposed inlets [{string.Join(", ", inlets)}] don't fit BidiShape");
            if (outlets.Length != 2) throw new ArgumentException($"Proposed outlets [{string.Join(", ", outlets)}] don't fit BidiShape");

            return new BidiShape<TIn1, TOut1, TIn2, TOut2>((Inlet<TIn1>)inlets[0], (Outlet<TOut1>)outlets[0], (Inlet<TIn2>)inlets[1], (Outlet<TOut2>)outlets[1]);
        }

        /// <summary>
        /// Creates a shape with the first and second flows swapped.
        /// </summary>
        /// <returns>A bidirectional shape whose input and output pairs are reversed.</returns>
        public Shape Reversed() => new BidiShape<TIn2, TOut2, TIn1, TOut1>(Inlet2, Outlet2, Inlet1, Outlet1);
    }

    /// <summary>
    /// Factory methods for constructing bidirectional shapes.
    /// </summary>
    public static class BidiShape
    {
        /// <summary>
        /// Creates a bidirectional shape from top and bottom flow shapes.
        /// </summary>
        /// <typeparam name="TIn1">Input element type of the top flow.</typeparam>
        /// <typeparam name="TOut1">Output element type of the top flow.</typeparam>
        /// <typeparam name="TIn2">Input element type of the bottom flow.</typeparam>
        /// <typeparam name="TOut2">Output element type of the bottom flow.</typeparam>
        /// <param name="top">Flow shape used for the first input and output.</param>
        /// <param name="bottom">Flow shape used for the second input and output.</param>
        /// <returns>A bidirectional shape containing the ports of both flows.</returns>
        public static BidiShape<TIn1, TOut1, TIn2, TOut2> FromFlows<TIn1, TOut1, TIn2, TOut2>(
            FlowShape<TIn1, TOut1> top, FlowShape<TIn2, TOut2> bottom)
            => new(top.Inlet, top.Outlet, bottom.Inlet, bottom.Outlet);
    }
}
