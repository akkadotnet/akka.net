//-----------------------------------------------------------------------
// <copyright file="StreamLayout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Annotations;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Util;
using Akka.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class StreamLayout
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly bool IsDebug = false;

        #region Materialized Value Node types

        /// <summary>
        /// TBD
        /// </summary>
        public interface IMaterializedValueNode
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Combine : IMaterializedValueNode
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Func<object, object, object> Combinator;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IMaterializedValueNode Left;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IMaterializedValueNode Right;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="combinator">TBD</param>
            /// <param name="left">TBD</param>
            /// <param name="right">TBD</param>
            public Combine(Func<object, object, object> combinator, IMaterializedValueNode left,
                IMaterializedValueNode right)
            {
                Combinator = combinator;
                Left = left;
                Right = right;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString() => $"Combine({Left}, {Right})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Atomic : IMaterializedValueNode
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IModule Module;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="module">TBD</param>
            public Atomic(IModule module)
            {
                Module = module;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString()
                => $"Atomic({Module.Attributes.GetNameOrDefault(Module.GetType().Name)}[{Module.GetHashCode()}])";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Transform : IMaterializedValueNode
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Func<object, object> Transformator;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IMaterializedValueNode Node;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="transformator">TBD</param>
            /// <param name="node">TBD</param>
            public Transform(Func<object, object> transformator, IMaterializedValueNode node)
            {
                Transformator = transformator;
                Node = node;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString() => $"Transform({Node})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Ignore : IMaterializedValueNode
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Ignore Instance = new Ignore();

            private Ignore()
            {
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString() => "Ignore";
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        /// <param name="level">TBD</param>
        /// <param name="shouldPrint">TBD</param>
        /// <param name="idMap">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        public static void Validate(IModule module, int level = 0, bool shouldPrint = false,
            IDictionary<object, int> idMap = null)
        {
            idMap = idMap ?? new Dictionary<object, int>();
            var currentId = 1;
            Func<int> ids = () => currentId++;
            Func<object, int> id = obj =>
            {
                if (!idMap.TryGetValue(obj, out int x))
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

            if (inset.Count != shape.Inlets.Count())
                problems.Add("shape has duplicate inlets " + ins(shape.Inlets));
            if (!inset.SetEquals(inPorts))
                problems.Add(
                    $"shape has extra {ins(inset.Except(inPorts))}, module has extra {ins(inPorts.Except(inset))}");

            var connectedInlets = inset.Intersect(module.Upstreams.Keys).ToArray();
            if (connectedInlets.Any())
                problems.Add("found connected inlets " + ins(connectedInlets));
            if (outset.Count != shape.Outlets.Count())
                problems.Add("shape has duplicate outlets " + outs(shape.Outlets));
            if (!outset.SetEquals(outPorts))
                problems.Add(
                    $"shape has extra {outs(outset.Except(outPorts))}, module has extra {outs(outPorts.Except(outset))}");

            var connectedOutlets = outset.Intersect(module.Downstreams.Keys).ToArray();
            if (connectedOutlets.Any())
                problems.Add("found connected outlets " + outs(connectedOutlets));

            var ups = module.Upstreams.ToImmutableHashSet();
            var ups2 = ups.Select(x => new KeyValuePair<OutPort, InPort>(x.Value, x.Key)).ToImmutableHashSet();
            var downs = module.Downstreams.ToImmutableHashSet();
            var inter = ups2.Intersect(downs);

            if (!downs.SetEquals(ups2))
                problems.Add($"inconsistent maps: ups {pairs(ups2.Except(inter))} downs {pairs(downs.Except(inter))}");

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

            if (!duplicateIn.IsEmpty)
                problems.Add("duplicate ports in submodules " + ins(duplicateIn));
            if (!duplicateOut.IsEmpty)
                problems.Add("duplicate ports in submodules " + outs(duplicateOut));
            if (!module.IsSealed && inset.Except(allIn).Any())
                problems.Add("foreign inlets " + ins(inset.Except(allIn)));
            if (!module.IsSealed && outset.Except(allOut).Any())
                problems.Add("foreign outlets " + outs(outset.Except(allOut)));

            var unIn = allIn.Except(inset).Except(module.Upstreams.Keys);
            if (unIn.Any() && !module.IsCopied)
                problems.Add("unconnected inlets " + ins(unIn));

            var unOut = allOut.Except(outset).Except(module.Downstreams.Keys);
            if (unOut.Any() && !module.IsCopied)
                problems.Add("unconnected outlets " + outs(unOut));

            var atomics = Atomics(module.MaterializedValueComputation);
            var graphValues =
                module.SubModules.SelectMany(
                    m => m is GraphModule ? ((GraphModule) m).MaterializedValueIds : Enumerable.Empty<IModule>());
            var nonExistent = atomics.Except(module.SubModules).Except(graphValues).Except(new[] {module});
            if (nonExistent.Any())
                problems.Add("computation refers to non-existent modules " + string.Join(", ", nonExistent));

            var print = shouldPrint || problems.Any();

            if (print)
            {
                var indent = string.Empty.PadLeft(level*2);
                Console.Out.WriteLine("{0}{1}({2}): {3} {4}", indent, typeof(StreamLayout).Name, shape, ins(inPorts),
                    outs(outPorts));
                foreach (var downstream in module.Downstreams)
                    Console.Out.WriteLine("{0}    {1} -> {2}", indent, outPort(downstream.Key), inPort(downstream.Value));
                foreach (var problem in problems)
                    Console.Out.WriteLine("{0}  -!- {1}", indent, problem);
            }

            foreach (var subModule in module.SubModules)
                Validate(subModule, level + 1, print, idMap);

            if (problems.Any() && !shouldPrint)
                throw new IllegalStateException(
                    $"module inconsistent, found {problems.Count} problems:\n - {string.Join("\n - ", problems)}");
        }

        private static ImmutableHashSet<IModule> Atomics(IMaterializedValueNode node)
        {
            if (node is Ignore)
                return ImmutableHashSet<IModule>.Empty;
            if (node is Transform)
                return Atomics(((Transform) node).Node);
            if (node is Atomic)
                return ImmutableHashSet.Create(((Atomic) node).Module);

            Combine c;
            if ((c = node as Combine) != null)
                return Atomics(c.Left).Union(Atomics(c.Right));
            throw new ArgumentException("Couldn't extract atomics for node " + node.GetType());
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class IgnorableMaterializedValueComposites
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="composition">TBD</param>
        /// <returns>TBD</returns>
        public static bool Apply(StreamLayout.IMaterializedValueNode composition)
        {
            if (composition is StreamLayout.Combine || composition is StreamLayout.Transform)
                return false;
            if (composition is StreamLayout.Ignore)
                return true;

            var atomic = composition as StreamLayout.Atomic;
            return atomic != null && Apply(atomic.Module);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        /// <returns>TBD</returns>
        public static bool Apply(IModule module)
        {
            if (module is AtomicModule || module is EmptyModule)
                return true;
            var copied = module as CopiedModule;
            if (copied != null)
                return Apply(copied.CopyOf);

            var composite = module as CompositeModule;
            if(composite != null)
                return Apply(composite.MaterializedValueComputation);

            var fused = module as FusedModule;
            return fused != null && Apply(fused.MaterializedValueComputation);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IModule : IComparable<IModule>
    {
        /// <summary>
        /// TBD
        /// </summary>
        Shape Shape { get; }

        /// <summary>
        /// Verify that the given Shape has the same ports and return a new module with that shape.
        /// Concrete implementations may throw UnsupportedOperationException where applicable.
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        IModule ReplaceShape(Shape shape);

        /// <summary>
        /// TBD
        /// </summary>
        IImmutableSet<InPort> InPorts { get; }
        /// <summary>
        /// TBD
        /// </summary>
        IImmutableSet<OutPort> OutPorts { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsRunnable { get; }

        /// <summary>
        /// TBD
        /// </summary>
        bool IsSink { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsSource { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsFlow { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsBidiFlow { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsAtomic { get; }
        /// <summary>
        /// TBD
        /// </summary>
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
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="T3">TBD</typeparam>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="mapFunc">TBD</param>
        /// <returns>TBD</returns>
        IModule TransformMaterializedValue<TMat, TMat2>(Func<TMat, TMat2> mapFunc);

        /// <summary>
        /// Creates a new Module which is this Module composed with <paramref name="that"/> Module.
        /// </summary>
        /// <param name="that">A Module to be composed with (cannot be itself)</param>
        /// <returns>A Module that represents the composition of this and <paramref name="that"/></returns>
        IModule Compose(IModule that);

        /// <summary>
        /// Creates a new Module which is this Module composed with <paramref name="that"/> Module,
        /// using the given function <paramref name="matFunc"/> to compose the materialized value of `this` with
        /// the materialized value of <paramref name="that"/>.
        /// </summary>
        /// <param name="that">A Module to be composed with (cannot be itself)</param>
        /// <param name="matFunc">A function which combines the materialized values</param>
        /// <typeparam name="T1">The type of the materialized value of this</typeparam>
        /// <typeparam name="T2">The type of the materialized value of <paramref name="that"/></typeparam>
        /// <typeparam name="T3">The type of the materialized value of the returned Module</typeparam>
        /// <returns>A Module that represents the composition of this and <paramref name="that"/></returns>
        IModule Compose<T1, T2, T3>(IModule that, Func<T1, T2, T3> matFunc);

        /// <summary>
        /// Creates a new Module which is this Module composed with <paramref name="that"/> Module.
        ///
        /// The difference to compose(that) is that this version completely ignores the materialized value
        /// computation of <paramref name="that"/> while the normal version executes the computation and discards its result.
        /// This means that this version must not be used for user-provided <paramref name="that"/> modules because users may
        /// transform materialized values only to achieve some side-effect; it can only be
        /// used where we know that there is no meaningful computation to be done (like for
        /// MaterializedValueSource).
        /// </summary>
        /// <param name="that">a Module to be composed with (cannot be itself)</param>
        /// <returns>a Module that represents the composition of this and <paramref name="that"/></returns>
        IModule ComposeNoMaterialized(IModule that);

        /// <summary>
        /// Creates a new Module which contains this Module
        /// </summary>
        /// <returns>TBD</returns>
        IModule Nest();

        // this cannot be set, since sets are changing ordering of modules
        // which must be kept for fusing to work
        /// <summary>
        /// TBD
        /// </summary>
        ImmutableArray<IModule> SubModules { get; }

        /// <summary>
        /// TBD
        /// </summary>
        bool IsSealed { get; }

        /// <summary>
        /// TBD
        /// </summary>
        IImmutableDictionary<OutPort, InPort> Downstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        IImmutableDictionary<InPort, OutPort> Upstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        IModule CarbonCopy();

        /// <summary>
        /// TBD
        /// </summary>
        Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        IModule WithAttributes(Attributes attributes);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class Module : IModule
    {
        private readonly Lazy<IImmutableSet<InPort>> _inports;
        private readonly Lazy<IImmutableSet<OutPort>> _outports;

        /// <summary>
        /// TBD
        /// </summary>
        protected Module()
        {
            _inports = new Lazy<IImmutableSet<InPort>>(() => ImmutableHashSet.CreateRange(Shape.Inlets.Cast<InPort>()));
            _outports =
                new Lazy<IImmutableSet<OutPort>>(() => ImmutableHashSet.CreateRange(Shape.Outlets.Cast<OutPort>()));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableSet<InPort> InPorts => _inports.Value;

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableSet<OutPort> OutPorts => _outports.Value;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsRunnable => InPorts.Count == 0 && OutPorts.Count == 0;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsSink => InPorts.Count == 1 && OutPorts.Count == 0;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsSource => InPorts.Count == 0 && OutPorts.Count == 1;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsFlow => InPorts.Count == 1 && OutPorts.Count == 1;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsBidiFlow => InPorts.Count == 2 && OutPorts.Count == 2;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsAtomic => !SubModules.Any();

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsCopied => false;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsFused => false;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <returns>TBD</returns>
        public virtual IModule Fuse(IModule other, OutPort from, InPort to)
            => Fuse<object, object, object>(other, from, to, Keep.Left);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="T3">TBD</typeparam>
        /// <param name="other">TBD</param>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <param name="matFunc">TBD</param>
        /// <returns>TBD</returns>
        public virtual IModule Fuse<T1, T2, T3>(IModule other, OutPort from, InPort to, Func<T1, T2, T3> matFunc)
            => Compose(other, matFunc).Wire(from, to);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public virtual IModule Wire(OutPort from, InPort to)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            if (!OutPorts.Contains(from))
            {
                var message = Downstreams.ContainsKey(from)
                    ? $"The output port [{from}] is already connected"
                    : $"The output port [{from}] is not part of underlying graph";
                throw new ArgumentException(message);
            }

            if (!InPorts.Contains(to))
            {
                var message = Upstreams.ContainsKey(to)
                    ? $"The input port [{to}] is already connected"
                    : $"The input port [{to}] is not part of underlying graph";
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="mapFunc">TBD</param>
        /// <returns>TBD</returns>
        public virtual IModule TransformMaterializedValue<TMat, TMat2>(Func<TMat, TMat2> mapFunc)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            return new CompositeModule(
                subModules: IsSealed ? ImmutableArray.Create(this as IModule) : SubModules,
                shape: Shape,
                downstreams: Downstreams,
                upstreams: Upstreams,
                materializedValueComputation: new StreamLayout.Transform(x => mapFunc((TMat) x), IsSealed
                    ? new StreamLayout.Atomic(this)
                    : MaterializedValueComputation),
                attributes: Attributes);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public virtual IModule Compose(IModule other) => Compose<object, object, object>(other, Keep.Left);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="T3">TBD</typeparam>
        /// <param name="other">TBD</param>
        /// <param name="matFunc">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public virtual IModule Compose<T1, T2, T3>(IModule other, Func<T1, T2, T3> matFunc)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            if (Equals(other, this))
                throw new ArgumentException(
                    "A module cannot be added to itself. You should pass a separate instance to compose().");
            if (SubModules.Contains(other))
                throw new ArgumentException(
                    "An existing submodule cannot be added again. All contained modules must be unique.");

            var modulesLeft = IsSealed ? ImmutableArray.Create<IModule>(this) : SubModules;
            var modulesRight = other.IsSealed ? ImmutableArray.Create(other) : other.SubModules;

            var matComputationLeft = IsSealed ? new StreamLayout.Atomic(this) : MaterializedValueComputation;
            var matComputationRight = other.IsSealed
                ? new StreamLayout.Atomic(other)
                : other.MaterializedValueComputation;

            StreamLayout.IMaterializedValueNode comp;

            if (Keep.IsLeft(matFunc) && IsIgnorable(matComputationRight)) comp = matComputationLeft;
            else if (Keep.IsRight(matFunc) && IsIgnorable(matComputationLeft)) comp = matComputationRight;
            else comp = new StreamLayout.Combine((x, y) => matFunc((T1)x, (T2)y), matComputationLeft, matComputationRight);
            
            return new CompositeModule(
                subModules: modulesLeft.Union(modulesRight).ToImmutableArray(),
                shape: new AmorphousShape(
                    Shape.Inlets.Concat(other.Shape.Inlets).ToImmutableArray(),
                    Shape.Outlets.Concat(other.Shape.Outlets).ToImmutableArray()),
                downstreams: Downstreams.AddRange(other.Downstreams),
                upstreams: Upstreams.AddRange(other.Upstreams),
                materializedValueComputation: comp,
                attributes: Attributes.None);
        }

        private bool IsIgnorable(StreamLayout.IMaterializedValueNode computation)
        {
            if (computation is StreamLayout.Atomic)
                return IsIgnorable(((StreamLayout.Atomic) computation).Module);

            if (computation is StreamLayout.Combine || computation is StreamLayout.Transform)
                return false;

            return computation is StreamLayout.Ignore;
        }

        private bool IsIgnorable(IModule module)
        {
            if (module is AtomicModule || module is EmptyModule) return true;
            if (module is CopiedModule) return IsIgnorable(((CopiedModule) module).CopyOf);
            if (module is CompositeModule) return IsIgnorable(((CompositeModule) module).MaterializedValueComputation);
            if (module is FusedModule) return IsIgnorable(((FusedModule) module).MaterializedValueComputation);

            throw new NotSupportedException($"Module of type {module.GetType()} is not supported by this method");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="that">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public IModule ComposeNoMaterialized(IModule that)
        {
            if (StreamLayout.IsDebug)
                StreamLayout.Validate(this);

            if (ReferenceEquals(this, that))
                throw new ArgumentException(
                    "A module cannot be added to itself. You should pass a separate instance to Compose().");
            if (SubModules.Contains(that))
                throw new ArgumentException(
                    "An existing submodule cannot be added again. All contained modules must be unique.");

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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsSealed => IsAtomic || IsCopied || IsFused || Attributes.AttributeList.Count() != 0;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual IImmutableDictionary<OutPort, InPort> Downstreams => ImmutableDictionary<OutPort, InPort>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual IImmutableDictionary<InPort, OutPort> Upstreams => ImmutableDictionary<InPort, OutPort>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual StreamLayout.IMaterializedValueNode MaterializedValueComputation => new StreamLayout.Atomic(this);

        /// <summary>
        /// TBD
        /// </summary>
        public abstract Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        public abstract IModule ReplaceShape(Shape shape);

        /// <summary>
        /// TBD
        /// </summary>
        public abstract ImmutableArray<IModule> SubModules { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract IModule CarbonCopy();

        /// <summary>
        /// TBD
        /// </summary>
        public abstract Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public abstract IModule WithAttributes(Attributes attributes);

        /// <inheritdoc/>
        public int CompareTo(IModule other) => GetHashCode().CompareTo(other.GetHashCode());
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class EmptyModule : Module
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly EmptyModule Instance = new EmptyModule();

        private EmptyModule()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape => ClosedShape.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsAtomic => false;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsRunnable => false;

        /// <summary>
        /// TBD
        /// </summary>
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation => StreamLayout.Ignore.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IModule ReplaceShape(Shape shape)
        {
            if (shape is ClosedShape)
                return this;

            throw new NotSupportedException("Cannot replace the shape of empty module");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public override IModule Compose(IModule other) => Compose<object,object,object>(other, Keep.Left);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="T3">TBD</typeparam>
        /// <param name="other">TBD</param>
        /// <param name="matFunc">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IModule Compose<T1, T2, T3>(IModule other, Func<T1, T2, T3> matFunc)
        {
            if (Keep.IsRight(matFunc))
                return other;

            if (Keep.IsLeft(matFunc))
            {
                // If "that" has a fully ignorable materialized value, we ignore it, otherwise we keep the side effect and
                // explicitly map to NotUsed
                var materialized =
                    IgnorableMaterializedValueComposites.Apply(other)
                        ? (StreamLayout.IMaterializedValueNode) StreamLayout.Ignore.Instance
                        : new StreamLayout.Transform(_ => NotUsed.Instance, other.MaterializedValueComputation);

                return new CompositeModule(other.IsSealed ? ImmutableArray.Create(other) : other.SubModules, other.Shape,
                    other.Downstreams, other.Upstreams, materialized, IsSealed ? Attributes.None : Attributes);
            }

            throw new NotSupportedException(
                "It is invalid to combine materialized value with EmptyModule except with Keep.Left or Keep.Right");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule Nest() => this;

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<IModule> SubModules { get; } = ImmutableArray<IModule>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => this;

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes => Attributes.None;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException("EmptyModule cannot carry attributes");
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class CopiedModule : Module
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="copyOf">TBD</param>
        public CopiedModule(Shape shape, Attributes attributes, IModule copyOf)
        {
            Shape = shape;
            Attributes = attributes;
            CopyOf = copyOf;
            SubModules = ImmutableArray.Create(copyOf);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<IModule> SubModules { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IModule CopyOf { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsCopied => true;

        /// <summary>
        /// TBD
        /// </summary>
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation
            => new StreamLayout.Atomic(CopyOf);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, CopyOf);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
        {
            if (!ReferenceEquals(attributes, Attributes))
                return new CopiedModule(Shape, attributes, CopyOf);
            return this;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{GetHashCode()} copy of {CopyOf}";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class CompositeModule : Module
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subModules">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="downstreams">TBD</param>
        /// <param name="upstreams">TBD</param>
        /// <param name="materializedValueComputation">TBD</param>
        /// <param name="attributes">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        public override IImmutableDictionary<InPort, OutPort> Upstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override IImmutableDictionary<OutPort, InPort> Downstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<IModule> SubModules { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new CompositeModule(SubModules, Shape, Downstreams, Upstreams, MaterializedValueComputation, attributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"\n  CompositeModule [{GetHashCode()}%08x]" +
                   $"\n  Name: {Attributes.GetNameOrDefault("unnamed")}" +
                    "\n  Modules:" +
                   $"\n    {string.Join("\n    ", SubModules.Select(m => m.Attributes.GetNameLifted() ?? m.ToString().Replace("\n", "\n    ")))}" +
                   $"\n  Downstreams: {string.Join("", Downstreams.Select(kvp => $"\n    {kvp.Key} -> {kvp.Value}"))}" +
                   $"\n  Upstreams: {string.Join("", Upstreams.Select(kvp => $"\n    {kvp.Key} -> {kvp.Value}"))}" +
                   $"\n  MatValue: {MaterializedValueComputation}";
        }
    }

    /// <summary>
    /// When fusing a <see cref="IGraph{TShape}"/> a part of the internal stage wirings are hidden within
    /// <see cref="GraphAssembly"/> objects that are
    /// optimized for high-speed execution. This structural information bundle contains
    /// the wirings in a more accessible form, allowing traversal from port to upstream
    /// or downstream port and from there to the owning module (or graph vertex).
    /// </summary>
    public sealed class StructuralInfoModule : Module
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subModules">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="downstreams">TBD</param>
        /// <param name="upstreams">TBD</param>
        /// <param name="inOwners">TBD</param>
        /// <param name="outOwners">TBD</param>
        /// <param name="materializedValues">TBD</param>
        /// <param name="materializedValueComputation">TBD</param>
        /// <param name="attributes">TBD</param>
        public StructuralInfoModule(ImmutableArray<IModule> subModules,
            Shape shape,
            IImmutableDictionary<OutPort, InPort> downstreams,
            IImmutableDictionary<InPort, OutPort> upstreams,
            IImmutableDictionary<InPort, IModule> inOwners, 
            IImmutableDictionary<OutPort, IModule> outOwners,
            IImmutableList<(IModule, StreamLayout.IMaterializedValueNode)> materializedValues,
            StreamLayout.IMaterializedValueNode materializedValueComputation,
            Attributes attributes)
        {
            InOwners = inOwners;
            OutOwners = outOwners;
            MaterializedValues = materializedValues;

            SubModules = subModules;
            Shape = shape;
            Downstreams = downstreams;
            Upstreams = upstreams;
            MaterializedValueComputation = materializedValueComputation;
            Attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }
        
        /// <summary>
        /// TBD
        /// </summary>
        public override IImmutableDictionary<OutPort, InPort> Downstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override IImmutableDictionary<InPort, OutPort> Upstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsFused { get; } = false;

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<IModule> SubModules { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<InPort, IModule> InOwners { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<OutPort, IModule> OutOwners { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableList<(IModule, StreamLayout.IMaterializedValueNode)> MaterializedValues { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override IModule ReplaceShape(Shape shape)
        {
            if (!ReferenceEquals(Shape, shape))
            {
                if(!Shape.HasSamePortsAs(shape))
                    throw new ArgumentException("StructuralInfoModule requires shape with same ports to replace", nameof(shape));
                return new StructuralInfoModule(SubModules, shape, Downstreams, Upstreams, InOwners, OutOwners,
                    MaterializedValues, MaterializedValueComputation, Attributes);
            }

            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        /// <summary>
        /// TBD
        /// </summary>
        public override IModule WithAttributes(Attributes attributes)
        {
            return new StructuralInfoModule(SubModules, Shape, Downstreams, Upstreams, InOwners, OutOwners,
                MaterializedValues, MaterializedValueComputation, attributes);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class FusedModule : Module
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subModules">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="downstreams">TBD</param>
        /// <param name="upstreams">TBD</param>
        /// <param name="materializedValueComputation">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="info">TBD</param>
        public FusedModule(
            ImmutableArray<IModule> subModules,
            Shape shape,
            IImmutableDictionary<OutPort, InPort> downstreams,
            IImmutableDictionary<InPort, OutPort> upstreams,
            StreamLayout.IMaterializedValueNode materializedValueComputation,
            Attributes attributes,
            StructuralInfoModule info)
        {
            SubModules = subModules;
            Shape = shape;
            Downstreams = downstreams;
            Upstreams = upstreams;
            MaterializedValueComputation = materializedValueComputation;
            Attributes = attributes;
            Info = info;
        }


        /// <summary>
        /// TBD
        /// </summary>
        public StructuralInfoModule Info { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsFused => true;

        /// <summary>
        /// TBD
        /// </summary>
        public override IImmutableDictionary<OutPort, InPort> Downstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override IImmutableDictionary<InPort, OutPort> Upstreams { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<IModule> SubModules { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            =>
                new FusedModule(SubModules, Shape, Downstreams, Upstreams, MaterializedValueComputation, attributes,
                    Info);

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"\n  Name: {Attributes.GetNameOrDefault("unnamed")}" +
                   "\n  Modules:" +
                   $"\n    {string.Join("\n    ", SubModules.Select(m => m.Attributes.GetNameLifted() ?? m.ToString().Replace("\n", "\n    ")))}" +
                   $"\n  Downstreams: {string.Join("", Downstreams.Select(kvp => $"\n    {kvp.Key} -> {kvp.Value}"))}" +
                   $"\n  Upstreams: {string.Join("", Upstreams.Select(kvp => $"\n    {kvp.Key} -> {kvp.Value}"))}" +
                   $"\n  MatValue: {MaterializedValueComputation}";
        }
    }

    /// <summary>
    /// This is the only extension point for the sealed type hierarchy: composition
    /// (i.e. the module tree) is managed strictly within this file, only leaf nodes
    /// may be declared elsewhere.
    /// </summary>
    public abstract class AtomicModule : Module
    {
        /// <summary>
        /// TBD
        /// </summary>
        public sealed override ImmutableArray<IModule> SubModules => ImmutableArray<IModule>.Empty;
        /// <summary>
        /// TBD
        /// </summary>
        public sealed override IImmutableDictionary<OutPort, InPort> Downstreams => base.Downstreams;
        /// <summary>
        /// TBD
        /// </summary>
        public sealed override IImmutableDictionary<InPort, OutPort> Upstreams => base.Upstreams;
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// This is a transparent processor that shall consume as little resources as
    /// possible. Due to the possibility of receiving uncoordinated inputs from both
    /// downstream and upstream, this needs an atomic state machine which looks a
    /// little like this:
    /// 
    /// <![CDATA[
    ///            +--------------+      (2)    +------------+
    ///            |     null     | ----------> | Subscriber |
    ///            +--------------+             +------------+
    ///                   |                           |
    ///               (1) |                           | (1)
    ///                  \|/                         \|/
    ///            +--------------+      (2)    +------------+ --\
    ///            | Subscription | ----------> |    Both    |    | (4)
    ///            +--------------+             +------------+ <-/
    ///                   |                           |
    ///               (3) |                           | (3)
    ///                  \|/                         \|/
    ///            +--------------+      (2)    +------------+ --\
    ///            |   Publisher  | ----------> |   Inert    |    | (4, *)
    ///            +--------------+             +------------+ <-/
    /// ]]>
    /// The idea is to keep the major state in only one atomic reference. The actions
    /// that can happen are:
    /// 
    ///  (1) onSubscribe
    ///  (2) subscribe
    ///  (3) onError / onComplete
    ///  (4) onNext
    ///      (*) Inert can be reached also by cancellation after which onNext is still fine
    ///          so we just silently ignore possible spec violations here
    /// 
    /// Any event that occurs in a state where no matching outgoing arrow can be found
    /// is a spec violation, leading to the shutdown of this processor (meaning that
    /// the state is updated such that all following actions match that of a failed
    /// Publisher or a cancelling Subscriber, and the non-guilty party is informed if
    /// already connected).
    /// 
    /// request() can only be called after the Subscriber has received the Subscription
    /// and that also means that onNext() will only happen after having transitioned into
    /// the Both state as well. The Publisher state means that if the real
    /// Publisher terminates before we get the Subscriber, we can just forget about the
    /// real one and keep an already finished one around for the Subscriber.
    /// 
    /// The Subscription that is offered to the Subscriber must cancel the original
    /// Publisher if things go wrong (like `request(0)` coming in from downstream) and
    /// it must ensure that we drop the Subscriber reference when `cancel` is invoked.
    /// </summary>
    [InternalApi]
    public sealed class VirtualProcessor<T> : AtomicReference<object>, IProcessor<T, T>
    {
        private const string NoDemand = "spec violation: OnNext was signaled from upstream without demand";

        #region internal classes

        private sealed class Inert
        {
            public static readonly ISubscriber<T> Subscriber = new CancellingSubscriber<T>();

            public static readonly Inert Instance = new Inert();

            private Inert()
            {
            }
        }

        private sealed class Both
        {
            public Both(ISubscriber<T> subscriber)
            {
                Subscriber = subscriber;
            }

            public ISubscriber<T> Subscriber { get; }

        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <exception cref="Exception">TBD</exception>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            if (subscriber == null)
            {
                var ex = ReactiveStreamsCompliance.SubscriberMustNotBeNullException;
                try
                {
                    TrySubscribe(Inert.Subscriber);
                }
                finally
                {
                    // must throw ArgumentNullEx, rule 2:13
                    throw ex;
                }
            }

            TrySubscribe(subscriber);
        }

        private void TrySubscribe(ISubscriber<T> subscriber)
        {
            if (Value == null)
            {
                if (!CompareAndSet(null, subscriber))
                    TrySubscribe(subscriber);
                return;
            }

            var subscription = Value as ISubscription;
            if (subscription != null)
            {
                if (CompareAndSet(subscription, new Both(subscriber)))
                    EstablishSubscription(subscriber, subscription);
                else
                    TrySubscribe(subscriber);

                return;
            }

            var publisher = Value as IPublisher<T>;
            if (publisher != null)
            {
                if (CompareAndSet(publisher, Inert.Instance))
                    publisher.Subscribe(subscriber);
                else
                    TrySubscribe(subscriber);

                return;
            }

            ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "VirtualProcessor");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        /// <exception cref="Exception">TBD</exception>
        public void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null)
            {
                var ex = ReactiveStreamsCompliance.SubscriptionMustNotBeNullException;
                try
                {
                    TryOnSubscribe(new ErrorPublisher<T>(ex, "failed-VirtualProcessor"), subscription);
                }
                finally
                {
                    // must throw ArgumentNullEx, rule 2:13
                    throw ex;
                }
            }

            TryOnSubscribe(subscription, subscription);
        }

        private void TryOnSubscribe(object obj, ISubscription s)
        {
            if (Value == null)
            {
                if (!CompareAndSet(null, obj))
                    TryOnSubscribe(obj, s);
                return;
            }

            var subscriber = Value as ISubscriber<T>;
            if (subscriber != null)
            {
                var subscription = obj as ISubscription;
                if (subscription != null)
                {
                    if (CompareAndSet(subscriber, new Both(subscriber)))
                        EstablishSubscription(subscriber, subscription);
                    else
                        TryOnSubscribe(obj, s);

                    return;
                }

                var publisher = Value as IPublisher<T>;
                if (publisher != null)
                {
                    var inert = GetAndSet(Inert.Instance);
                    if (inert != Inert.Instance)
                        publisher.Subscribe(subscriber);
                    return;
                }

                return;
            }

            // spec violation
            ReactiveStreamsCompliance.TryCancel(s);
        }

        private void EstablishSubscription(ISubscriber<T> subscriber, ISubscription subscription)
        {
            var wrapped = new WrappedSubscription(subscription, this);
            try
            {
                subscriber.OnSubscribe(wrapped);
                // Requests will be only allowed once onSubscribe has returned to avoid reentering on an onNext before
                // onSubscribe completed
                wrapped.UngateDemandAndRequestBuffered();
            }
            catch (Exception ex)
            {
                Value = Inert.Instance;
                ReactiveStreamsCompliance.TryCancel(subscription);
                ReactiveStreamsCompliance.TryOnError(subscriber, ex);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <exception cref="Exception">TBD</exception>
        public void OnError(Exception cause)
        {
            /*
            * `ex` is always a reasonable Throwable that we should communicate downstream,
            * but if `t` was `null` then the spec requires us to throw an NPE (which `ex`
            * will be in this case).
            */
            var ex = cause ?? ReactiveStreamsCompliance.ExceptionMustNotBeNullException;

            while (true)
            {
                if (Value == null)
                {
                    if (!CompareAndSet(null, new ErrorPublisher<T>(ex, "failed-VirtualProcessor")))
                        continue;
                    if (cause == null)
                        throw ex;
                    return;
                }

                var subscription = Value as ISubscription;
                if (subscription != null)
                {
                    if (!CompareAndSet(subscription, new ErrorPublisher<T>(ex, "failed-VirtualProcessor")))
                        continue;
                    if (cause == null)
                        throw ex;
                    return;
                }

                var both = Value as Both;
                if (both != null)
                {
                    Value = Inert.Instance;
                    try
                    {
                        ReactiveStreamsCompliance.TryOnError(both.Subscriber, ex);
                    }
                    finally
                    {
                        // must throw ArgumentNullEx, rule 2:13
                        if (cause == null)
                            throw ex;
                    }

                    return;
                }

                var subscriber = Value as ISubscriber<T>;
                if (subscriber != null)
                {
                    // spec violation
                    var inert = GetAndSet(Inert.Instance);
                    if (inert != Inert.Instance)
                        new ErrorPublisher<T>(ex, "failed-VirtualProcessor").Subscribe(subscriber);

                    return;
                }

                // spec violation or cancellation race, but nothing we can do
                return;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void OnComplete()
        {
            while (true)
            {
                if (Value == null)
                {
                    if (!CompareAndSet(null, EmptyPublisher<T>.Instance))
                        continue;

                    return;
                }

                var subscription = Value as ISubscription;
                if (subscription != null)
                {
                    if (!CompareAndSet(subscription, EmptyPublisher<T>.Instance))
                        continue;

                    return;
                }

                var both = Value as Both;
                if (both != null)
                {
                    Value = Inert.Instance;
                    ReactiveStreamsCompliance.TryOnComplete(both.Subscriber);
                    return;
                }

                var subscriber = Value as ISubscriber<T>;
                if (subscriber != null)
                {
                    // spec violation
                    Value = Inert.Instance;
                    EmptyPublisher<T>.Instance.Subscribe(subscriber);
                    return;
                }

                // spec violation or cancellation race, but nothing we can do
                return;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        /// <exception cref="Exception">TBD</exception>
        /// <exception cref="IllegalStateException">TBD</exception>
        public void OnNext(T element)
        {
            if (element == null)
            {
                var ex = ReactiveStreamsCompliance.ElementMustNotBeNullException;

                while (true)
                {
                    if (Value == null || Value is ISubscription)
                    {
                        if (!CompareAndSet(Value, new ErrorPublisher<T>(ex, "failed-VirtualProcessor")))
                            continue;

                        break;
                    }

                    var subscriber = Value as ISubscriber<T>;
                    if (subscriber != null)
                    {
                        try
                        {
                            subscriber.OnError(ex);
                        }
                        finally
                        {
                            Value = Inert.Instance;
                        }
                        break;
                    }

                    var both = Value as Both;
                    if (both != null)
                    {
                        try
                        {
                            both.Subscriber.OnError(ex);
                        }
                        finally
                        {
                            Value = Inert.Instance;
                        }
                    }

                    // spec violation or cancellation race, but nothing we can do
                    break;
                }

                // must throw ArgumentNullEx, rule 2:13
                throw ex;
            }

            while (true)
            {
                var both = Value as Both;
                if (both != null)
                {
                    try
                    {
                        both.Subscriber.OnNext(element);
                        return;
                    }
                    catch (Exception e)
                    {
                        Value = Inert.Instance;
                        throw new IllegalStateException(
                            "Subscriber threw exception, this is in violation of rule 2:13", e);
                    }
                }

                var subscriber = Value as ISubscriber<T>;
                if (subscriber != null)
                {
                    // spec violation
                    var ex = new IllegalStateException(NoDemand);
                    var inert = GetAndSet(Inert.Instance);
                    if (inert != Inert.Instance)
                        new ErrorPublisher<T>(ex, "failed-VirtualProcessor").Subscribe(subscriber);
                    throw ex;
                }

                if (Value == Inert.Instance || Value is IPublisher<T>)
                {
                    // nothing to be done
                    return;
                }

                var publisher = new ErrorPublisher<T>(new IllegalStateException(NoDemand), "failed-VirtualPublisher");
                if (!CompareAndSet(Value, publisher))
                    continue;
                throw publisher.Cause;
            }
        }

        private interface ISubscriptionState
        {
            long Demand { get; }
        }

        private sealed class PassThrough : ISubscriptionState
        {
            public static PassThrough Instance { get; } = new PassThrough();

            private PassThrough() { }

            public long Demand { get; } = 0;
        }

        private sealed class Buffering : ISubscriptionState
        {
            public Buffering(long demand)
            {
                Demand = demand;
            }

            public long Demand { get; }
        }

        private sealed class WrappedSubscription : AtomicReference<ISubscriptionState>, ISubscription
        {
            private static readonly Buffering NoBufferedDemand = new Buffering(0);
            private readonly ISubscription _real;
            private readonly VirtualProcessor<T> _processor;
            
            public WrappedSubscription(ISubscription real, VirtualProcessor<T> processor) : base(NoBufferedDemand)
            {
                _real = real;
                _processor = processor;
            }

            public void Request(long n)
            {
                if (n < 1)
                {
                    ReactiveStreamsCompliance.TryCancel(_real);
                    var value = _processor.GetAndSet(Inert.Instance);
                    var both = value as Both;
                    if (both != null)
                        ReactiveStreamsCompliance.RejectDueToNonPositiveDemand(both.Subscriber);
                    else if (value == Inert.Instance)
                    {
                        // another failure has won the race
                    }
                    else
                    {
                        // this cannot possibly happen, but signaling errors is impossible at this point
                    }
                }
                else
                {
                    // NOTE: At this point, batched requests might not have been dispatched, i.e. this can reorder requests.
                    // This does not violate the Spec though, since we are a "Processor" here and although we, in reality,
                    // proxy downstream requests, it is virtually *us* that emit the requests here and we are free to follow
                    // any pattern of emitting them.
                    // The only invariant we need to keep is to never emit more requests than the downstream emitted so far.

                    while (true)
                    {
                        var current = Value;
                        if (current == PassThrough.Instance)
                        {
                            _real.Request(n);
                            break;
                        }
                        if (!CompareAndSet(current, new Buffering(current.Demand + n)))
                            continue;
                        break;
                    }
                }
            }

            public void Cancel()
            {
                _processor.Value = Inert.Instance;
                _real.Cancel();
            }

            public void UngateDemandAndRequestBuffered()
            {
                // Ungate demand
                var requests = GetAndSet(PassThrough.Instance).Demand;
                // And request buffered demand
                if(requests > 0)
                    _real.Request(requests);
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// The implementation of <see cref="Sink.AsPublisher{T}"/> needs to offer a <see cref="IPublisher{T}"/> that
    /// defers to the upstream that is connected during materialization. This would
    /// be trivial if it were not for materialized value computations that may even
    /// spawn the code that does <see cref="IPublisher{T}.Subscribe"/> in a Future, running concurrently
    /// with the actual materialization. Therefore we implement a minimal shell here
    /// that plugs the downstream and the upstream together as soon as both are known.
    /// Using a VirtualProcessor would technically also work, but it would defeat the
    /// purpose of subscription timeoutsï¿½the subscription would always already be
    /// established from the Actorï¿½s perspective, regardless of whether a downstream
    /// will ever be connected.
    /// 
    /// One important consideration is that this <see cref="IPublisher{T}"/> must not retain a reference
    /// to the <see cref="ISubscriber{T}"/> after having hooked it up with the real <see cref="IPublisher{T}"/>, hence
    /// the use of `Inert.subscriber` as a tombstone.
    /// </summary>
    internal sealed class VirtualPublisher<T> : AtomicReference<object>, IPublisher<T>, IUntypedVirtualPublisher
    {
        #region internal classes

        private sealed class Inert
        {
            public static readonly ISubscriber<T> Subscriber = new CancellingSubscriber<T>();

            public static readonly Inert Instance = new Inert();

            private Inert()
            {
            }
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);

            while (true)
            {
                if (Value == null)
                {
                    if (!CompareAndSet(null, subscriber))
                        continue;
                    return;
                }

                var publisher = Value as IPublisher<T>;
                if (publisher != null)
                {
                    if (CompareAndSet(publisher, Inert.Subscriber))
                    {
                        publisher.Subscribe(subscriber);
                        return;
                    }
                    continue;
                }

                ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Sink.AsPublisher(fanout = false)");
                return;
            }
        }

        void IUntypedVirtualPublisher.Subscribe(IUntypedSubscriber subscriber)
            => Subscribe(UntypedSubscriber.ToTyped<T>(subscriber));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        public void RegisterPublisher(IUntypedPublisher publisher)
            => RegisterPublisher(UntypedPublisher.ToTyped<T>(publisher));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        public void RegisterPublisher(IPublisher<T> publisher)
        {
            while (true)
            {
                if (Value == null)
                {
                    if (!CompareAndSet(null, publisher))
                        continue;
                    return;
                }

                var subscriber = Value as ISubscriber<T>;
                if (subscriber != null)
                {
                    Value = Inert.Instance;
                    publisher.Subscribe(subscriber);
                    return;
                }

                throw new IllegalStateException("internal error");
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public abstract class MaterializerSession
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly bool IsDebug = false;

        /// <summary>
        /// TBD
        /// </summary>
        public class MaterializationPanicException : Exception
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MaterializationPanicException"/> class.
            /// </summary>
            /// <param name="innerException">The exception that is the cause of the current exception.</param>
            public MaterializationPanicException(Exception innerException)
                : base("Materialization aborted.", innerException)
            {
            }

#if SERIALIZATION
            /// <summary>
            /// Initializes a new instance of the <see cref="MaterializationPanicException" /> class.
            /// </summary>
            /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
            /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
            protected MaterializationPanicException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
#endif
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly IModule TopLevel;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly Attributes InitialAttributes;

        private readonly LinkedList<IDictionary<InPort, object>> _subscribersStack =
            new LinkedList<IDictionary<InPort, object>>();

        private readonly LinkedList<IDictionary<OutPort, IUntypedPublisher>> _publishersStack =
            new LinkedList<IDictionary<OutPort, IUntypedPublisher>>();

        private readonly LinkedList<IDictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>>>
            _materializedValueSources =
                new LinkedList<IDictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>>>();

        /// <summary>
        /// Please note that this stack keeps track of the scoped modules wrapped in CopiedModule but not the CopiedModule
        /// itself. The reason is that the CopiedModule itself is only needed for the enterScope and exitScope methods but
        /// not elsewhere. For this reason they are just simply passed as parameters to those methods.
        ///
        /// The reason why the encapsulated (copied) modules are stored as mutable state to save subclasses of this class
        /// from passing the current scope around or even knowing about it.
        /// </summary>
        private readonly LinkedList<IModule> _moduleStack = new LinkedList<IModule>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="topLevel">TBD</param>
        /// <param name="initialAttributes">TBD</param>
        protected MaterializerSession(IModule topLevel, Attributes initialAttributes)
        {
            TopLevel = topLevel;
            InitialAttributes = initialAttributes;
            _subscribersStack.AddFirst(new Dictionary<InPort, object>());
            _publishersStack.AddFirst(new Dictionary<OutPort, IUntypedPublisher>());
            _materializedValueSources.AddFirst(
                new Dictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>>());
            _moduleStack.AddFirst(TopLevel);
        }

        private IDictionary<InPort, object> Subscribers => _subscribersStack.First.Value;
        private IDictionary<OutPort, IUntypedPublisher> Publishers => _publishersStack.First.Value;
        private IModule CurrentLayout => _moduleStack.First.Value;

        private IDictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>> MaterializedValueSource => _materializedValueSources.First.Value;
        ///<summary>
        /// Enters a copied module and establishes a scope that prevents internals to leak out and interfere with copies
        /// of the same module.
        /// We don't store the enclosing CopiedModule itself as state since we don't use it anywhere else than exit and enter
        /// </summary>
        /// <param name="enclosing">TBD</param>
        protected void EnterScope(CopiedModule enclosing)
        {
            if(IsDebug)
                Console.WriteLine($"entering scope [{GetHashCode()}%08x]");
            _subscribersStack.AddFirst(new Dictionary<InPort, object>());
            _publishersStack.AddFirst(new Dictionary<OutPort, IUntypedPublisher>());
            _materializedValueSources.AddFirst(new Dictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>>());
            _moduleStack.AddFirst(enclosing.CopyOf);
        }

        /// <summary>
        /// Exits the scope of the copied module and propagates Publishers/Subscribers to the enclosing scope assigning
        /// them to the copied ports instead of the original ones (since there might be multiple copies of the same module
        /// leading to port identity collisions)
        /// We don't store the enclosing CopiedModule itself as state since we don't use it anywhere else than exit and enter
        /// </summary>
        /// <param name="enclosing">TBD</param>
        protected void ExitScope(CopiedModule enclosing)
        {
            var scopeSubscribers = Subscribers;
            var scopePublishers = Publishers;

            _subscribersStack.RemoveFirst();
            _publishersStack.RemoveFirst();
            _materializedValueSources.RemoveFirst();
            _moduleStack.RemoveFirst();

            if(IsDebug)
                Console.WriteLine($"   subscribers = {scopeSubscribers}\n publishers = {scopePublishers}");

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

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
        public object Materialize()
        {
            if(IsDebug)
                Console.WriteLine($"beginning materialization of {TopLevel}");

            if (TopLevel is EmptyModule)
                throw new InvalidOperationException("An empty module cannot be materialized (EmptyModule was given)");

            if (!TopLevel.IsRunnable)
                throw new InvalidOperationException(
                    "The top level module cannot be materialized because it has unconnected ports");

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
                    foreach (var value in subMap.Values)
                    {
                        var subscriber = value as IUntypedSubscriber;
                        if (subscriber != null)
                        {
                            var subscribedType = UntypedSubscriber.ToTyped(subscriber).GetType().GetSubscribedType();
                            var publisher = typeof(ErrorPublisher<>).Instantiate(subscribedType, ex, string.Empty);

                            UntypedPublisher.FromTyped(publisher).Subscribe(subscriber);
                            continue;
                        }

                        var virtualPublisher = value as IUntypedVirtualPublisher;
                        if (virtualPublisher != null)
                        {
                            var publishedType =
                                UntypedVirtualPublisher.ToTyped(virtualPublisher).GetType().GetPublishedType();
                            var publisher = typeof(ErrorPublisher<>).Instantiate(publishedType, ex, string.Empty);
                            virtualPublisher.RegisterPublisher(UntypedPublisher.FromTyped(publisher));
                        }
                    }

                foreach (var pubMap in _publishersStack)
                    foreach (var publisher in pubMap.Values)
                    {
                        var publishedType = UntypedPublisher.ToTyped(publisher).GetType().GetPublishedType();
                        var subscriber = typeof(CancellingSubscriber<>).Instantiate(publishedType);

                        publisher.Subscribe(UntypedSubscriber.FromTyped(subscriber));
                    }

                throw;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="parent">TBD</param>
        /// <param name="current">TBD</param>
        /// <returns>TBD</returns>
        protected virtual Attributes MergeAttributes(Attributes parent, Attributes current) => parent.And(current);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="materializedSource">TBD</param>
        protected void RegisterSource(IMaterializedValueSource materializedSource)
        {
            if (IsDebug) Console.WriteLine($"Registering source {materializedSource}");

            if (MaterializedValueSource.TryGetValue(materializedSource.Computation, out var list))
                list.AddFirst(materializedSource);
            else
                MaterializedValueSource.Add(materializedSource.Computation,
                    new LinkedList<IMaterializedValueSource>(new[] {materializedSource}));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        /// <param name="effectiveAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected object MaterializeModule(IModule module, Attributes effectiveAttributes)
        {
            var materializedValues = new Dictionary<IModule, object>();

            if(IsDebug)
                Console.WriteLine($"entering module {module.GetHashCode()}%08x] ({module.GetType().Name})");

            foreach (var submodule in module.SubModules)
            {
                var subEffectiveAttributes = MergeAttributes(effectiveAttributes, submodule.Attributes);

                var atomic = submodule as AtomicModule;
                if (atomic != null)
                    MaterializeAtomic(atomic, subEffectiveAttributes, materializedValues);
                else if (submodule is CopiedModule)
                {
                    var copied = submodule as CopiedModule;
                    EnterScope(copied);
                    materializedValues.Add(copied, MaterializeModule(copied, subEffectiveAttributes));
                    ExitScope(copied);
                }
                else if(submodule is CompositeModule || submodule is FusedModule)
                    materializedValues.Add(submodule, MaterializeComposite(submodule, subEffectiveAttributes));
                
                //EmptyModule, nothing to do or say
            }

            if (IsDebug)
            {
                Console.WriteLine($"resolving module [{module.GetHashCode()}%08x] computation {module.MaterializedValueComputation}");
                Console.WriteLine($"  matValSrc = {MaterializedValueSource}");
                Console.WriteLine($"  matVals = {materializedValues}");
            }

            var resolved = ResolveMaterialized(module.MaterializedValueComputation, materializedValues, 2);
            while (MaterializedValueSource.Count != 0)
            {
                var node = MaterializedValueSource.Keys.First();
                if(IsDebug)
                    Console.WriteLine($"  delayed computation of {node}");
                ResolveMaterialized(node, materializedValues, 4);
            }
            if(IsDebug)
                Console.WriteLine($"exiting module [{module.GetHashCode()}%08x]");
            return resolved;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="composite">TBD</param>
        /// <param name="effectiveAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected virtual object MaterializeComposite(IModule composite, Attributes effectiveAttributes)
            => MaterializeModule(composite, effectiveAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="atomic">TBD</param>
        /// <param name="effectiveAttributes">TBD</param>
        /// <param name="materializedValues">TBD</param>
        /// <returns>TBD</returns>
        protected abstract object MaterializeAtomic(AtomicModule atomic, Attributes effectiveAttributes,
            IDictionary<IModule, object> materializedValues);

        private object ResolveMaterialized(StreamLayout.IMaterializedValueNode node, IDictionary<IModule, object> values,
            int spaces)
        {
            var indent = Enumerable.Repeat(" ", spaces).Aggregate("", (s, s1) => s + s1);
            if (IsDebug)
                Console.WriteLine($"{indent}{node}");
            object result;
            if (node is StreamLayout.Atomic)
            {
                var atomic = (StreamLayout.Atomic) node;
                values.TryGetValue(atomic.Module, out result);
            }
            else if (node is StreamLayout.Combine)
            {
                var combine = (StreamLayout.Combine) node;
                result = combine.Combinator(ResolveMaterialized(combine.Left, values, spaces + 2),
                    ResolveMaterialized(combine.Right, values, spaces + 2));
            }
            else if (node is StreamLayout.Transform)
            {
                var transform = (StreamLayout.Transform) node;
                result = transform.Transformator(ResolveMaterialized(transform.Node, values, spaces + 2));
            }
            else
                result = NotUsed.Instance;

            if (IsDebug)
                Console.WriteLine($"{indent}result = {result}");

            if (MaterializedValueSource.TryGetValue(node, out var sources))
            {
                if (IsDebug)
                    Console.WriteLine($"{indent}triggering sources {sources}");
                MaterializedValueSource.Remove(node);
                foreach (var source in sources)
                    source.SetValue(result);
            }

            return result;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inPort">TBD</param>
        /// <param name="subscriberOrVirtual">TBD</param>
        protected void AssignPort(InPort inPort, object subscriberOrVirtual)
        {
            Subscribers[inPort] = subscriberOrVirtual;
            
            // Interface (unconnected) ports of the current scope will be wired when exiting the scope
            if (CurrentLayout.Upstreams.TryGetValue(inPort, out var outPort))
                if (Publishers.TryGetValue(outPort, out var publisher))
                    DoSubscribe(publisher, subscriberOrVirtual);
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outPort">TBD</param>
        /// <param name="publisher">TBD</param>
        protected void AssignPort(OutPort outPort, IUntypedPublisher publisher)
        {
            Publishers[outPort] = publisher;
            // Interface (unconnected) ports of the current scope will be wired when exiting the scope
            if (CurrentLayout.Downstreams.TryGetValue(outPort, out var inPort))
                if (Subscribers.TryGetValue(inPort, out var subscriber))
                    DoSubscribe(publisher, subscriber);
        }

        private void DoSubscribe(IUntypedPublisher publisher, object subscriberOrVirtual)
        {
            var subscriber = subscriberOrVirtual as IUntypedSubscriber;
            if (subscriber != null)
            {
                publisher.Subscribe(subscriber);
                return;
            }

            var virtualPublisher = subscriberOrVirtual as IUntypedVirtualPublisher;
            if (virtualPublisher != null)
            {
                virtualPublisher.RegisterPublisher(UntypedPublisher.FromTyped(publisher));
                return;
            }

            publisher.Subscribe(UntypedSubscriber.FromTyped(subscriberOrVirtual));
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal interface IProcessorModule
    {
        /// <summary>
        /// TBD
        /// </summary>
        Inlet In { get; }
        
        /// <summary>
        /// TBD
        /// </summary>
        Outlet Out { get; }

        /// <summary>
        /// TBD
        /// </summary>
        (object, object) CreateProcessor();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    [InternalApi]
    public sealed class ProcessorModule<TIn, TOut, TMat> : AtomicModule, IProcessorModule
    {
        private readonly Func<(IProcessor<TIn, TOut>, TMat)> _createProcessor;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="createProcessor">TBD</param>
        /// <param name="attributes">TBD</param>
        public ProcessorModule(Func<(IProcessor<TIn, TOut>, TMat)> createProcessor, Attributes attributes = null)
        {
            _createProcessor = createProcessor;
            Attributes = attributes ?? DefaultAttributes.Processor;
            Shape = new FlowShape<TIn, TOut>((Inlet<TIn>)In, (Outlet<TOut>)Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet In { get; } = new Inlet<TIn>("ProcessorModule.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet Out { get; } = new Outlet<TOut>("ProcessorModule.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IModule ReplaceShape(Shape shape)
        {
            if(shape != Shape)
                throw new NotSupportedException("Cannot replace the shape of a FlowModule");
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy() => WithAttributes(Attributes);

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes) => new ProcessorModule<TIn, TOut, TMat>(_createProcessor, attributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public (object, object) CreateProcessor()
        {
            var result = _createProcessor();
            return (result.Item1, result.Item2);
        }
    }
}
