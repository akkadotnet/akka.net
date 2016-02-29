using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation.Fusing
{
    /**
     * INTERNAL API
     *
     * A GraphAssembly represents a small stream processing graph to be executed by the interpreter. Instances of this
     * class **must not** be mutated after construction.
     *
     * The array ``originalAttributes`` may contain the attribute information of the original atomic module, otherwise
     * it must contain a none (otherwise the enclosing module could not overwrite attributes defined in this array).
     *
     * The arrays [[ins]] and [[outs]] correspond to the notion of a *connection* in the [[GraphInterpreter]]. Each slot
     * *i* contains the input and output port corresponding to connection *i*. Slots where the graph is not closed (i.e.
     * ports are exposed to the external world) are marked with *null* values. For example if an input port *p* is
     * exposed, then outs(p) will contain a *null*.
     *
     * The arrays [[inOwners]] and [[outOwners]] are lookup tables from a connection id (the index of the slot)
     * to a slot in the [[stages]] array, indicating which stage is the owner of the given input or output port.
     * Slots which would correspond to non-existent stages (where the corresponding port is null since it represents
     * the currently unknown external context) contain the value [[GraphInterpreter#Boundary]].
     *
     * The current assumption by the infrastructure is that the layout of these arrays looks like this:
     *
     *            +---------------------------------------+-----------------+
     * inOwners:  | index to stages array                 | Boundary (-1)   |
     *            +----------------+----------------------+-----------------+
     * ins:       | exposed inputs | internal connections | nulls           |
     *            +----------------+----------------------+-----------------+
     * outs:      | nulls          | internal connections | exposed outputs |
     *            +----------------+----------------------+-----------------+
     * outOwners: | Boundary (-1)  | index to stages array                  |
     *            +----------------+----------------------------------------+
     *
     * In addition, it is also assumed by the infrastructure that the order of exposed inputs and outputs in the
     * corresponding segments of these arrays matches the exact same order of the ports in the [[Shape]].
     *
     */
    internal sealed class GraphAssembly
    {
        public static GraphAssembly Create(IEnumerable<Inlet> inlets, IEnumerable<Outlet> outlets, IEnumerable<IGraphStageWithMaterializedValue> stages)
        {
            // add the contents of an iterator to an array starting at idx
            var inletsCount = inlets.Count();
            var outletsCount = outlets.Count();
            var connectionsCount = inletsCount + outletsCount;

            if (connectionsCount <= 0) throw new ArgumentException(string.Format("Sum of inlets ({0}) and outlets ({1}) must be > 0", inletsCount, outletsCount));

            return new GraphAssembly(
                stages: stages.ToArray(),
                originalAttributes: GraphInterpreter.SingleNoAttribute,
                inlets: Add(inlets, new Inlet[connectionsCount], 0),
                inletOwners: MarkBoundary(new int[connectionsCount], inletsCount, connectionsCount),
                outlets: Add(outlets, new Outlet[connectionsCount], inletsCount),
                outletOwners: MarkBoundary(new int[connectionsCount], 0, inletsCount));
        }

        private static int[] MarkBoundary(int[] owners, int from, int to)
        {
            for (int i = from; i < to; i++)
                owners[i] = GraphInterpreter.Boundary;
            return owners;
        }

        private static T[] Add<T>(IEnumerable<T> seq, T[] array, int idx)
        {
            var i = seq.GetEnumerator();
            while (i.MoveNext())
            {
                array[idx] = i.Current;
                idx++;
            }
            return array;
        }

        public readonly IGraphStageWithMaterializedValue[] Stages;
        public readonly Attributes[] OriginalAttributes;
        public readonly Inlet[] Inlets;
        public readonly int[] InletOwners;
        public readonly Outlet[] Outlets;
        public readonly int[] OutletOwners;

        public GraphAssembly(IGraphStageWithMaterializedValue[] stages, Attributes[] originalAttributes, Inlet[] inlets, int[] inletOwners, Outlet[] outlets, int[] outletOwners)
        {
            Stages = stages;
            OriginalAttributes = originalAttributes;
            Inlets = inlets;
            InletOwners = inletOwners;
            Outlets = outlets;
            OutletOwners = outletOwners;
        }

        public int ConnectionCount { get { return Inlets.Length; } }

        /**
         * Takes an interpreter and returns three arrays required by the interpreter containing the input, output port
         * handlers and the stage logic instances.
         *
         * Returns a tuple of
         *  - lookup table for InHandlers
         *  - lookup table for OutHandlers
         *  - array of the logics
         *  - materialized value
         */
        public Tuple<InHandler[], OutHandler[], GraphStageLogic[]> Materialize(
            Attributes inheritedAttributes,
            IModule[] copiedModules,
            IDictionary<IModule, object> materializedValues,
            Action<IMaterializedValueSource> register)
        {
            var logics = new GraphStageLogic[Stages.Length];
            for (int i = 0; i < Stages.Length; i++)
            {
                // Port initialization loops, these must come first
                var shape = Stages[i].Shape;
                var idx = 0;
                var inletEnumerator = shape.Inlets.GetEnumerator();
                while (inletEnumerator.MoveNext())
                {
                    var inlet = inletEnumerator.Current;
                    if (!(inlet.Id == -1 || inlet.Id == idx)) throw new ArgumentException(string.Format("Inlet {0} was shared among multiple stages. That is illegal.", inlet));
                    inlet.Id = idx;
                    idx++;
                }

                idx = 0;
                var outletEnumerator = shape.Outlets.GetEnumerator();
                while (outletEnumerator.MoveNext())
                {
                    var outlet = outletEnumerator.Current;
                    if (!(outlet.Id == -1 || outlet.Id == idx)) throw new ArgumentException(string.Format("Outlet {0} was shared among multiple stages. That is illegal.", outlet));
                    outlet.Id = idx;
                    idx++;
                }

                var stage = Stages[i];
                if (stage is IMaterializedValueSource)
                {
                    var copy = ((IMaterializedValueSource) stage).CopySource();
                    register(copy);
                    stage = (IGraphStageWithMaterializedValue)copy;
                }

                object materialized;
                var logic = stage.CreateLogicAndMaterializedValue(inheritedAttributes.And(OriginalAttributes[i]), out materialized);
                materializedValues.Add(copiedModules[i], materialized);

                logics[i] = logic;
            }

            var inHandlers = new InHandler[ConnectionCount];
            var outHandlers = new OutHandler[ConnectionCount];

            for (int i = 0; i < ConnectionCount; i++)
            {
                var inlet = Inlets[i];
                if (inlet != null)
                {
                    var owner = InletOwners[i];
                    var logic = logics[owner];
                    var h = logic.Handlers[inlet.Id] as InHandler;

                    if (h == null) throw new IllegalStateException(string.Format("No handler defined in stage {0} for port {1}", logic, inlet));
                    inHandlers[i] = h;

                    logic.PortToConn[inlet.Id] = i;
                }

                var outlet = Outlets[i];
                if (outlet != null)
                {
                    var owner = OutletOwners[i];
                    var logic = logics[owner];
                    var h = logic.Handlers[outlet.Id] as OutHandler;

                    if (h == null) throw new IllegalStateException(string.Format("No handler defined in stage {0} for port {1}", logic, outlet));
                    outHandlers[i] = h;

                    logic.PortToConn[outlet.Id] = i;
                }
            }

            return Tuple.Create(inHandlers, outHandlers, logics);
        }
    }
}