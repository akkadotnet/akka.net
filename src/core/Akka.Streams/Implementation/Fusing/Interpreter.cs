using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Stage;
using IDirective = Akka.Streams.Supervision.IDirective;

namespace Akka.Streams.Implementation.Fusing
{
    /**
     * INTERNAL API
     *
     * This artificial op is used as a boundary to prevent two forked paths of execution (complete, cancel) to cross
     * paths again. When finishing an op this op is injected in its place to isolate upstream and downstream execution
     * domains.
     */
    internal sealed class Finished<TIn, TOut> : BoundaryStage<TIn, TOut>
    {
        public static readonly Finished<TIn, TOut> Instance = new Finished<TIn, TOut>();

        private Finished() { }
        public override IDirective OnPush(TIn element, IBoundaryContext<TOut> context)
        {
            return context.Finish();
        }

        public override IDirective OnPull(IBoundaryContext<TOut> context)
        {
            return context.Finish();
        }

        public override ITerminationDirective OnUpstreamFinish(IBoundaryContext<TOut> context)
        {
            return context.Exit();
        }

        public override ITerminationDirective OnDownstreamFinish(IBoundaryContext<TOut> context)
        {
            return context.Exit();
        }

        public override ITerminationDirective OnUpstreamFailure(Exception cause, IBoundaryContext<TOut> context)
        {
            return context.Exit();
        }
    }

    public interface IState
    {
        
    }

    internal struct OverflowStackItem
    {
        public readonly int Index;
        public readonly IState State;
        public readonly object Element;

        public OverflowStackItem(int index, IState state, object element) : this()
        {
            Index = index;
            State = state;
            Element = element;
        }
    }

    // TODO:
    // fix jumpback table with keep-going-on-complete ops (we might jump between otherwise isolated execution regions)
    // implement grouped, buffer
    // add recover

    public class OneBoundedInterpreter<TIn, TOut, TExt>
    {
        public static readonly bool IsDebug = false;

        public static BoundaryStage<TIn, TOut> GetFinished()
        {
            return Finished<TIn, TOut>.Instance;
        }

        private readonly IStage<TIn, TOut>[] _pipeline;
        private readonly Action<AsyncStage<TIn, TOut, TExt>, IAsyncContext<TIn, TExt>> _onAsyncInput;
        private readonly IMaterializer _materializer;
        private readonly Attributes _attributes;
        private readonly int _forkLimit;
        private readonly bool _overflowToHeap;
        private readonly string _name;

        /**
         * This table is used to accelerate demand propagation upstream. All ops that implement PushStage are guaranteed
         * to only do upstream propagation of demand signals, therefore it is not necessary to execute them but enough to
         * "jump over" them. This means that when a chain of one million maps gets a downstream demand it is propagated
         * to the upstream *in one step* instead of one million onPull() calls.
         * This table maintains the positions where execution should jump from a current position when a pull event is to
         * be executed.
         */
        private readonly int[] _jumpBacks;
        private readonly int _upstreamIndex = 0;
        private readonly int _downstreamIndex;

        /// <summary>
        /// Var to hold the current element if pushing. The only reason why this var is needed is to avoid 
        /// allocations and make it possible for the Pushing state to be an object
        /// </summary>
        private object _elemInFlight;

        /// <summary>
        /// Points to the current point of execution inside the pipeline
        /// </summary>
        private int _activeOpIndex = -1;

        /// <summary>
        /// Points to the last point of exit
        /// </summary>
        private int _lastExitedIndex;

        /// <summary>
        /// The current interpreter state that decides what happens at the next round
        /// </summary>
        private IState _state;

        /// <summary>
        /// Counter that keeps track of the depth of recursive forked executions
        /// </summary>
        private int _forkCount;

        /// <summary>
        /// List that is used as an auxiliary stack if fork recursion depth reaches forkLimit
        /// </summary>
        private List<OverflowStackItem> _owerflowStack = new List<OverflowStackItem>();
        private int _lastOpFailing;

