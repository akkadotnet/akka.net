//-----------------------------------------------------------------------
// <copyright file="GraphAssembly.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Stage;
using static Akka.Streams.Implementation.Fusing.GraphInterpreter;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// A GraphAssembly represents a small stream processing graph to be executed by the interpreter. Instances of this
    /// class **must not** be mutated after construction.
    /// 
    /// The array <see cref="OriginalAttributes"/> may contain the attribute information of the original atomic module, otherwise
    /// it must contain a none (otherwise the enclosing module could not overwrite attributes defined in this array).
    /// 
    /// The arrays <see cref="Inlets"/> and <see cref="Outlets"/> correspond to the notion of a *connection* in the <see cref="GraphInterpreter"/>. Each slot
    /// *i* contains the input and output port corresponding to connection *i*. Slots where the graph is not closed (i.e.
    /// ports are exposed to the external world) are marked with null values. For example if an input port p is
    /// exposed, then Outlets[p] will contain a null.
    ///
    /// The arrays <see cref="InletOwners"/> and <see cref="OutletOwners"/> are lookup tables from a connection id(the index of the slot)
    /// to a slot in the <see cref="Stages"/> array, indicating which stage is the owner of the given input or output port.
    ///
    /// Slots which would correspond to non-existent stages(where the corresponding port is null since it represents
    /// the currently unknown external context) contain the value <see cref="GraphInterpreter.Boundary"/>.
    ///
    ///The current assumption by the infrastructure is that the layout of these arrays looks like this:
    ///
    ///            +---------------------------------------+-----------------+
    /// inOwners:  | index to stages array | Boundary(-1)                    |
    ///            +----------------+----------------------+-----------------+
    /// ins:       | exposed inputs | internal connections | nulls           |
    ///            +----------------+----------------------+-----------------+
    /// outs:      | nulls          | internal connections | exposed outputs |
    ///            +----------------+----------------------+-----------------+
    /// outOwners: | Boundary(-1)   | index to stages array                  |
    ///            +----------------+----------------------------------------+
    ///
    /// In addition, it is also assumed by the infrastructure that the order of exposed inputs and outputs in the
    /// corresponding segments of these arrays matches the exact same order of the ports in the <see cref="Shape"/>.
    /// </summary>
    public sealed class GraphAssembly
    {
        public static GraphAssembly Create(IList<Inlet> inlets, IList<Outlet> outlets, IList<IGraphStageWithMaterializedValue<Shape, object>> stages)
        {
            // add the contents of an iterator to an array starting at idx
            var inletsCount = inlets.Count;
            var outletsCount = outlets.Count;
            var connectionsCount = inletsCount + outletsCount;

            if (connectionsCount <= 0) throw new ArgumentException($"Sum of inlets ({inletsCount}) and outlets ({outletsCount}) must be > 0");

            return new GraphAssembly(
                stages: stages.ToArray(),
                originalAttributes: SingleNoAttribute,
                inlets: Add(inlets, new Inlet[connectionsCount], 0),
                inletOwners: MarkBoundary(new int[connectionsCount], inletsCount, connectionsCount),
                outlets: Add(outlets, new Outlet[connectionsCount], inletsCount),
                outletOwners: MarkBoundary(new int[connectionsCount], 0, inletsCount));
        }

        private static int[] MarkBoundary(int[] owners, int from, int to)
        {
            for (var i = from; i < to; i++)
                owners[i] = Boundary;
            return owners;
        }

        private static T[] Add<T>(IList<T> seq, T[] array, int idx)
        {
            foreach (var t in seq)
                array[idx++] = t;
            return array;
        }

        public readonly IGraphStageWithMaterializedValue<Shape, object>[] Stages;
        public readonly Attributes[] OriginalAttributes;
        public readonly Inlet[] Inlets;
        public readonly int[] InletOwners;
        public readonly Outlet[] Outlets;
        public readonly int[] OutletOwners;

        public GraphAssembly(IGraphStageWithMaterializedValue<Shape, object>[] stages, Attributes[] originalAttributes, Inlet[] inlets, int[] inletOwners, Outlet[] outlets, int[] outletOwners)
        {
            if (inlets.Length != inletOwners.Length)
                throw new ArgumentException("'inlets' and 'inletOwners' must have the same length.", nameof(inletOwners));
            if (inletOwners.Length != outlets.Length)
                throw new ArgumentException("'inletOwners' and 'outlets' must have the same length.", nameof(outlets));
            if (outlets.Length != outletOwners.Length)
                throw new ArgumentException("'outlets' and 'outletOwners' must have the same length.", nameof(outletOwners));

            Stages = stages;
            OriginalAttributes = originalAttributes;
            Inlets = inlets;
            InletOwners = inletOwners;
            Outlets = outlets;
            OutletOwners = outletOwners;
        }

        public int ConnectionCount => Inlets.Length;

        /// <summary>
        /// Takes an interpreter and returns three arrays required by the interpreter containing the input, output port
        /// handlers and the stage logic instances.
        /// 
        /// <para>Returns a tuple of</para>
        /// <para/> - lookup table for InHandlers
        /// <para/> - lookup table for OutHandlers
        /// <para/> - array of the logics
        /// <para/> - materialized value
        /// </summary>
        public Tuple<Connection[], GraphStageLogic[]> Materialize(
            Attributes inheritedAttributes,
            IModule[] copiedModules,
            IDictionary<IModule, object> materializedValues,
            Action<IMaterializedValueSource> register)
        {
            var logics = new GraphStageLogic[Stages.Length];
            for (var i = 0; i < Stages.Length; i++)
            {
                // Port initialization loops, these must come first
                var shape = Stages[i].Shape;

                var idx = 0;
                var inletEnumerator = shape.Inlets.GetEnumerator();
                while (inletEnumerator.MoveNext())
                {
                    var inlet = inletEnumerator.Current;
                    if (inlet.Id != -1 && inlet.Id != idx)
                        throw new ArgumentException($"Inlet {inlet} was shared among multiple stages. That is illegal.");
                    inlet.Id = idx;
                    idx++;
                }

                idx = 0;
                var outletEnumerator = shape.Outlets.GetEnumerator();
                while (outletEnumerator.MoveNext())
                {
                    var outlet = outletEnumerator.Current;
                    if (outlet.Id != -1 && outlet.Id != idx)
                        throw new ArgumentException($"Outlet {outlet} was shared among multiple stages. That is illegal.");
                    outlet.Id = idx;
                    idx++;
                }

                var stage = Stages[i];
                if (stage is IMaterializedValueSource)
                {
                    var copy = ((IMaterializedValueSource) stage).CopySource();
                    register(copy);
                    stage = (IGraphStageWithMaterializedValue<Shape, object>)copy;
                }

                var logicAndMaterialized = stage.CreateLogicAndMaterializedValue(inheritedAttributes.And(OriginalAttributes[i]));
                materializedValues[copiedModules[i]] = logicAndMaterialized.MaterializedValue;
                logics[i] = logicAndMaterialized.Logic;
            }

            var connections = new Connection[ConnectionCount];
            
            for (var i = 0; i < ConnectionCount; i++)
            {
                var connection = new Connection(i, InletOwners[i],
                    InletOwners[i] == Boundary ? null : logics[InletOwners[i]], OutletOwners[i],
                    OutletOwners[i] == Boundary ? null : logics[OutletOwners[i]], null, null);
                connections[i] = connection;

                var inlet = Inlets[i];
                if (inlet != null)
                {
                    var owner = InletOwners[i];
                    var logic = logics[owner];
                    var h = logic.Handlers[inlet.Id] as IInHandler;

                    if (h == null) throw new IllegalStateException($"No handler defined in stage {logic} for port {inlet}");
                    connection.InHandler = h;

                    logic.PortToConn[inlet.Id] = connection;
                }

                var outlet = Outlets[i];
                if (outlet != null)
                {
                    var owner = OutletOwners[i];
                    var logic = logics[owner];
                    var inCount = logic.InCount;
                    var h = logic.Handlers[outlet.Id + inCount] as IOutHandler;

                    if (h == null) throw new IllegalStateException($"No handler defined in stage {logic} for port {outlet}");
                    connection.OutHandler = h;

                    logic.PortToConn[outlet.Id + inCount] = connection;
                }
            }

            return Tuple.Create(connections, logics);
        }

        public override string ToString()
        {
            return "GraphAssembly\n  " +
                   "Stages: [" + string.Join<IGraphStageWithMaterializedValue<Shape, object>>(",", Stages) + "]\n  " +
                   "Attributes: [" + string.Join<Attributes>(",", OriginalAttributes) + "]\n  " +
                   "Inlets: [" + string.Join<Inlet>(",", Inlets) + "]\n  " +
                   "InOwners: [" + string.Join(",", InletOwners) + "]\n  " +
                   "Outlets: [" + string.Join<Outlet>(",", Outlets) + "]\n  " +
                   "OutOwners: [" + string.Join(",", OutletOwners) + "]";
        }
    }
}