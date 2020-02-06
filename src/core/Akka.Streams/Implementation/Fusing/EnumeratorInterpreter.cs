//-----------------------------------------------------------------------
// <copyright file="EnumeratorInterpreter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class EnumeratorInterpreter
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        public sealed class EnumeratorUpstream<TIn> : GraphInterpreter.UpstreamBoundaryStageLogic
        {
            /// <summary>
            /// TBD
            /// </summary>
            public bool HasNext;

            private readonly Outlet<TIn> _outlet;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="input">TBD</param>
            public EnumeratorUpstream(IEnumerator<TIn> input)
            {
                _outlet = new Outlet<TIn>("IteratorUpstream.out") { Id = 0 };

                SetHandler(_outlet, onPull: () =>
                {
                    if (!HasNext) CompleteStage();
                    else
                    {
                        var element = input.Current;
                        HasNext = input.MoveNext();
                        if (!HasNext)
                        {
                            Push(_outlet, element);
                            Complete(_outlet);
                        }
                        else Push(_outlet, element);
                    }
                },
                onDownstreamFinish: CompleteStage);
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override Outlet Out => _outlet;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        public sealed class EnumeratorDownstream<TOut> : GraphInterpreter.DownstreamBoundaryStageLogic, IEnumerator<TOut>
        {
            /// <summary>
            /// TBD
            /// </summary>
            internal bool IsDone;
            /// <summary>
            /// TBD
            /// </summary>
            internal TOut NextElement;
            /// <summary>
            /// TBD
            /// </summary>
            internal bool NeedsPull = true;
            /// <summary>
            /// TBD
            /// </summary>
            internal Exception LastFailure;

            private readonly Inlet<TOut> _inlet;

            /// <summary>
            /// TBD
            /// </summary>
            public EnumeratorDownstream()
            {
                _inlet = new Inlet<TOut>("IteratorDownstream.in") { Id = 0 };

                SetHandler(_inlet, onPush: () =>
                {
                    NextElement = Grab(_inlet);
                    NeedsPull = false;
                }, 
                onUpstreamFinish: () =>
                {
                    IsDone = true;
                    CompleteStage();
                }, 
                onUpstreamFailure: cause =>
                {
                    IsDone = true;
                    LastFailure = cause;
                    CompleteStage();
                });
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override Inlet In => _inlet;

            /// <summary>
            /// TBD
            /// </summary>
            public void Dispose() { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public bool MoveNext()
            {
                if (LastFailure != null)
                {
                    var e = LastFailure;
                    LastFailure = null;
                    throw e;
                }
                if (!HasNext())
                    return false;

                NeedsPull = true;
                return true;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void Reset()
            {
                IsDone = false;
                NextElement = default(TOut);
                NeedsPull = true;
                LastFailure = null;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public bool HasNext()
            {
                if(!IsDone)
                    PullIfNeeded();

                return !(IsDone && NeedsPull) || LastFailure != null;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TOut Current => NextElement;

            object IEnumerator.Current => Current;

            private void PullIfNeeded()
            {
                if (NeedsPull)
                {
                    Pull(_inlet);
                    Interpreter.Execute(int.MaxValue);
                }
            }
        }
    }
}