        /**
         * INTERNAL API
         *
         * One-bounded interpreter for a linear chain of stream operations (graph support is possible and will be implemented
         * later)
         *
         * The ideas in this interpreter are an amalgamation of earlier ideas, notably:
         *  - The original effect-tracking implementation by Johannes Rudolph -- the difference here that effects are not chained
         *  together as classes but the callstack is used instead and only certain combinations are allowed.
         *  - The on-stack reentrant implementation by Mathias Doenitz -- the difference here that reentrancy is handled by the
         *  interpreter itself, not user code, and the interpreter is able to use the heap when needed instead of the
         *  callstack.
         *  - The pinball interpreter by Endre Sándor Varga -- the difference here that the restriction for "one ball" is
         *  lifted by using isolated execution regions, completion handling is introduced and communication with the external
         *  world is done via boundary ops.
         *
         * The design goals/features of this interpreter are:
         *  - bounded callstack and heapless execution whenever possible
         *  - callstack usage should be constant for the most common ops independently of the size of the op-chain
         *  - allocation-free execution on the hot paths
         *  - enforced backpressure-safety (boundedness) on user defined ops at compile-time (and runtime in a few cases)
         *
         * The main driving idea of this interpreter is the concept of 1-bounded execution of well-formed free choice Petri
         * nets (J. Desel and J. Esparza: Free Choice Petri Nets - https://www7.in.tum.de/~esparza/bookfc.html). Technically
         * different kinds of operations partition the chain of ops into regions where *exactly one* event is active all the
         * time. This "exactly one" property is enforced by proper types and runtime checks where needed. Currently there are
         * three kinds of ops:
         *
         *  - PushPullStage implementations participate in 1-bounded regions. For every external non-completion signal these
         *  ops produce *exactly one* signal (completion is different, explained later) therefore keeping the number of events
         *  the same: exactly one.
         *
         *  - DetachedStage implementations are boundaries between 1-bounded regions. This means that they need to enforce the
         *  "exactly one" property both on their upstream and downstream regions. As a consequence a DetachedStage can never
         *  answer an onPull with a ctx.pull() or answer an onPush() with a ctx.push() since such an action would "steal"
         *  the event from one region (resulting in zero signals) and would inject it to the other region (resulting in two
         *  signals). However DetachedStages have the ability to call ctx.hold() as a response to onPush/onPull which temporarily
         *  takes the signal off and stops execution, at the same time putting the op in a "holding" state. If the op is in a
         *  holding state it contains one absorbed signal, therefore in this state the only possible command to call is
         *  ctx.pushAndPull() which results in two events making the balance right again:
         *  1 hold + 1 external event = 2 external event
         *  This mechanism allows synchronization between the upstream and downstream regions which otherwise can progress
         *  independently.
         *
         *  - BoundaryStage implementations are meant to communicate with the external world. These ops do not have most of the
         *  safety properties enforced and should be used carefully. One important ability of BoundaryStages that they can take
         *  off an execution signal by calling ctx.exit(). This is typically used immediately after an external signal has
         *  been produced (for example an actor message). BoundaryStages can also kickstart execution by calling enter() which
         *  returns a context they can use to inject signals into the interpreter. There is no checks in place to enforce that
         *  the number of signals taken out by exit() and the number of signals returned via enter() are the same -- using this
         *  op type needs extra care from the implementer.
         *  BoundaryStages are the elements that make the interpreter *tick*, there is no other way to start the interpreter
         *  than using a BoundaryStage.
         *
         * Operations are allowed to do early completion and cancel/complete their upstreams and downstreams. It is *not*
         * allowed however to do these independently to avoid isolated execution islands. The only call possible is ctx.finish()
         * which is a combination of cancel/complete.
         * Since onComplete is not a backpressured signal it is sometimes preferable to push a final element and then immediately
         * finish. This combination is exposed as pushAndFinish() which enables op writers to propagate completion events without
         * waiting for an extra round of pull.
         * Another peculiarity is how to convert termination events (complete/failure) into elements. The problem
         * here is that the termination events are not backpressured while elements are. This means that simply calling ctx.push()
         * as a response to onUpstreamFinished() will very likely break boundedness and result in a buffer overflow somewhere.
         * Therefore the only allowed command in this case is ctx.absorbTermination() which stops the propagation of the
         * termination signal, and puts the op in a finishing state. Depending on whether the op has a pending pull signal it has
         * not yet "consumed" by a push its onPull() handler might be called immediately.
         *
         * In order to execute different individual execution regions the interpreter uses the callstack to schedule these. The
         * current execution forking operations are
         *  - ctx.finish() which starts a wave of completion and cancellation in two directions. When an op calls finish()
         *  it is immediately replaced by an artificial Finished op which makes sure that the two execution paths are isolated
         *  forever.
         *  - ctx.fail() which is similar to finish()
         *  - ctx.pushAndPull() which (as a response to a previous ctx.hold()) starts a wave of downstream push and upstream
         *  pull. The two execution paths are isolated by the op itself since onPull() from downstream can only be answered by hold or
         *  push, while onPush() from upstream can only answered by hold or pull -- it is impossible to "cross" the op.
         *  - ctx.pushAndFinish() which is different from the forking ops above because the execution of push and finish happens on
         *  the same execution region and they are order dependent, too.
         * The interpreter tracks the depth of recursive forking and allows various strategies of dealing with the situation
         * when this depth reaches a certain limit. In the simplest case a failure is reported (this is very useful for stress
         * testing and finding callstack wasting bugs), in the other case the forked call is scheduled via a list -- i.e. instead
         * of the stack the heap is used.
         */
        public OneBoundedInterpreter(IEnumerable<IStage<TIn, TOut>> operations, 
            Action<AsyncStage<TIn, TOut, TExt>, IAsyncContext<TIn, TExt>> onAsyncInput,
            IMaterializer materializer,
            Attributes attributes = null,
            int forkLimit = 100,
            bool overflowToHeap = true,
            string name = "")
        {
            _pipeline = operations.ToArray();
            if(_pipeline.Length == 0) throw new ArgumentException("OneBoundedInterpreter cannot be created without at least one Op", "operations");

            _onAsyncInput = onAsyncInput;
            _materializer = materializer;
            _attributes = attributes;
            _forkLimit = forkLimit;
            _overflowToHeap = overflowToHeap;
            _name = name;

            _jumpBacks = CalculateJumpBacks();
            _downstreamIndex = _pipeline.Length - 1;
            _lastExitedIndex = _downstreamIndex;
        }

