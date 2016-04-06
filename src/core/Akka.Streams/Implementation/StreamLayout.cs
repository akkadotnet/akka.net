using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    public static class StreamLayout
    {
        //TODO: Materialization order
        //TODO: Special case linear composites
        //TODO: Cycles

        public static readonly bool IsDebug = false;

        #region Materialized Value Node types

        public interface IMaterializedValueNode { }
        public sealed class Combine : IMaterializedValueNode
        {
            public readonly Func<object, object, object> Combinator;
            public readonly IMaterializedValueNode Left;
            public readonly IMaterializedValueNode Right;

            public Combine(Func<object, object, object> combinator, IMaterializedValueNode left, IMaterializedValueNode right)
            {
                Combinator = combinator;
                Left = left;
                Right = right;
            }

            public override string ToString() => $"Combine({Left}, {Right})";
        }

        public sealed class Atomic : IMaterializedValueNode
        {
            public readonly IModule Module;

            public Atomic(IModule module)
            {
                Module = module;
            }

            public override string ToString() => $"Atomic({Module.Attributes.GetNameOrDefault(Module.GetType().Name)}[{Module.GetHashCode()}])";
        }

        public sealed class Transform : IMaterializedValueNode
        {
            public readonly Func<object, object> Transformator;
            public readonly IMaterializedValueNode Node;

            public Transform(Func<object, object> transformator, IMaterializedValueNode node)
            {
                Transformator = transformator;
                Node = node;
            }

            public override string ToString() => $"Transform({Node})";
        }

        public sealed class Ignore : IMaterializedValueNode
        {
            public static readonly Ignore Instance = new Ignore();
            private Ignore() { }

            public override string ToString() => "Ignore";
        }

        #endregion

        public static void Validate(IModule module, int level = 0, bool shouldPrint = false, IDictionary<object, int> idMap = null)
        {
            idMap = idMap ?? new Dictionary<object, int>();
            var currentId = 1;
            Func<int> ids = () => currentId++;
            Func<object, int> id = obj =>
            {
                int x;
                if (!idMap.TryGetValue(obj, out x))
                {
                    x = ids();
                    idMap.Add(obj, x);
                }
                return x;
            };
            Func<InPort, string> inPort = i => $"{i}@{id(i)}";
            Func<OutPort, string> outPort = o => $"{o}@{id(o)}";
            Func<IEnumerable<InPort>, string> ins = i => $"In[{string.Join(",", i.Select(inPort))}]";
            Func<IEnumerable<OutPort>, string> outs = o => $"Out[{string.Join(",", o.Select(outPort))}]";
            Func<OutPort, InPort, string> pair = (o, i) => $"{inPort(i)}->{outPort(o)}";
            Func<IEnumerable<KeyValuePair<OutPort, InPort>>, string> pairs = i =>
                $"[{string.Join(",", i.Select(p => pair(p.Key, p.Value)))}]";

            var shape = module.Shape;
            var inset = shape.Inlets.ToImmutableHashSet();
            var outset = shape.Outlets.ToImmutableHashSet();
            var inPorts = module.InPorts.Cast<Inlet>().ToImmutableHashSet();
            var outPorts = module.OutPorts.Cast<Outlet>().ToImmutableHashSet();
            var problems = new List<string>();

            if (inset.Count != shape.Inlets.Count()) problems.Add("shape has duplicate inlets " + ins(shape.Inlets));
            if (!inset.SetEquals(inPorts)) problems.Add($"shape has extra {ins(inset.Except(inPorts))}, module has extra {ins(inPorts.Except(inset))}");
            var connectedInlets = inset.Intersect(module.Upstreams.Keys).ToArray();
            if (connectedInlets.Any()) problems.Add("found connected inlets " + ins(connectedInlets));
            if (outset.Count != shape.Outlets.Count()) problems.Add("shape has duplicate outlets " + outs(shape.Outlets));
            if (!outset.SetEquals(outPorts)) problems.Add($"shape has extra {outs(outset.Except(outPorts))}, module has extra {outs(outPorts.Except(outset))}");
            var connectedOutlets = outset.Intersect(module.Downstreams.Keys).ToArray();
            if (connectedOutlets.Any()) problems.Add("found connected outlets " + outs(connectedOutlets));

            var ups = module.Upstreams.ToImmutableHashSet();
            var ups2 = ups.Select(x => new KeyValuePair<OutPort, InPort>(x.Value, x.Key)).ToImmutableHashSet();
            var downs = module.Downstreams.ToImmutableHashSet();
            var inter = ups2.Intersect(downs);

            if (!downs.SetEquals(ups2)) problems.Add($"inconsistent maps: ups {pairs(ups2.Except(inter))} downs {pairs(downs.Except(inter))}");

            var allInBuilder = ImmutableHashSet<InPort>.Empty.ToBuilder();
            var duplicateInBuilder = ImmutableHashSet<InPort>.Empty.ToBuilder();
            var allOutBuilder = ImmutableHashSet<OutPort>.Empty.ToBuilder();
            var duplicateOutBuilder = ImmutableHashSet<OutPort>.Empty.ToBuilder();
            foreach (var subModule in module.SubModules)
            {
                // check for duplicates before adding current module's ports
                duplicateInBuilder.UnionWith(allInBuilder.Intersect(subModule.InPorts));
                allInBuilder.UnionWith(subModule.InPorts);
                // check for duplicates before adding current module's ports
                duplicateOutBuilder.UnionWith(allOutBuilder.Intersect(subModule.OutPorts));
                allOutBuilder.UnionWith(subModule.OutPorts);
            }
            var allIn = allInBuilder.ToImmutable();
            var duplicateIn = duplicateInBuilder.ToImmutable();
            var allOut = allOutBuilder.ToImmutable();
            var duplicateOut = duplicateOutBuilder.ToImmutable();

            if (!duplicateIn.IsEmpty) problems.Add("duplicate ports in submodules " + ins(duplicateIn));
            if (!duplicateOut.IsEmpty) problems.Add("duplicate ports in submodules " + outs(duplicateOut));
            if (!module.IsSealed && inset.Except(allIn).Any()) problems.Add("foreign inlets " + ins(inset.Except(allIn)));
            if (!module.IsSealed && outset.Except(allOut).Any()) problems.Add("foreign outlets " + outs(outset.Except(allOut)));
            var unIn = allIn.Except(inset).Except(module.Upstreams.Keys);
            if (unIn.Any() && !module.IsCopied) problems.Add("unconnected inlets " + ins(unIn));
            var unOut = allOut.Except(outset).Except(module.Downstreams.Keys);
            if (unOut.Any() && !module.IsCopied) problems.Add("unconnected outlets " + outs(unOut));

            var atomics = Atomics(module.MaterializedValueComputation);
            var graphValues = module.SubModules.SelectMany(m => m is GraphModule ? ((GraphModule)m).MaterializedValueIds : Enumerable.Empty<IModule>());
            var nonExistent = atomics.Except(module.SubModules).Except(graphValues).Except(new[] { module });
            if (nonExistent.Any()) problems.Add("computation refers to non-existent modules " + string.Join(", ", nonExistent));

            var print = shouldPrint || problems.Any();

            if (print)
            {
                var indent = string.Empty.PadLeft(level * 2);
                Console.Out.WriteLine("{0}{1}({2}): {3} {4}", indent, typeof(StreamLayout).Name, shape, ins(inPorts), outs(outPorts));
                foreach (var downstream in module.Downstreams)
                {
                    Console.Out.WriteLine("{0}    {1} -> {2}", indent, outPort(downstream.Key), inPort(downstream.Value));
                }
                foreach (var problem in problems)
                {
                    Console.Out.WriteLine("{0}  -!- {1}", indent, problem);
                }
            }

            foreach (var subModule in module.SubModules)
            {
                Validate(subModule, level + 1, print, idMap);
            }

            if (problems.Any() && !shouldPrint) throw new IllegalStateException(
                $"module inconsistent, found {problems.Count} problems:\n - {string.Join("\n - ", problems)}");
        }

        private static ImmutableHashSet<IModule> Atomics(IMaterializedValueNode node)
        {
            if (node is Ignore) return ImmutableHashSet<IModule>.Empty;
            if (node is Transform) return Atomics(((Transform)node).Node);
            if (node is Atomic) return ImmutableHashSet.Create(((Atomic)node).Module);
            Combine c;
            if ((c = node as Combine) != null) return Atomics(c.Left).Union(Atomics(c.Right));
            throw new ArgumentException("Couldn't extract atomics for node " + node.GetType());
        }
    }

    public interface IModule : IComparable<IModule>
    {
        Shape Shape { get; }

        /// <summary>
        /// Verify that the given Shape has the same ports and return a new module with that shape.
        /// Concrete implementations may throw UnsupportedOperationException where applicable.
        /// </summary>
        IModule ReplaceShape(Shape shape);

        IImmutableSet<InPort> InPorts { get; }
        IImmutableSet<OutPort> OutPorts { get; }
        bool IsRunnable { get; }

        bool IsSink { get; }
        bool IsSource { get; }
        bool IsFlow { get; }
        bool IsBidiFlow { get; }
        bool IsAtomic { get; }
        bool IsCopied { get; }

        /// <summary>
        /// Fuses this Module to <paramref name="that"/> Module by wiring together <paramref name="from"/> and <paramref name="to"/>,
        /// retaining the materialized value of `this` in the result
        /// </summary>
        /// <param name="that">A module to fuse with</param>
        /// <param name="from">The data source to wire</param>
        /// <param name="to">The data sink to wire</param>
        /// <returns>A module representing fusion of `this` and <paramref name="that"/></returns>
        IModule Fuse(IModule that, OutPort from, InPort to);

        /// <summary>
        /// Fuses this Module to <paramref name="that"/> Module by wiring together <paramref name="from"/> and <paramref name="to"/>,
        /// retaining the materialized value of `this` in the result, using the provided function <paramref name="matFunc"/>.
        /// </summary>
        /// <param name="that">A module to fuse with</param>
        /// <param name="from">The data source to wire</param>
        /// <param name="to">The data sink to wire</param>
        /// <param name="matFunc">The function to apply to the materialized values</param>
        /// <returns>A module representing fusion of `this` and <paramref name="that"/></returns>
        IModule Fuse<T1, T2, T3>(IModule that, OutPort from, InPort to, Func<T1, T2, T3> matFunc);

        /// <summary>
        /// Creates a new Module based on the current Module but with the given OutPort wired to the given InPort.
        /// </summary>
        /// <param name="from">The OutPort to wire.</param>
        /// <param name="to">The InPort to wire.</param>
        /// <returns>A new Module with the ports wired</returns>
        IModule Wire(OutPort from, InPort to);

        IModule TransformMaterializedValue<TMat, TMat2>(Func<TMat, TMat2> mapFunc);

        /// <summary>
        /// Creates a new Module which is `this` Module composed with <paramref name="that"/> Module.
        /// </summary>
        /// <param name="that">A Module to be composed with (cannot be itself)</param>
        /// <returns>A Module that represents the composition of `this` and <paramref name="that"/></returns>
        IModule Compose(IModule that);

        /// <summary>
        /// Creates a new Module which is `this` Module composed with <paramref name="that"/> Module,
        /// using the given function <paramref name="matFunc"/> to compose the materialized value of `this` with
        /// the materialized value of <paramref name="that"/>.
        /// </summary>
        /// <param name="that">A Module to be composed with (cannot be itself)</param>
        /// <param name="matFunc">A function which combines the materialized values</param>
        /// <typeparam name="T1">The type of the materialized value of `this`</typeparam>
        /// <typeparam name="T2">The type of the materialized value of <paramref name="that"/></typeparam>
        /// <typeparam name="T3">The type of the materialized value of the returned Module</typeparam>
        /// <returns>A Module that represents the composition of `this` and <paramref name="that"/></returns>
        IModule Compose<T1, T2, T3>(IModule that, Func<T1, T2, T3> matFunc);

        /// <summary>
        /// Creates a new Module which is `this` Module composed with <paramref name="that"/> Module.
        ///
        /// The difference to compose(that) is that this version completely ignores the materialized value
        /// computation of <paramref name="that"/> while the normal version executes the computation and discards its result.
        /// This means that this version must not be used for user-provided <paramref name="that"/> modules because users may
        /// transform materialized values only to achieve some side-effect; it can only be
        /// used where we know that there is no meaningful computation to be done (like for
        /// MaterializedValueSource).
        /// </summary>
        /// <param name="that">a Module to be composed with (cannot be itself)</param>
        /// <returns>a Module that represents the composition of `this` and <paramref name="that"/></returns>
        IModule ComposeNoMaterialized(IModule that);

        /// <summary>
        /// Creates a new Module which contains `this` Module
        /// </summary>
        IModule Nest();

        // this cannot be set, since sets are changing ordering of modules
        // which must be kept for fusing to work
        ImmutableArray<IModule> SubModules { get; }
        bool IsSealed { get; }

        IImmutableDictionary<OutPort, InPort> Downstreams { get; }
        IImmutableDictionary<InPort, OutPort> Upstreams { get; }

        StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }
        IModule CarbonCopy();

        Attributes Attributes { get; }
        IModule WithAttributes(Attributes attributes);
    }

    public abstract class Module : IModule
    {
        private readonly Lazy<IImmutableSet<InPort>> _inports;
        private readonly Lazy<IImmutableSet<OutPort>> _outports;

        protected Module()
        {
            _inports = new Lazy<IImmutableSet<InPort>>(() => ImmutableHashSet.CreateRange(Shape.Inlets.Cast<InPort>()));
            _outports = new Lazy<IImmutableSet<OutPort>>(() => ImmutableHashSet.CreateRange(Shape.Outlets.Cast<OutPort>()));
        }

        public IImmutableSet<InPort> InPorts => _inports.Value;
        public IImmutableSet<OutPort> OutPorts => _outports.Value;
        public virtual bool IsRunnable => InPorts.Count == 0 && OutPorts.Count == 0;
        public virtual bool IsSink => InPorts.Count == 1 && OutPorts.Count == 0;
        public virtual bool IsSource => InPorts.Count == 0 && OutPorts.Count == 1;
        public virtual bool IsFlow => InPorts.Count == 1 && OutPorts.Count == 1;
        public virtual bool IsBidiFlow => InPorts.Count == 2 && OutPorts.Count == 2;
        public virtual bool IsAtomic => !SubModules.Any();
        public virtual bool IsCopied => false;
        public virtual bool IsFused => false;

        public virtual IModule Fuse(IModule other, OutPort @from, InPort to)
        {
            return Fuse<object, object, object>(other, from, to, Keep.Left);
        }

        public virtual IModule Fuse<T1, T2, T3>(IModule other, OutPort @from, InPort to, Func<T1, T2, T3> matFunc)
        {
            return Compose(other, matFunc).Wire(from, to);
        }

        public virtual IModule Wire(OutPort @from, InPort to)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            if (!OutPorts.Contains(from))
            {
                var message = Downstreams.ContainsKey(from)
                    ? $"The output port [{@from}] is already connected"
                    : $"The output port [{@from}] is not part of underlying graph";
                throw new ArgumentException(message);
            }

            if (!InPorts.Contains(to))
            {
                var message = Upstreams.ContainsKey(to)
                    ? $"The input port [{@from}] is already connected"
                    : $"The input port [{@from}] is not part of underlying graph";
                throw new ArgumentException(message);
            }

            return new CompositeModule(
                subModules: SubModules,
                shape: new AmorphousShape(
                    Shape.Inlets.Where(i => !i.Equals(to)).ToImmutableArray(),
                    Shape.Outlets.Where(o => !o.Equals(from)).ToImmutableArray()),
                downstreams: Downstreams.SetItem(from, to),
                upstreams: Upstreams.SetItem(to, from),
                materializedValueComputation: MaterializedValueComputation,
                attributes: Attributes);
        }

        public virtual IModule TransformMaterializedValue<TMat, TMat2>(Func<TMat, TMat2> mapFunc)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            return new CompositeModule(
                subModules: IsSealed ? ImmutableArray.Create(this as IModule) : SubModules,
                shape: Shape,
                downstreams: Downstreams,
                upstreams: Upstreams,
                materializedValueComputation: new StreamLayout.Transform(x => mapFunc((TMat)x), IsSealed
                    ? new StreamLayout.Atomic(this)
                    : MaterializedValueComputation),
                attributes: Attributes);
        }

        public virtual IModule Compose(IModule other)
        {
            return Compose<object, object, object>(other, Keep.Left);
        }

        public virtual IModule Compose<T1, T2, T3>(IModule other, Func<T1, T2, T3> matFunc)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            if (Equals(other, this))
                throw new ArgumentException("A module cannot be added to itself. You should pass a separate instance to compose().");
            if (SubModules.Contains(other))
                throw new ArgumentException("An existing submodule cannot be added again. All contained modules must be unique.");

            var modules1 = IsSealed ? ImmutableArray.Create<IModule>(this) : SubModules;
            var modules2 = other.IsSealed ? ImmutableArray.Create(other) : other.SubModules;

            var matComputationLeft = IsSealed ? new StreamLayout.Atomic(this) : MaterializedValueComputation;
            var matComputationRight = other.IsSealed ? new StreamLayout.Atomic(other) : other.MaterializedValueComputation;

            StreamLayout.IMaterializedValueNode comp;

            if (IsKeepLeft(matFunc) && IsIgnorable(matComputationRight)) comp = matComputationLeft;
            else if (IsKeepRight(matFunc) && IsIgnorable(matComputationLeft)) comp = matComputationRight;
            else comp = new StreamLayout.Combine((x, y) => matFunc((T1)x, (T2)y), matComputationLeft, matComputationRight);

            return new CompositeModule(
                subModules: modules1.Concat(modules2).ToImmutableArray(),
                shape: new AmorphousShape(
                    Shape.Inlets.Concat(other.Shape.Inlets).ToImmutableArray(),
                    Shape.Outlets.Concat(other.Shape.Outlets).ToImmutableArray()),
                downstreams: Downstreams.AddRange(other.Downstreams),
                upstreams: Upstreams.AddRange(other.Upstreams),
                materializedValueComputation: comp,
                attributes: Attributes.None);
        }
        
        private static readonly RuntimeMethodHandle KeepRightMethodhandle = typeof (Keep).GetMethod(nameof(Keep.Right)).MethodHandle;
        private bool IsKeepRight<T1, T2, T3>(Func<T1, T2, T3> fn)
        {
            return fn.Method.IsGenericMethod && fn.Method.GetGenericMethodDefinition().MethodHandle.Value == KeepRightMethodhandle.Value;
        }

        private static readonly RuntimeMethodHandle KeepLeftMethodhandle = typeof(Keep).GetMethod(nameof(Keep.Left)).MethodHandle;
        private bool IsKeepLeft<T1, T2, T3>(Func<T1, T2, T3> fn)
        {
            return fn.Method.IsGenericMethod && fn.Method.GetGenericMethodDefinition().MethodHandle.Value == KeepLeftMethodhandle.Value;
        }

        private bool IsIgnorable(StreamLayout.IMaterializedValueNode computation)
        {
            if (computation is StreamLayout.Atomic) return IsIgnorable(((StreamLayout.Atomic) computation).Module);
            return (computation is StreamLayout.Combine || computation is StreamLayout.Transform) || !(computation is StreamLayout.Ignore);
        }

        private bool IsIgnorable(IModule module)
        {
            if (module is AtomicModule || module is EmptyModule) return true;
            if (module is CopiedModule) return IsIgnorable(((CopiedModule) module).CopyOf);
            if (module is CompositeModule) return IsIgnorable(((CompositeModule) module).MaterializedValueComputation);
            if (module is FusedModule) return IsIgnorable(((FusedModule) module).MaterializedValueComputation);

            throw new NotSupportedException($"Module of type {module.GetType()} is not supported by this method");
        }

        public IModule ComposeNoMaterialized(IModule that)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            if (ReferenceEquals(this, that)) throw new ArgumentException("A module cannot be added to itself. You should pass a separate instance to Compose().");
            if (SubModules.Contains(that)) throw new ArgumentException("An existing submodule cannot be added again. All contained modules must be unique.");

            var module1 = IsSealed ? ImmutableArray.Create<IModule>(this) : SubModules;
            var module2 = that.IsSealed ? ImmutableArray.Create(that) : that.SubModules;

            var matComputation = IsSealed ? new StreamLayout.Atomic(this) : MaterializedValueComputation;

            return new CompositeModule(
                subModules: module1.Concat(module2).ToImmutableArray(),
                shape: new AmorphousShape(
                    Shape.Inlets.Concat(that.Shape.Inlets).ToImmutableArray(),
                    Shape.Outlets.Concat(that.Shape.Outlets).ToImmutableArray()),
                downstreams: Downstreams.AddRange(that.Downstreams),
                upstreams: Upstreams.AddRange(that.Upstreams),
                materializedValueComputation: matComputation,
                attributes: Attributes.None);
        }

        public virtual IModule Nest()
        {
            return new CompositeModule(
                subModules: ImmutableArray.Create(this as IModule),
                shape: Shape,
                /*
                 * Composite modules always maintain the flattened upstreams/downstreams map (i.e. they contain all the
                 * layout information of all the nested modules). Copied modules break the nesting, scoping them to the
                 * copied module. The MaterializerSession will take care of propagating the necessary Publishers and Subscribers
                 * from the enclosed scope to the outer scope.
                 */
                downstreams: Downstreams,
                upstreams: Upstreams,
                /*
                 * Wrapping like this shields the outer module from the details of the
                 * materialized value computation of its submodules.
                 */
                materializedValueComputation: new StreamLayout.Atomic(this),
                attributes: Attributes.None);
        }

        public bool IsSealed => IsAtomic || IsCopied || IsFused || Attributes.AttributeList.Count() != 0;
        public virtual IImmutableDictionary<OutPort, InPort> Downstreams => ImmutableDictionary<OutPort, InPort>.Empty;
        public virtual IImmutableDictionary<InPort, OutPort> Upstreams => ImmutableDictionary<InPort, OutPort>.Empty;
        public virtual StreamLayout.IMaterializedValueNode MaterializedValueComputation => new StreamLayout.Atomic(this);

        public abstract Shape Shape { get; }
        public abstract IModule ReplaceShape(Shape shape);

        public abstract ImmutableArray<IModule> SubModules { get; }
        public abstract IModule CarbonCopy();
        public abstract Attributes Attributes { get; }
        public abstract IModule WithAttributes(Attributes attributes);
        public int CompareTo(IModule other)
        {
            return GetHashCode().CompareTo(other.GetHashCode());
        }
    }

    public sealed class EmptyModule : Module
    {
        public static readonly EmptyModule Instance = new EmptyModule();

        private EmptyModule() { }

        public override Shape Shape => ClosedShape.Instance;
        public override bool IsAtomic => false;
        public override bool IsRunnable => false;
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation => StreamLayout.Ignore.Instance;

        public override IModule ReplaceShape(Shape shape)
        {
            if (shape is ClosedShape) return this;
            else throw new NotSupportedException("Cannot replace the shape of empty module");
        }

        public override IModule Compose(IModule other) => other;

        public override IModule Compose<T1, T2, T3>(IModule other, Func<T1, T2, T3> matFunc)
        {
            throw new NotSupportedException("It is invalid to combine materialized value with EmptyModule");
        }

        public override IModule Nest() => this;

        public override ImmutableArray<IModule> SubModules { get; } = ImmutableArray<IModule>.Empty;

        public override IModule CarbonCopy() => this;

        public override Attributes Attributes => Attributes.None;

        public override IModule WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException("EmptyModule cannot carry attributes");
        }
    }

    public sealed class CopiedModule : Module
    {
        public CopiedModule(Shape shape, Attributes attributes, IModule copyOf)
        {
            Shape = shape;
            Attributes = attributes;
            CopyOf = copyOf;
            SubModules = ImmutableArray.Create(copyOf);
        }

        public override ImmutableArray<IModule> SubModules { get; }
        public override Attributes Attributes { get; }

        public IModule CopyOf { get; }
        public override Shape Shape { get; }

        public override bool IsCopied => true;
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation => new StreamLayout.Atomic(CopyOf);

        public override IModule ReplaceShape(Shape shape)
        {
            if (!ReferenceEquals(shape, Shape))
            {
                if (!Shape.HasSamePortsAs(shape))
                    throw new ArgumentException("CopiedModule requires shape with same ports to replace", nameof(shape));
                return CompositeModule.Create(this, shape);
            }
            return this;
        }

        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, CopyOf);

        public override IModule WithAttributes(Attributes attributes)
        {
            if (!ReferenceEquals(attributes, Attributes))
                return new CopiedModule(Shape, attributes, CopyOf);
            return this;
        }

        public override string ToString() => $"Copy({CopyOf})";
    }

    public sealed class CompositeModule : Module
    {
        public CompositeModule(ImmutableArray<IModule> subModules,
            Shape shape,
            IImmutableDictionary<OutPort, InPort> downstreams,
            IImmutableDictionary<InPort, OutPort> upstreams,
            StreamLayout.IMaterializedValueNode materializedValueComputation,
            Attributes attributes)
        {
            SubModules = subModules;
            Shape = shape;
            Downstreams = downstreams;
            Upstreams = upstreams;
            MaterializedValueComputation = materializedValueComputation;
            Attributes = attributes;
        }

        public override IImmutableDictionary<InPort, OutPort> Upstreams { get; }
        public override IImmutableDictionary<OutPort, InPort> Downstreams { get; }

        public override Shape Shape { get; }
        public override Attributes Attributes { get; }
        public override ImmutableArray<IModule> SubModules { get; }
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        public override IModule ReplaceShape(Shape shape)
        {
            if (!ReferenceEquals(shape, Shape))
            {
                if (!Shape.HasSamePortsAs(shape))
                    throw new ArgumentException("CombinedModule requires shape with same ports to replace",
                        nameof(shape));

                return new CompositeModule(SubModules, shape, Downstreams, Upstreams, MaterializedValueComputation,
                    Attributes);
            }
            return this;
        }

        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        public override IModule WithAttributes(Attributes attributes) => new CompositeModule(SubModules, Shape, Downstreams, Upstreams, MaterializedValueComputation, attributes);

        public override string ToString() => $"Composite({string.Join(", ", SubModules)})";

        public static CompositeModule Create(Module module, Shape shape)
        {
            return new CompositeModule(
                new IModule[] {module}.ToImmutableArray(),
                shape,
                ImmutableDictionary<OutPort, InPort>.Empty,
                ImmutableDictionary<InPort, OutPort>.Empty,
                new StreamLayout.Atomic(module),
                Attributes.None
                );
        }
    }

    public sealed class FusedModule : Module
    {
        public readonly Streams.Fusing.StructuralInfo Info;

        public FusedModule(
            ImmutableArray<IModule> subModules,
            Shape shape,
            IImmutableDictionary<OutPort, InPort> downstreams,
            IImmutableDictionary<InPort, OutPort> upstreams,
            StreamLayout.IMaterializedValueNode materializedValueComputation,
            Attributes attributes,
            Streams.Fusing.StructuralInfo info)
        {
            SubModules = subModules;
            Shape = shape;
            Downstreams = downstreams;
            Upstreams = upstreams;
            MaterializedValueComputation = materializedValueComputation;
            Attributes = attributes;
            Info = info;
        }

        public override bool IsFused => true;
        public override IImmutableDictionary<OutPort, InPort> Downstreams { get; }
        public override IImmutableDictionary<InPort, OutPort> Upstreams { get; }
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        public override Shape Shape { get; }
        public override ImmutableArray<IModule> SubModules { get; }
        public override Attributes Attributes { get; }

        public override IModule ReplaceShape(Shape shape)
        {
            if (!ReferenceEquals(shape, Shape))
            {
                if (!shape.HasSamePortsAndShapeAs(Shape))
                    throw new ArgumentException("FusedModule requires shape with the same ports as existing one",
                        nameof(shape));
                return new FusedModule(SubModules, shape, Downstreams, Upstreams, MaterializedValueComputation,
                    Attributes, Info);
            }
            return this;
        }

        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        public override IModule WithAttributes(Attributes attributes) => new FusedModule(SubModules, Shape, Downstreams, Upstreams, MaterializedValueComputation, attributes, Info);

        public override string ToString()
        {
            return $"\n  Name: {Attributes.GetNameOrDefault("unnamed")}" +
                   "\n  Modules:" +
                   $"\n    {string.Join("\n    ", SubModules.Select(m => m.Attributes.GetNameLifted() ?? m.ToString().Replace("\n", "\n    ")))}" +
                   $"\n  Downstreams: {string.Join("", Downstreams.Select(kvp => $"\n    {kvp.Key} -> {kvp.Value}"))}" +
                   $"\n  Upstreams: {string.Join("", Upstreams.Select(kvp => $"\n    {kvp.Key} -> {kvp.Value}"))}";
        }
    }

    /// <summary>
    /// This is the only extension point for the sealed type hierarchy: composition
    /// (i.e. the module tree) is managed strictly within this file, only leaf nodes
    /// may be declared elsewhere.
    /// </summary>
    public abstract class AtomicModule : Module
    {
        public sealed override ImmutableArray<IModule> SubModules => ImmutableArray<IModule>.Empty;
        public sealed override IImmutableDictionary<OutPort, InPort> Downstreams => base.Downstreams;
        public override IImmutableDictionary<InPort, OutPort> Upstreams => base.Upstreams;
    }

    internal sealed class VirtualProcessor<T> : IProcessor<T, T>
    {
        #region internal classes

        internal interface ITermination { }
        internal struct Allowed : ITermination
        {
            public static readonly Allowed Instance = new Allowed();
        }
        internal struct Completed : ITermination
        {
            public static readonly Completed Instance = new Completed();
        }
        internal struct Failed : ITermination
        {
            public readonly Exception Reason;

            public Failed(Exception reason) : this()
            {
                Reason = reason;
            }
        }

        private sealed class Sub : AtomicCounterLong, ISubscription
        {
            private readonly ISubscription _subscription;
            private readonly AtomicReference<object> _subscriptionStatus;

            public Sub(ISubscription subscription, AtomicReference<object> subscriptionStatus)
            {
                _subscription = subscription;
                _subscriptionStatus = subscriptionStatus;
            }

            public void Request(long n)
            {
                var current = Current;
                while (true)
                {
                    if (current < 0) _subscription.Request(n);
                    else if (CompareAndSet(current, current + n)) ;
                    else continue;
                    break;
                }
            }

            public void Cancel()
            {
                _subscriptionStatus.Value = InnertSubscriber;
                _subscription.Cancel();
            }

            public void CloseLatch()
            {
                var requested = GetAndSet(-1);
                if (requested > 0) _subscription.Request(requested);
            }
        }

        #endregion

        private static readonly CancellingSubscriber<T> InnertSubscriber = new CancellingSubscriber<T>();

        private readonly AtomicReference<object> _subscriptionStatus = new AtomicReference<object>();
        private readonly AtomicReference<ITermination> _terminationStatus = new AtomicReference<ITermination>();

        public void Subscribe(ISubscriber<T> subscriber)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);

            if (_subscriptionStatus.CompareAndSet(null, subscriber))
            {
                /* wait for OnSubscribe */
            }
            else
            {
                var status = _subscriptionStatus.Value;
                if (status is ISubscriber<T>) ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "VirtualProcessor");
                else if (status is Sub)
                {
                    var sub = (Sub)status;
                    try
                    {
                        _subscriptionStatus.Value = subscriber;
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, sub);
                        sub.CloseLatch(); // allow onNext only now
                        var terminationStatus = _terminationStatus.GetAndSet(Allowed.Instance);
                        if (terminationStatus is Completed) ReactiveStreamsCompliance.TryOnComplete(subscriber);
                        else if (terminationStatus is Failed) ReactiveStreamsCompliance.TryOnError(subscriber, ((Failed)terminationStatus).Reason);
                    }
                    catch (Exception)
                    {
                        sub.Cancel();
                    }
                }
            }
        }

        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);

            var wrapped = new Sub(subscription, _subscriptionStatus);
            if (_subscriptionStatus.CompareAndSet(null, wrapped))
            {
                /* wait for subscriber */
            }
            else
            {
                var value = _subscriptionStatus.Value;
                if (value is ISubscriber<T>)
                {
                    var sub = (ISubscriber<T>)value;
                    var terminationStatus = _terminationStatus.Value;
                    if (terminationStatus is Allowed)
                    {
                        /*
                         * There is a race condition here: if this thread reads the subscriptionStatus after
                         * set set() in subscribe() but then sees the terminationStatus before the getAndSet()
                         * is published then we will rely upon the downstream Subscriber for cancelling this
                         * Subscription. I only mention this because the TCK requires that we handle this here
                         * (since the manualSubscriber used there does not expose this behavior).
                         */
                        subscription.Cancel();
                    }
                    else
                    {
                        ReactiveStreamsCompliance.TryOnSubscribe(sub, wrapped);
                        wrapped.CloseLatch(); // allow OnNext only now
                        _terminationStatus.Value = Allowed.Instance;
                    }
                }
                else if (value is ISubscription)
                    subscription.Cancel(); // reject further Subscriptions
            }
        }

        public void OnNext(T element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            ReactiveStreamsCompliance.TryOnNext(_subscriptionStatus.Value as ISubscriber<T>, element);
        }

        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            if (_terminationStatus.CompareAndSet(null, new Failed(cause))) /* let it be picked up by Subscribe() */ ;
            else ReactiveStreamsCompliance.TryOnError(_subscriptionStatus.Value as ISubscriber<T>, cause);
        }

        public void OnComplete()
        {
            if (_terminationStatus.CompareAndSet(null, Completed.Instance)) /* let it be picked up by Subscribe() */ ;
            else ReactiveStreamsCompliance.TryOnComplete(_subscriptionStatus.Value as ISubscriber<T>);
        }

        void ISubscriber.OnNext(object element)
        {
            OnNext((T)element);
        }

        void IPublisher.Subscribe(ISubscriber subscriber)
        {
            Subscribe((ISubscriber<T>)subscriber);
        }
    }

    internal abstract class MaterializerSession
    {
        public class MaterializationPanicException : Exception
        {
            public MaterializationPanicException(Exception innerException) : base("Materialization aborted.", innerException)
            {
            }

            protected MaterializationPanicException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
        }

        protected readonly IModule TopLevel;
        protected readonly Attributes InitialAttributes;

        private readonly LinkedList<IDictionary<InPort, ISubscriber>> _subscribersStack = new LinkedList<IDictionary<InPort, ISubscriber>>();
        private readonly LinkedList<IDictionary<OutPort, IPublisher>> _publishersStack = new LinkedList<IDictionary<OutPort, IPublisher>>();
        private readonly IDictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>> _materializedValueSources =
            new Dictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>>();

        /// <summary>
        /// Please note that this stack keeps track of the scoped modules wrapped in CopiedModule but not the CopiedModule
        /// itself. The reason is that the CopiedModule itself is only needed for the enterScope and exitScope methods but
        /// not elsewhere. For this reason they are just simply passed as parameters to those methods.
        ///
        /// The reason why the encapsulated (copied) modules are stored as mutable state to save subclasses of this class
        /// from passing the current scope around or even knowing about it.
        /// </summary>
        private readonly LinkedList<IModule> _moduleStack = new LinkedList<IModule>();

        protected MaterializerSession(IModule topLevel, Attributes initialAttributes)
        {
            TopLevel = topLevel;
            InitialAttributes = initialAttributes;
            _subscribersStack.AddFirst(new Dictionary<InPort, ISubscriber>());
            _publishersStack.AddFirst(new Dictionary<OutPort, IPublisher>());
            _moduleStack.AddFirst(TopLevel);
        }

        private IDictionary<InPort, ISubscriber> Subscribers => _subscribersStack.First.Value;
        private IDictionary<OutPort, IPublisher> Publishers => _publishersStack.First.Value;
        private IModule CurrentLayout => _moduleStack.First.Value;

        ///<summary>
        /// Enters a copied module and establishes a scope that prevents internals to leak out and interfere with copies
        /// of the same module.
        /// We don't store the enclosing CopiedModule itself as state since we don't use it anywhere else than exit and enter
        /// </summary>
        private void EnterScope(CopiedModule enclosing)
        {
            _subscribersStack.AddFirst(new Dictionary<InPort, ISubscriber>());
            _publishersStack.AddFirst(new Dictionary<OutPort, IPublisher>());
            _moduleStack.AddFirst(enclosing.CopyOf);
        }

        /// <summary>
        /// Exits the scope of the copied module and propagates Publishers/Subscribers to the enclosing scope assigning
        /// them to the copied ports instead of the original ones (since there might be multiple copies of the same module
        /// leading to port identity collisions)
        /// We don't store the enclosing CopiedModule itself as state since we don't use it anywhere else than exit and enter
        /// </summary>
        private void ExitScope(CopiedModule enclosing)
        {
            var scopeSubscribers = Subscribers;
            var scopePublishers = Publishers;

            _subscribersStack.RemoveFirst();
            _publishersStack.RemoveFirst();
            _moduleStack.RemoveFirst();

            // When we exit the scope of a copied module,  pick up the Subscribers/Publishers belonging to exposed ports of
            // the original module and assign them to the copy ports in the outer scope that we will return to
            var inZip = enclosing.CopyOf.Shape.Inlets.Zip(enclosing.Shape.Inlets, (original, exposed) =>
                new KeyValuePair<Inlet, Inlet>(original, exposed));

            foreach (var kv in inZip)
                AssignPort(kv.Value, scopeSubscribers[kv.Key]);

            var outZip = enclosing.CopyOf.Shape.Outlets.Zip(enclosing.Shape.Outlets, (original, exposed) =>
                new KeyValuePair<Outlet, Outlet>(original, exposed));

            foreach (var kv in outZip)
                AssignPort(kv.Value, scopePublishers[kv.Key]);
        }

        public object Materialize()
        {
            if (TopLevel is EmptyModule)
                throw new InvalidOperationException("An empty module cannot be materialized (EmptyModule was given)");

            if (!TopLevel.IsRunnable)
                throw new InvalidOperationException("The top level module cannot be materialized because it has unconnected ports");

            try
            {
                return MaterializeModule(TopLevel, InitialAttributes.And(TopLevel.Attributes));
            }
            catch (Exception cause)
            {
                // PANIC!!! THE END OF THE MATERIALIZATION IS NEAR!
                // Cancels all intermediate Publishers and fails all intermediate Subscribers.
                // (This is an attempt to clean up after an exception during materialization)
                var ex = new MaterializationPanicException(cause);

                foreach (var subMap in _subscribersStack)
                    foreach (var subscriber in subMap.Values)
                    {
                        var subscribedType = subscriber.GetType().GetSubscribedType();
                        var publisher = typeof(ErrorPublisher<>).Instantiate(subscribedType, ex, string.Empty);

                        ((IPublisher)publisher).Subscribe(subscriber);
                    }

                foreach (var pubMap in _publishersStack)
                    foreach (var publisher in pubMap.Values)
                    {
                        var publishedType = publisher.GetType().GetPublishedType();
                        var subscribe = typeof(CancellingSubscriber<>).Instantiate(publishedType);

                        publisher.Subscribe((ISubscriber)subscribe);
                    }

                throw;
            }
        }

        protected virtual Attributes MergeAttributes(Attributes parent, Attributes current)
        {
            return parent.And(current);
        }

        protected void RegisterSource(IMaterializedValueSource materializedSource)
        {
            LinkedList<IMaterializedValueSource> sources;
            if (_materializedValueSources.TryGetValue(materializedSource.Computation, out sources))
                sources.AddFirst(materializedSource);
            else _materializedValueSources.Add(materializedSource.Computation, new LinkedList<IMaterializedValueSource>(new[] { materializedSource }));
        }

        protected object MaterializeModule(IModule module, Attributes effectiveAttributes)
        {
            var materializedValues = new Dictionary<IModule, object>();

            foreach (var submodule in module.SubModules)
            {
                var subEffectiveAttributes = MergeAttributes(effectiveAttributes, submodule.Attributes);
                GraphStageModule graphStageModule;
                Type stageType;
                if (((graphStageModule = submodule as GraphStageModule) != null) &&
                    (stageType = graphStageModule.Stage.GetType()).IsGenericType &&
                    stageType.GetGenericTypeDefinition() == typeof(MaterializedValueSource<>))
                {
                    var copy = new MaterializedValueSource<object>(graphStageModule.MaterializedValueComputation).CopySource();
                    RegisterSource(copy);
                    MaterializeAtomic(copy.Module, subEffectiveAttributes, materializedValues);
                }
                else if (submodule.IsAtomic)
                    MaterializeAtomic(submodule, subEffectiveAttributes, materializedValues);
                else if (submodule is CopiedModule)
                {
                    var copied = submodule as CopiedModule;
                    EnterScope(copied);
                    materializedValues.Add(copied, MaterializeModule(copied, subEffectiveAttributes));
                    ExitScope(copied);
                }
                else
                    materializedValues.Add(submodule, MaterializeComposite(submodule, subEffectiveAttributes));
            }

            return ResolveMaterialized(module.MaterializedValueComputation, materializedValues, "  ");
        }

        protected virtual object MaterializeComposite(IModule composite, Attributes effectiveAttributes)
        {
            return MaterializeModule(composite, effectiveAttributes);
        }

        protected abstract object MaterializeAtomic(IModule atomic, Attributes effectiveAttributes, IDictionary<IModule, object> materializedValues);

        private object ResolveMaterialized(StreamLayout.IMaterializedValueNode node, IDictionary<IModule, object> values, string indent)
        {
            object result;
            if (node is StreamLayout.Atomic)
            {
                var atomic = (StreamLayout.Atomic) node;
                values.TryGetValue(atomic.Module, out result);
            }
            else if (node is StreamLayout.Combine)
            {
                var combine = (StreamLayout.Combine) node;
                result = combine.Combinator(ResolveMaterialized(combine.Left, values, indent + "  "),
                    ResolveMaterialized(combine.Right, values, indent + "  "));
            }
            else if (node is StreamLayout.Transform)
            {
                var transform = (StreamLayout.Transform) node;
                result = transform.Transformator(ResolveMaterialized(transform.Node, values, indent + "  "));
            }
            else result = null;

            LinkedList<IMaterializedValueSource> sources;
            if (_materializedValueSources.TryGetValue(node, out sources))
            {
                _materializedValueSources.Remove(node);
                foreach (var source in sources)
                    source.SetValue(result);
            }

            return result;
        }

        protected void AssignPort(InPort inPort, ISubscriber subscriber)
        {
            Subscribers[inPort] = subscriber;
            // Interface (unconnected) ports of the current scope will be wired when exiting the scope
            if (!CurrentLayout.InPorts.Contains(inPort))
            {
                IPublisher publisher;
                if (Publishers.TryGetValue(CurrentLayout.Upstreams[inPort], out publisher))
                    publisher.Subscribe(subscriber);
            }
        }

        protected void AssignPort(OutPort outPort, IPublisher publisher)
        {
            Publishers[outPort] = publisher;
            // Interface (unconnected) ports of the current scope will be wired when exiting the scope
            if (!CurrentLayout.OutPorts.Contains(outPort))
            {
                ISubscriber subscriber;
                if (Subscribers.TryGetValue(CurrentLayout.Downstreams[outPort], out subscriber))
                    publisher.Subscribe(subscriber);
            }
        }
    }
}
