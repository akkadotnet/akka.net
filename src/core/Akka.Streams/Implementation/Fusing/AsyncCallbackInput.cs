//-----------------------------------------------------------------------
// <copyright file="AsyncCallbackInput.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation.Fusing
{
    // Callback registrations own their stage and handler. Events carry only arguments and feedback.
    internal readonly struct AsyncCallbackArguments
    {
        internal readonly object? First;
        internal readonly object? Second;

        internal AsyncCallbackArguments(object? first, object? second)
        {
            First = first;
            Second = second;
        }
    }

    internal interface IAsyncCallbackTarget
    {
        GraphStageLogic Logic { get; }
        Action<object> LegacyHandler { get; }
        void Invoke(AsyncCallbackArguments arguments);
    }

    internal readonly struct AsyncCallbackInput : ActorGraphInterpreter.IBoundaryEvent
    {
        public GraphInterpreterShell Shell { get; }
        internal readonly IAsyncCallbackTarget Target;
        internal readonly AsyncCallbackArguments Arguments;
        internal readonly TaskCompletionSource<Done> Promise;

        internal AsyncCallbackInput(GraphInterpreterShell shell, IAsyncCallbackTarget target,
            AsyncCallbackArguments arguments, TaskCompletionSource<Done> promise)
        {
            Shell = shell;
            Target = target;
            Arguments = arguments;
            Promise = promise;
        }
    }
}