        public IStage<TIn, TOut> CurrentStage { get { return _pipeline[_activeOpIndex]; } }

        public void Init()
        {
            throw new NotImplementedException();
        }

        private int[] CalculateJumpBacks()
        {
            var table = new int[_pipeline.Length];
            var nextJumpBack = -1;
            for (int i = 0; i < _pipeline.Length; i++)
            {
                table[i] = nextJumpBack;
                if (!(_pipeline[i] is PushStage<TIn, TOut>))
                {
                    nextJumpBack = i;
                }
            }
            return table;
        }

        private void UpdateJumpBacks(int lastNonCompletedIndex)
        {
            var pos = lastNonCompletedIndex;
            while (_jumpBacks[pos] < lastNonCompletedIndex && pos < _pipeline.Length)
            {
                _jumpBacks[pos] = lastNonCompletedIndex;
                pos++;
            }
        }

        private string GetPipeName(IStage<TIn, TOut> op)
        {
            if (op is Finished<TIn, TOut>) return "finished";
            if (op is BoundaryStage<TIn, TOut>) return "boundary";
            if (op is StatefulStage<TIn, TOut>) return "stateful";
            if (op is PushStage<TIn, TOut>) return "push";
            if (op is PushPullStage<TIn, TOut>) return "pushpull";
            if (op is DetachedStage<TIn, TOut>) return "detached";
            return "other";
        }
    }
}