using System;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Stages;
using Akka.Util.Internal.Collections;

namespace Akka.Streams.Dsl
{
    public static class FlexiMerge
    {
        public interface IReadCondition<T> { }

        /**
         * Read condition for the [[MergeLogic#State]] that will be
         * fulfilled when there are elements for one specific upstream
         * input.
         *
         * It is not allowed to use a handle that has been canceled or
         * has been completed. `IllegalArgumentException` is thrown if
         * that is not obeyed.
         */
        [Serializable]
        public sealed class Read<T> : IReadCondition<T>
        {
            public readonly Inlet<T> Input;

            public Read(Inlet<T> input)
            {
                Input = input;
            }
        }

        /**
         * Read condition for the [[MergeLogic#State]] that will be
         * fulfilled when there are elements for any of the given upstream
         * inputs.
         *
         * Canceled and completed inputs are not used, i.e. it is allowed
         * to specify them in the list of `inputs`.
         */
        [Serializable]
        public sealed class ReadAny<T> : IReadCondition<T>
        {
            public readonly Inlet<T>[] Inputs;

            public ReadAny(params Inlet<T>[] inputs)
            {
                Inputs = inputs;
            }
        }

        /**
         * Read condition for the [[MergeLogic#State]] that will be
         * fulfilled when there are elements for any of the given upstream
         * inputs, however it differs from [[ReadAny]] in the case that both
         * the `preferred` and at least one other `secondary` input have demand,
         * the `preferred` input will always be consumed first.
         *
         * Canceled and completed inputs are not used, i.e. it is allowed
         * to specify them in the list of `inputs`.
         */
        [Serializable]
        public sealed class ReadPreferred<T> : IReadCondition<T>
        {
            public readonly Inlet<T> Preferred;
            public readonly Inlet<T>[] Secondaries;

            public ReadPreferred(Inlet<T> preferred, Inlet<T>[] secondaries)
            {
                Preferred = preferred;
                Secondaries = secondaries;
            }
        }

        /**
         * Read condition for the [[MergeLogic#State]] that will be
         * fulfilled when there are elements for *all* of the given upstream
         * inputs.
         *
         * The emitted element the will be a [[ReadAllInputs]] object, which contains values for all non-canceled inputs of this FlexiMerge.
         *
         * Canceled inputs are not used, i.e. it is allowed to specify them in the list of `inputs`,
         * the resulting [[ReadAllInputs]] will then not contain values for this element, which can be
         * handled via supplying a default value instead of the value from the (now canceled) input.
         */
        public sealed class ReadAll<T> : IReadCondition<ReadAllInputs>
        {
            public readonly Func<IImmutableMap<InPort, object>, IReadAllInputs> ResultFactory;
            public readonly Inlet<T>[] Inputs;

            public ReadAll(Func<IImmutableMap<InPort, object>, IReadAllInputs> resultFactory, params Inlet<T>[] inputs)
            {
                ResultFactory = resultFactory;
                Inputs = inputs;
            }
        }

        public interface IReadAllInputs { }

        /**
         * Provides typesafe accessors to values from inputs supplied to [[ReadAll]].
         */
        [Serializable]
        public sealed class ReadAllInputs : IReadAllInputs
        {

        }

        /**
         * The possibly stateful logic that reads from input via the defined [[MergeLogic#State]] and
         * handles completion and failure via the defined [[MergeLogic#CompletionHandling]].
         *
         * Concrete instance is supposed to be created by implementing [[FlexiMerge#createMergeLogic]].
         */
        public abstract class MergeLogic<TOut>
        {
            #region Internal classes

            /**
             * Context that is passed to the `onUpstreamFinish` and `onUpstreamFailure`
             * functions of [[FlexiMerge$.CompletionHandling]].
             * The context provides means for performing side effects, such as emitting elements
             * downstream.
             */
            public interface IMergeLogicContextBase
            {
                /**
                 * Emit one element downstream. It is only allowed to `emit` zero or one
                 * element in response to `onInput`, otherwise `IllegalStateException`
                 * is thrown.
                 */
                void Emit(TOut element);
            }

            /**
             * Context that is passed to the `onInput` function of [[FlexiMerge$.State]].
             * The context provides means for performing side effects, such as emitting elements
             * downstream.
             */
            public interface IMergeLogicContext
            {
                /**
                 * Complete this stream successfully. Upstream subscriptions will be canceled.
                 */
                void Finish();

