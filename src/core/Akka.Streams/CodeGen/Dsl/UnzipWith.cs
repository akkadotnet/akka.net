//-----------------------------------------------------------------------
// <copyright file="UnzipWith.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Creates a typed <see cref="UnzipWith{TIn,T0,T1}"/> stage from a function that maps one input element to multiple outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut">The result type returned by the supplied function.</typeparam>
    /// <typeparam name="T">The type returned by <see cref="Create"/>.</typeparam>
    public interface IUnzipWithCreator<out TIn, in TOut, out T>
    {
        /// <summary>
        /// Creates an unzip stage using the supplied splitter function.
        /// </summary>
        /// <param name="unzipper">The function used to create a value of type <typeparamref name="T"/> from an input.</param>
        /// <returns>A value of type <typeparamref name="T"/> created from the supplied function.</returns>
        T Create(Func<TIn, TOut> unzipper);
    }

    /// <summary>
    /// Base class for factories that create an unzip stage from a function returning a tuple of outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
    /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
    public abstract class UnzipWithCreator<TIn, TOut0, TOut1> : IUnzipWithCreator<TIn, (TOut0, TOut1), UnzipWith<TIn, TOut0, TOut1>>
    {
        /// <summary>
        /// Creates an unzip stage for a function that maps one input element to a tuple of outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <returns>An unzip stage whose outputs correspond to the tuple items.</returns>
        public virtual UnzipWith<TIn, TOut0, TOut1> Create(Func<TIn, (TOut0, TOut1)> unzipper)
        {
            return new UnzipWith<TIn, TOut0, TOut1>(unzipper);
        }
    }
    /// <summary>
    /// Base class for factories that create an unzip stage from a function returning a tuple of outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
    /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
    /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
    public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2> : IUnzipWithCreator<TIn, (TOut0, TOut1, TOut2), UnzipWith<TIn, TOut0, TOut1, TOut2>>
    {
        /// <summary>
        /// Creates an unzip stage for a function that maps one input element to a tuple of outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <returns>An unzip stage whose outputs correspond to the tuple items.</returns>
        public virtual UnzipWith<TIn, TOut0, TOut1, TOut2> Create(Func<TIn, (TOut0, TOut1, TOut2)> unzipper)
        {
            return new UnzipWith<TIn, TOut0, TOut1, TOut2>(unzipper);
        }
    }
    /// <summary>
    /// Base class for factories that create an unzip stage from a function returning a tuple of outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
    /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
    /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
    /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
    public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3> : IUnzipWithCreator<TIn, (TOut0, TOut1, TOut2, TOut3), UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3>>
    {
        /// <summary>
        /// Creates an unzip stage for a function that maps one input element to a tuple of outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <returns>An unzip stage whose outputs correspond to the tuple items.</returns>
        public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3> Create(Func<TIn, (TOut0, TOut1, TOut2, TOut3)> unzipper)
        {
            return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3>(unzipper);
        }
    }
    /// <summary>
    /// Base class for factories that create an unzip stage from a function returning a tuple of outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
    /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
    /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
    /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
    /// <typeparam name="TOut4">The element type of output tuple item <c>Item5</c>.</typeparam>
    public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> : IUnzipWithCreator<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4), UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4>>
    {
        /// <summary>
        /// Creates an unzip stage for a function that maps one input element to a tuple of outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <returns>An unzip stage whose outputs correspond to the tuple items.</returns>
        public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> Create(Func<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4)> unzipper)
        {
            return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4>(unzipper);
        }
    }
    /// <summary>
    /// Base class for factories that create an unzip stage from a function returning a tuple of outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
    /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
    /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
    /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
    /// <typeparam name="TOut4">The element type of output tuple item <c>Item5</c>.</typeparam>
    /// <typeparam name="TOut5">The element type of output tuple item <c>Item6</c>.</typeparam>
    public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> : IUnzipWithCreator<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4, TOut5), UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>>
    {
        /// <summary>
        /// Creates an unzip stage for a function that maps one input element to a tuple of outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <returns>An unzip stage whose outputs correspond to the tuple items.</returns>
        public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> Create(Func<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4, TOut5)> unzipper)
        {
            return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>(unzipper);
        }
    }
    /// <summary>
    /// Base class for factories that create an unzip stage from a function returning a tuple of outputs.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
    /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
    /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
    /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
    /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
    /// <typeparam name="TOut4">The element type of output tuple item <c>Item5</c>.</typeparam>
    /// <typeparam name="TOut5">The element type of output tuple item <c>Item6</c>.</typeparam>
    /// <typeparam name="TOut6">The element type of output tuple item <c>Item7</c>.</typeparam>
    public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> : IUnzipWithCreator<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6), UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>>
    {
        /// <summary>
        /// Creates an unzip stage for a function that maps one input element to a tuple of outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <returns>An unzip stage whose outputs correspond to the tuple items.</returns>
        public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> Create(Func<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6)> unzipper)
        {
            return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>(unzipper);
        }
    }

    /// <summary>
    /// Provides factories for unzip stages that split each input element into a tuple of output elements.
    /// </summary>
    public partial class UnzipWith 
    {
        /// <summary>
        /// Creates an unzip stage using the supplied tuple-producing function and creator.
        /// </summary>
        /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
        /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <param name="creator">The factory that constructs the unzip stage.</param>
        /// <returns>The stage created by <paramref name="creator"/> for <paramref name="unzipper"/>.</returns>
        public static UnzipWith<TIn, TOut0, TOut1> Apply<TIn, TOut0, TOut1>(Func<TIn, (TOut0, TOut1)> unzipper, UnzipWithCreator<TIn, TOut0, TOut1> creator)
        {
            return creator.Create(unzipper);
        }	
        /// <summary>
        /// Creates an unzip stage using the supplied tuple-producing function and creator.
        /// </summary>
        /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
        /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
        /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <param name="creator">The factory that constructs the unzip stage.</param>
        /// <returns>The stage created by <paramref name="creator"/> for <paramref name="unzipper"/>.</returns>
        public static UnzipWith<TIn, TOut0, TOut1, TOut2> Apply<TIn, TOut0, TOut1, TOut2>(Func<TIn, (TOut0, TOut1, TOut2)> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2> creator)
        {
            return creator.Create(unzipper);
        }	
        /// <summary>
        /// Creates an unzip stage using the supplied tuple-producing function and creator.
        /// </summary>
        /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
        /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
        /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
        /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <param name="creator">The factory that constructs the unzip stage.</param>
        /// <returns>The stage created by <paramref name="creator"/> for <paramref name="unzipper"/>.</returns>
        public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3> Apply<TIn, TOut0, TOut1, TOut2, TOut3>(Func<TIn, (TOut0, TOut1, TOut2, TOut3)> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3> creator)
        {
            return creator.Create(unzipper);
        }	
        /// <summary>
        /// Creates an unzip stage using the supplied tuple-producing function and creator.
        /// </summary>
        /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
        /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
        /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
        /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
        /// <typeparam name="TOut4">The element type of output tuple item <c>Item5</c>.</typeparam>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <param name="creator">The factory that constructs the unzip stage.</param>
        /// <returns>The stage created by <paramref name="creator"/> for <paramref name="unzipper"/>.</returns>
        public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> Apply<TIn, TOut0, TOut1, TOut2, TOut3, TOut4>(Func<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4)> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> creator)
        {
            return creator.Create(unzipper);
        }	
        /// <summary>
        /// Creates an unzip stage using the supplied tuple-producing function and creator.
        /// </summary>
        /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
        /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
        /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
        /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
        /// <typeparam name="TOut4">The element type of output tuple item <c>Item5</c>.</typeparam>
        /// <typeparam name="TOut5">The element type of output tuple item <c>Item6</c>.</typeparam>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <param name="creator">The factory that constructs the unzip stage.</param>
        /// <returns>The stage created by <paramref name="creator"/> for <paramref name="unzipper"/>.</returns>
        public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> Apply<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>(Func<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4, TOut5)> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> creator)
        {
            return creator.Create(unzipper);
        }	
        /// <summary>
        /// Creates an unzip stage using the supplied tuple-producing function and creator.
        /// </summary>
        /// <typeparam name="TIn">The input element type accepted by the splitter.</typeparam>
        /// <typeparam name="TOut0">The element type of output tuple item <c>Item1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type of output tuple item <c>Item2</c>.</typeparam>
        /// <typeparam name="TOut2">The element type of output tuple item <c>Item3</c>.</typeparam>
        /// <typeparam name="TOut3">The element type of output tuple item <c>Item4</c>.</typeparam>
        /// <typeparam name="TOut4">The element type of output tuple item <c>Item5</c>.</typeparam>
        /// <typeparam name="TOut5">The element type of output tuple item <c>Item6</c>.</typeparam>
        /// <typeparam name="TOut6">The element type of output tuple item <c>Item7</c>.</typeparam>
        /// <param name="unzipper">A function that maps one input element to a tuple of output elements.</param>
        /// <param name="creator">The factory that constructs the unzip stage.</param>
        /// <returns>The stage created by <paramref name="creator"/> for <paramref name="unzipper"/>.</returns>
        public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> Apply<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>(Func<TIn, (TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6)> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> creator)
        {
            return creator.Create(unzipper);
        }	
    }

    /// <summary>
    /// Splits each input element into multiple output elements using a tuple-producing function.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port and splitter function.</typeparam>
    /// <typeparam name="T0">The element type emitted by output port <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output port <c>Out1</c>.</typeparam>
    public class UnzipWith<TIn, T0, T1> : GraphStage<FanOutShape<TIn, T0, T1>>
    {
        private sealed class UnzipWithStageLogic : InGraphStageLogic 
        {
            private readonly UnzipWith<TIn, T0, T1> _stage;
            private int _pendingCount = 2;
            private int _downstreamRunning = 2;
            private bool _pending0 = true;
            private bool _pending1 = true;
    
            public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);				
                
                SetHandler(stage.Out0, onPull: () => {
                    _pendingCount--;
                    _pending0 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending0) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out1, onPull: () => {
                    _pendingCount--;
                    _pending1 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending1) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
            }

            public override void  OnPush()
            {
                var elements = _stage._unzipper(Grab(_stage.In));
                    
                if (!IsClosed(_stage.Out0)) 
                {
                    Push(_stage.Out0, elements.Item1);
                    _pending0 = true;
                }
                if (!IsClosed(_stage.Out1)) 
                {
                    Push(_stage.Out1, elements.Item2);
                    _pending1 = true;
                }
                
                _pendingCount = _downstreamRunning;
            }
        }		

        private readonly Func<TIn, (T0, T1)> _unzipper;
        /// <summary>
        /// Creates a stage that applies <paramref name="unzipper"/> to each input and emits tuple items on separate outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps each input element to a tuple containing one value for each output.</param>
        public UnzipWith(Func<TIn, (T0, T1)> unzipper)
        {
            _unzipper = unzipper;

            InitialAttributes = Attributes.CreateName("UnzipWith");
            Shape = new FanOutShape<TIn, T0, T1>("UnzipWith");
            In = Shape.In;

            Out0 = Shape.Out0;
            Out1 = Shape.Out1;
        }

        /// <summary>
        /// The input port that supplies elements to the splitter.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// Output port <c>Out0</c> for tuple item <c>Item1</c>.
        /// </summary>
        public Outlet<T0> Out0 { get; }
        /// <summary>
        /// Output port <c>Out1</c> for tuple item <c>Item2</c>.
        /// </summary>
        public Outlet<T1> Out1 { get; }

        /// <summary>
        /// The attributes initially assigned to this graph stage.
        /// </summary>
        protected sealed override Attributes InitialAttributes { get; }
        /// <summary>
        /// The stage's input and typed output ports.
        /// </summary>
        public sealed override FanOutShape<TIn, T0, T1> Shape { get; }
        /// <summary>
        /// Creates the stage logic that applies the splitter and coordinates demand across its outputs.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited from the surrounding graph.</param>
        /// <returns>The logic that handles this stage's input and output ports.</returns>
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
    }
    /// <summary>
    /// Splits each input element into multiple output elements using a tuple-producing function.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port and splitter function.</typeparam>
    /// <typeparam name="T0">The element type emitted by output port <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output port <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output port <c>Out2</c>.</typeparam>
    public class UnzipWith<TIn, T0, T1, T2> : GraphStage<FanOutShape<TIn, T0, T1, T2>>
    {
        private sealed class UnzipWithStageLogic : InGraphStageLogic 
        {
            private readonly UnzipWith<TIn, T0, T1, T2> _stage;
            private int _pendingCount = 3;
            private int _downstreamRunning = 3;
            private bool _pending0 = true;
            private bool _pending1 = true;
            private bool _pending2 = true;
    
            public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);				
                
                SetHandler(stage.Out0, onPull: () => {
                    _pendingCount--;
                    _pending0 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending0) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out1, onPull: () => {
                    _pendingCount--;
                    _pending1 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending1) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out2, onPull: () => {
                    _pendingCount--;
                    _pending2 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending2) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
            }

            public override void  OnPush()
            {
                var elements = _stage._unzipper(Grab(_stage.In));
                    
                if (!IsClosed(_stage.Out0)) 
                {
                    Push(_stage.Out0, elements.Item1);
                    _pending0 = true;
                }
                if (!IsClosed(_stage.Out1)) 
                {
                    Push(_stage.Out1, elements.Item2);
                    _pending1 = true;
                }
                if (!IsClosed(_stage.Out2)) 
                {
                    Push(_stage.Out2, elements.Item3);
                    _pending2 = true;
                }
                
                _pendingCount = _downstreamRunning;
            }
        }		

        private readonly Func<TIn, (T0, T1, T2)> _unzipper;
        /// <summary>
        /// Creates a stage that applies <paramref name="unzipper"/> to each input and emits tuple items on separate outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps each input element to a tuple containing one value for each output.</param>
        public UnzipWith(Func<TIn, (T0, T1, T2)> unzipper)
        {
            _unzipper = unzipper;

            InitialAttributes = Attributes.CreateName("UnzipWith");
            Shape = new FanOutShape<TIn, T0, T1, T2>("UnzipWith");
            In = Shape.In;

            Out0 = Shape.Out0;
            Out1 = Shape.Out1;
            Out2 = Shape.Out2;
        }

        /// <summary>
        /// The input port that supplies elements to the splitter.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// Output port <c>Out0</c> for tuple item <c>Item1</c>.
        /// </summary>
        public Outlet<T0> Out0 { get; }
        /// <summary>
        /// Output port <c>Out1</c> for tuple item <c>Item2</c>.
        /// </summary>
        public Outlet<T1> Out1 { get; }
        /// <summary>
        /// Output port <c>Out2</c> for tuple item <c>Item3</c>.
        /// </summary>
        public Outlet<T2> Out2 { get; }

        /// <summary>
        /// The attributes initially assigned to this graph stage.
        /// </summary>
        protected sealed override Attributes InitialAttributes { get; }
        /// <summary>
        /// The stage's input and typed output ports.
        /// </summary>
        public sealed override FanOutShape<TIn, T0, T1, T2> Shape { get; }
        /// <summary>
        /// Creates the stage logic that applies the splitter and coordinates demand across its outputs.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited from the surrounding graph.</param>
        /// <returns>The logic that handles this stage's input and output ports.</returns>
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
    }
    /// <summary>
    /// Splits each input element into multiple output elements using a tuple-producing function.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port and splitter function.</typeparam>
    /// <typeparam name="T0">The element type emitted by output port <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output port <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output port <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output port <c>Out3</c>.</typeparam>
    public class UnzipWith<TIn, T0, T1, T2, T3> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3>>
    {
        private sealed class UnzipWithStageLogic : InGraphStageLogic 
        {
            private readonly UnzipWith<TIn, T0, T1, T2, T3> _stage;
            private int _pendingCount = 4;
            private int _downstreamRunning = 4;
            private bool _pending0 = true;
            private bool _pending1 = true;
            private bool _pending2 = true;
            private bool _pending3 = true;
    
            public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);				
                
                SetHandler(stage.Out0, onPull: () => {
                    _pendingCount--;
                    _pending0 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending0) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out1, onPull: () => {
                    _pendingCount--;
                    _pending1 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending1) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out2, onPull: () => {
                    _pendingCount--;
                    _pending2 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending2) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out3, onPull: () => {
                    _pendingCount--;
                    _pending3 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending3) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
            }

            public override void  OnPush()
            {
                var elements = _stage._unzipper(Grab(_stage.In));
                    
                if (!IsClosed(_stage.Out0)) 
                {
                    Push(_stage.Out0, elements.Item1);
                    _pending0 = true;
                }
                if (!IsClosed(_stage.Out1)) 
                {
                    Push(_stage.Out1, elements.Item2);
                    _pending1 = true;
                }
                if (!IsClosed(_stage.Out2)) 
                {
                    Push(_stage.Out2, elements.Item3);
                    _pending2 = true;
                }
                if (!IsClosed(_stage.Out3)) 
                {
                    Push(_stage.Out3, elements.Item4);
                    _pending3 = true;
                }
                
                _pendingCount = _downstreamRunning;
            }
        }		

        private readonly Func<TIn, (T0, T1, T2, T3)> _unzipper;
        /// <summary>
        /// Creates a stage that applies <paramref name="unzipper"/> to each input and emits tuple items on separate outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps each input element to a tuple containing one value for each output.</param>
        public UnzipWith(Func<TIn, (T0, T1, T2, T3)> unzipper)
        {
            _unzipper = unzipper;

            InitialAttributes = Attributes.CreateName("UnzipWith");
            Shape = new FanOutShape<TIn, T0, T1, T2, T3>("UnzipWith");
            In = Shape.In;

            Out0 = Shape.Out0;
            Out1 = Shape.Out1;
            Out2 = Shape.Out2;
            Out3 = Shape.Out3;
        }

        /// <summary>
        /// The input port that supplies elements to the splitter.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// Output port <c>Out0</c> for tuple item <c>Item1</c>.
        /// </summary>
        public Outlet<T0> Out0 { get; }
        /// <summary>
        /// Output port <c>Out1</c> for tuple item <c>Item2</c>.
        /// </summary>
        public Outlet<T1> Out1 { get; }
        /// <summary>
        /// Output port <c>Out2</c> for tuple item <c>Item3</c>.
        /// </summary>
        public Outlet<T2> Out2 { get; }
        /// <summary>
        /// Output port <c>Out3</c> for tuple item <c>Item4</c>.
        /// </summary>
        public Outlet<T3> Out3 { get; }

        /// <summary>
        /// The attributes initially assigned to this graph stage.
        /// </summary>
        protected sealed override Attributes InitialAttributes { get; }
        /// <summary>
        /// The stage's input and typed output ports.
        /// </summary>
        public sealed override FanOutShape<TIn, T0, T1, T2, T3> Shape { get; }
        /// <summary>
        /// Creates the stage logic that applies the splitter and coordinates demand across its outputs.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited from the surrounding graph.</param>
        /// <returns>The logic that handles this stage's input and output ports.</returns>
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
    }
    /// <summary>
    /// Splits each input element into multiple output elements using a tuple-producing function.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port and splitter function.</typeparam>
    /// <typeparam name="T0">The element type emitted by output port <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output port <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output port <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output port <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output port <c>Out4</c>.</typeparam>
    public class UnzipWith<TIn, T0, T1, T2, T3, T4> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3, T4>>
    {
        private sealed class UnzipWithStageLogic : InGraphStageLogic 
        {
            private readonly UnzipWith<TIn, T0, T1, T2, T3, T4> _stage;
            private int _pendingCount = 5;
            private int _downstreamRunning = 5;
            private bool _pending0 = true;
            private bool _pending1 = true;
            private bool _pending2 = true;
            private bool _pending3 = true;
            private bool _pending4 = true;
    
            public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);				
                
                SetHandler(stage.Out0, onPull: () => {
                    _pendingCount--;
                    _pending0 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending0) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out1, onPull: () => {
                    _pendingCount--;
                    _pending1 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending1) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out2, onPull: () => {
                    _pendingCount--;
                    _pending2 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending2) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out3, onPull: () => {
                    _pendingCount--;
                    _pending3 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending3) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out4, onPull: () => {
                    _pendingCount--;
                    _pending4 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending4) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
            }

            public override void  OnPush()
            {
                var elements = _stage._unzipper(Grab(_stage.In));
                    
                if (!IsClosed(_stage.Out0)) 
                {
                    Push(_stage.Out0, elements.Item1);
                    _pending0 = true;
                }
                if (!IsClosed(_stage.Out1)) 
                {
                    Push(_stage.Out1, elements.Item2);
                    _pending1 = true;
                }
                if (!IsClosed(_stage.Out2)) 
                {
                    Push(_stage.Out2, elements.Item3);
                    _pending2 = true;
                }
                if (!IsClosed(_stage.Out3)) 
                {
                    Push(_stage.Out3, elements.Item4);
                    _pending3 = true;
                }
                if (!IsClosed(_stage.Out4)) 
                {
                    Push(_stage.Out4, elements.Item5);
                    _pending4 = true;
                }
                
                _pendingCount = _downstreamRunning;
            }
        }		

        private readonly Func<TIn, (T0, T1, T2, T3, T4)> _unzipper;
        /// <summary>
        /// Creates a stage that applies <paramref name="unzipper"/> to each input and emits tuple items on separate outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps each input element to a tuple containing one value for each output.</param>
        public UnzipWith(Func<TIn, (T0, T1, T2, T3, T4)> unzipper)
        {
            _unzipper = unzipper;

            InitialAttributes = Attributes.CreateName("UnzipWith");
            Shape = new FanOutShape<TIn, T0, T1, T2, T3, T4>("UnzipWith");
            In = Shape.In;

            Out0 = Shape.Out0;
            Out1 = Shape.Out1;
            Out2 = Shape.Out2;
            Out3 = Shape.Out3;
            Out4 = Shape.Out4;
        }

        /// <summary>
        /// The input port that supplies elements to the splitter.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// Output port <c>Out0</c> for tuple item <c>Item1</c>.
        /// </summary>
        public Outlet<T0> Out0 { get; }
        /// <summary>
        /// Output port <c>Out1</c> for tuple item <c>Item2</c>.
        /// </summary>
        public Outlet<T1> Out1 { get; }
        /// <summary>
        /// Output port <c>Out2</c> for tuple item <c>Item3</c>.
        /// </summary>
        public Outlet<T2> Out2 { get; }
        /// <summary>
        /// Output port <c>Out3</c> for tuple item <c>Item4</c>.
        /// </summary>
        public Outlet<T3> Out3 { get; }
        /// <summary>
        /// Output port <c>Out4</c> for tuple item <c>Item5</c>.
        /// </summary>
        public Outlet<T4> Out4 { get; }

        /// <summary>
        /// The attributes initially assigned to this graph stage.
        /// </summary>
        protected sealed override Attributes InitialAttributes { get; }
        /// <summary>
        /// The stage's input and typed output ports.
        /// </summary>
        public sealed override FanOutShape<TIn, T0, T1, T2, T3, T4> Shape { get; }
        /// <summary>
        /// Creates the stage logic that applies the splitter and coordinates demand across its outputs.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited from the surrounding graph.</param>
        /// <returns>The logic that handles this stage's input and output ports.</returns>
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
    }
    /// <summary>
    /// Splits each input element into multiple output elements using a tuple-producing function.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port and splitter function.</typeparam>
    /// <typeparam name="T0">The element type emitted by output port <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output port <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output port <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output port <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output port <c>Out4</c>.</typeparam>
    /// <typeparam name="T5">The element type emitted by output port <c>Out5</c>.</typeparam>
    public class UnzipWith<TIn, T0, T1, T2, T3, T4, T5> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3, T4, T5>>
    {
        private sealed class UnzipWithStageLogic : InGraphStageLogic 
        {
            private readonly UnzipWith<TIn, T0, T1, T2, T3, T4, T5> _stage;
            private int _pendingCount = 6;
            private int _downstreamRunning = 6;
            private bool _pending0 = true;
            private bool _pending1 = true;
            private bool _pending2 = true;
            private bool _pending3 = true;
            private bool _pending4 = true;
            private bool _pending5 = true;
    
            public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4, T5> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);				
                
                SetHandler(stage.Out0, onPull: () => {
                    _pendingCount--;
                    _pending0 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending0) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out1, onPull: () => {
                    _pendingCount--;
                    _pending1 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending1) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out2, onPull: () => {
                    _pendingCount--;
                    _pending2 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending2) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out3, onPull: () => {
                    _pendingCount--;
                    _pending3 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending3) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out4, onPull: () => {
                    _pendingCount--;
                    _pending4 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending4) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out5, onPull: () => {
                    _pendingCount--;
                    _pending5 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending5) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
            }

            public override void  OnPush()
            {
                var elements = _stage._unzipper(Grab(_stage.In));
                    
                if (!IsClosed(_stage.Out0)) 
                {
                    Push(_stage.Out0, elements.Item1);
                    _pending0 = true;
                }
                if (!IsClosed(_stage.Out1)) 
                {
                    Push(_stage.Out1, elements.Item2);
                    _pending1 = true;
                }
                if (!IsClosed(_stage.Out2)) 
                {
                    Push(_stage.Out2, elements.Item3);
                    _pending2 = true;
                }
                if (!IsClosed(_stage.Out3)) 
                {
                    Push(_stage.Out3, elements.Item4);
                    _pending3 = true;
                }
                if (!IsClosed(_stage.Out4)) 
                {
                    Push(_stage.Out4, elements.Item5);
                    _pending4 = true;
                }
                if (!IsClosed(_stage.Out5)) 
                {
                    Push(_stage.Out5, elements.Item6);
                    _pending5 = true;
                }
                
                _pendingCount = _downstreamRunning;
            }
        }		

        private readonly Func<TIn, (T0, T1, T2, T3, T4, T5)> _unzipper;
        /// <summary>
        /// Creates a stage that applies <paramref name="unzipper"/> to each input and emits tuple items on separate outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps each input element to a tuple containing one value for each output.</param>
        public UnzipWith(Func<TIn, (T0, T1, T2, T3, T4, T5)> unzipper)
        {
            _unzipper = unzipper;

            InitialAttributes = Attributes.CreateName("UnzipWith");
            Shape = new FanOutShape<TIn, T0, T1, T2, T3, T4, T5>("UnzipWith");
            In = Shape.In;

            Out0 = Shape.Out0;
            Out1 = Shape.Out1;
            Out2 = Shape.Out2;
            Out3 = Shape.Out3;
            Out4 = Shape.Out4;
            Out5 = Shape.Out5;
        }

        /// <summary>
        /// The input port that supplies elements to the splitter.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// Output port <c>Out0</c> for tuple item <c>Item1</c>.
        /// </summary>
        public Outlet<T0> Out0 { get; }
        /// <summary>
        /// Output port <c>Out1</c> for tuple item <c>Item2</c>.
        /// </summary>
        public Outlet<T1> Out1 { get; }
        /// <summary>
        /// Output port <c>Out2</c> for tuple item <c>Item3</c>.
        /// </summary>
        public Outlet<T2> Out2 { get; }
        /// <summary>
        /// Output port <c>Out3</c> for tuple item <c>Item4</c>.
        /// </summary>
        public Outlet<T3> Out3 { get; }
        /// <summary>
        /// Output port <c>Out4</c> for tuple item <c>Item5</c>.
        /// </summary>
        public Outlet<T4> Out4 { get; }
        /// <summary>
        /// Output port <c>Out5</c> for tuple item <c>Item6</c>.
        /// </summary>
        public Outlet<T5> Out5 { get; }

        /// <summary>
        /// The attributes initially assigned to this graph stage.
        /// </summary>
        protected sealed override Attributes InitialAttributes { get; }
        /// <summary>
        /// The stage's input and typed output ports.
        /// </summary>
        public sealed override FanOutShape<TIn, T0, T1, T2, T3, T4, T5> Shape { get; }
        /// <summary>
        /// Creates the stage logic that applies the splitter and coordinates demand across its outputs.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited from the surrounding graph.</param>
        /// <returns>The logic that handles this stage's input and output ports.</returns>
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
    }
    /// <summary>
    /// Splits each input element into multiple output elements using a tuple-producing function.
    /// </summary>
    /// <typeparam name="TIn">The element type accepted by the input port and splitter function.</typeparam>
    /// <typeparam name="T0">The element type emitted by output port <c>Out0</c>.</typeparam>
    /// <typeparam name="T1">The element type emitted by output port <c>Out1</c>.</typeparam>
    /// <typeparam name="T2">The element type emitted by output port <c>Out2</c>.</typeparam>
    /// <typeparam name="T3">The element type emitted by output port <c>Out3</c>.</typeparam>
    /// <typeparam name="T4">The element type emitted by output port <c>Out4</c>.</typeparam>
    /// <typeparam name="T5">The element type emitted by output port <c>Out5</c>.</typeparam>
    /// <typeparam name="T6">The element type emitted by output port <c>Out6</c>.</typeparam>
    public class UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>>
    {
        private sealed class UnzipWithStageLogic : InGraphStageLogic 
        {
            private readonly UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> _stage;
            private int _pendingCount = 7;
            private int _downstreamRunning = 7;
            private bool _pending0 = true;
            private bool _pending1 = true;
            private bool _pending2 = true;
            private bool _pending3 = true;
            private bool _pending4 = true;
            private bool _pending5 = true;
            private bool _pending6 = true;
    
            public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);				
                
                SetHandler(stage.Out0, onPull: () => {
                    _pendingCount--;
                    _pending0 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending0) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out1, onPull: () => {
                    _pendingCount--;
                    _pending1 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending1) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out2, onPull: () => {
                    _pendingCount--;
                    _pending2 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending2) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out3, onPull: () => {
                    _pendingCount--;
                    _pending3 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending3) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out4, onPull: () => {
                    _pendingCount--;
                    _pending4 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending4) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out5, onPull: () => {
                    _pendingCount--;
                    _pending5 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending5) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
                SetHandler(stage.Out6, onPull: () => {
                    _pendingCount--;
                    _pending6 = false;
                    if (_pendingCount == 0) Pull(stage.In);
                },
                onDownstreamFinish: cause => {
                    _downstreamRunning--;
                    if (_downstreamRunning == 0) CancelStage(cause);
                    else 
                    {
                        if (_pending6) _pendingCount--;
                        if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
                    }
                });
                
            }

            public override void  OnPush()
            {
                var elements = _stage._unzipper(Grab(_stage.In));
                    
                if (!IsClosed(_stage.Out0)) 
                {
                    Push(_stage.Out0, elements.Item1);
                    _pending0 = true;
                }
                if (!IsClosed(_stage.Out1)) 
                {
                    Push(_stage.Out1, elements.Item2);
                    _pending1 = true;
                }
                if (!IsClosed(_stage.Out2)) 
                {
                    Push(_stage.Out2, elements.Item3);
                    _pending2 = true;
                }
                if (!IsClosed(_stage.Out3)) 
                {
                    Push(_stage.Out3, elements.Item4);
                    _pending3 = true;
                }
                if (!IsClosed(_stage.Out4)) 
                {
                    Push(_stage.Out4, elements.Item5);
                    _pending4 = true;
                }
                if (!IsClosed(_stage.Out5)) 
                {
                    Push(_stage.Out5, elements.Item6);
                    _pending5 = true;
                }
                if (!IsClosed(_stage.Out6)) 
                {
                    Push(_stage.Out6, elements.Item7);
                    _pending6 = true;
                }
                
                _pendingCount = _downstreamRunning;
            }
        }		

        private readonly Func<TIn, (T0, T1, T2, T3, T4, T5, T6)> _unzipper;
        /// <summary>
        /// Creates a stage that applies <paramref name="unzipper"/> to each input and emits tuple items on separate outputs.
        /// </summary>
        /// <param name="unzipper">A function that maps each input element to a tuple containing one value for each output.</param>
        public UnzipWith(Func<TIn, (T0, T1, T2, T3, T4, T5, T6)> unzipper)
        {
            _unzipper = unzipper;

            InitialAttributes = Attributes.CreateName("UnzipWith");
            Shape = new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>("UnzipWith");
            In = Shape.In;

            Out0 = Shape.Out0;
            Out1 = Shape.Out1;
            Out2 = Shape.Out2;
            Out3 = Shape.Out3;
            Out4 = Shape.Out4;
            Out5 = Shape.Out5;
            Out6 = Shape.Out6;
        }

        /// <summary>
        /// The input port that supplies elements to the splitter.
        /// </summary>
        public Inlet<TIn> In { get; }

        /// <summary>
        /// Output port <c>Out0</c> for tuple item <c>Item1</c>.
        /// </summary>
        public Outlet<T0> Out0 { get; }
        /// <summary>
        /// Output port <c>Out1</c> for tuple item <c>Item2</c>.
        /// </summary>
        public Outlet<T1> Out1 { get; }
        /// <summary>
        /// Output port <c>Out2</c> for tuple item <c>Item3</c>.
        /// </summary>
        public Outlet<T2> Out2 { get; }
        /// <summary>
        /// Output port <c>Out3</c> for tuple item <c>Item4</c>.
        /// </summary>
        public Outlet<T3> Out3 { get; }
        /// <summary>
        /// Output port <c>Out4</c> for tuple item <c>Item5</c>.
        /// </summary>
        public Outlet<T4> Out4 { get; }
        /// <summary>
        /// Output port <c>Out5</c> for tuple item <c>Item6</c>.
        /// </summary>
        public Outlet<T5> Out5 { get; }
        /// <summary>
        /// Output port <c>Out6</c> for tuple item <c>Item7</c>.
        /// </summary>
        public Outlet<T6> Out6 { get; }

        /// <summary>
        /// The attributes initially assigned to this graph stage.
        /// </summary>
        protected sealed override Attributes InitialAttributes { get; }
        /// <summary>
        /// The stage's input and typed output ports.
        /// </summary>
        public sealed override FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6> Shape { get; }
        /// <summary>
        /// Creates the stage logic that applies the splitter and coordinates demand across its outputs.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited from the surrounding graph.</param>
        /// <returns>The logic that handles this stage's input and output ports.</returns>
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
    }
}