                /**
                 * Complete this stream with failure. Upstream subscriptions will be canceled.
                 */
                void Fail(Exception reason);

                /**
                 * Cancel a specific upstream input stream.
                 */
                void Cancel(InPort input);

                /**
                 * Replace current [[CompletionHandling]].
                 */
                void ChangeCompletionHandling(CompletionHandling completion);
            }

            /**
             * Definition of which inputs to read from and how to act on the read elements.
             * When an element has been read [[#onInput]] is called and then it is ensured
             * that downstream has requested at least one element, i.e. it is allowed to
             * emit at most one element downstream with [[MergeLogicContext#emit]].
             *
             * The `onInput` function is called when an `element` was read from the `input`.
             * The function returns next behavior or [[#SameState]] to keep current behavior.
             */
            [Serializable]
            public sealed class State<TIn>
            {
                public readonly IReadCondition<TIn> Condition;
                public readonly Func<IMergeLogicContext, InPort, TIn, State<TIn>> OnInput;

                public State(IReadCondition<TIn> condition, Func<IMergeLogicContext, InPort, TIn, State<TIn>> onInput)
                {
                    Condition = condition;
                    OnInput = onInput;
                }
            }

            /**
             * How to handle completion or failure from upstream input.
             *
             * The `onUpstreamFinish` function is called when an upstream input was completed successfully.
             * It returns next behavior or [[#SameState]] to keep current behavior.
             * A completion can be propagated downstream with [[MergeLogicContextBase#finish]],
             * or it can be swallowed to continue with remaining inputs.
             *
             * The `onUpstreamFailure` function is called when an upstream input was completed with failure.
             * It returns next behavior or [[#SameState]] to keep current behavior.
             * A failure can be propagated downstream with [[MergeLogicContextBase#fail]],
             * or it can be swallowed to continue with remaining inputs.
             *
             * It is not possible to emit elements from the completion handling, since completion
             * handlers may be invoked at any time (without regard to downstream demand being available).
             */
            public struct CompletionHandling<TIn>
            {
                public readonly Func<IMergeLogicContextBase, InPort, State<TIn>> OnUpstreamFinish;
                public readonly Func<IMergeLogicContextBase, InPort, Exception, State<TIn>> OnUpstreamFailure;

                public CompletionHandling(Func<IMergeLogicContextBase, InPort, State<TIn>> onUpstreamFinish, Func<IMergeLogicContextBase, InPort, Exception, State<TIn>> onUpstreamFailure)
                {
                    OnUpstreamFinish = onUpstreamFinish;
                    OnUpstreamFailure = onUpstreamFailure;
                }
            }

            #endregion
            
            protected MergeLogic()
            {

            }

            public abstract State<TOut> InitialState { get; }


        }
    }


    /**
     * Base class for implementing custom merge junctions.
     * Such a junction always has one `out` port and one or more `in` ports.
     * The ports need to be defined by the concrete subclass by providing them as a constructor argument
     * to the [[FlexiMerge]] base class.
     *
     * The concrete subclass must implement [[#createMergeLogic]] to define the [[FlexiMerge#MergeLogic]]
     * that will be used when reading input elements and emitting output elements.
     * As response to an input element it is allowed to emit at most one output element.
     *
     * The [[FlexiMerge#MergeLogic]] instance may be stateful, but the ``FlexiMerge`` instance
     * must not hold mutable state, since it may be shared across several materialized ``FlowGraph``
     * instances.
     *
     * @param ports ports that this junction exposes
     * @param attributes optional attributes for this junction
     */
    public abstract class FlexiMerge<TOut, TShape> : IGraph<TShape, object> where TShape : Shape
    {
        private readonly TShape _shape;
        private readonly Attributes _attributes;
        private readonly Lazy<IModule> _module;

        protected FlexiMerge(TShape shape, Attributes attributes)
        {
            _shape = shape;
            _attributes = attributes;
            _module = new Lazy<IModule>(() => new Junctions.FlexiMergeModule<TOut, TShape>(shape, CreateMergeLogic(shape), attributes.And(DefaultAttributes.FlexiMerge)));
        }

        protected abstract FlexiMerge.MergeLogic<TOut> CreateMergeLogic(TShape shape);

        public TShape Shape { get { return _shape; } }
        public IModule Module { get { return _module.Value; } }

        public virtual IGraph<TShape, object> WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException("WithAttributes not supported by default by FlexiMerge, subclass may override and implement it");
        }

        public IGraph<TShape, object> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }
}