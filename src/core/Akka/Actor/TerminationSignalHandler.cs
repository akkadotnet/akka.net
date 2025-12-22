//-----------------------------------------------------------------------
// <copyright file="TerminationSignalHandler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
#if NET6_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Akka.Actor
{
    /// <summary>
    /// Abstraction for handling process termination signals (SIGTERM, SIGHUP, ProcessExit).
    /// Required for .NET 10 compatibility where ProcessExit no longer fires on SIGTERM.
    /// </summary>
    internal interface ITerminationSignalHandler : IDisposable
    {
        /// <summary>
        /// Registers a callback to be invoked when a termination signal is received.
        /// The callback will only be invoked once, even if multiple signals are received.
        /// </summary>
        /// <param name="onTerminationSignal">The callback to invoke on termination signal.</param>
        void Register(Action onTerminationSignal);
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// .NET 6+ implementation using PosixSignalRegistration for proper signal handling.
    /// Handles SIGTERM and SIGHUP signals, plus ProcessExit as fallback.
    /// This is required for .NET 10 compatibility where ProcessExit no longer fires on SIGTERM.
    /// </summary>
    internal sealed class PosixTerminationSignalHandler : ITerminationSignalHandler
    {
        private PosixSignalRegistration? _sigtermRegistration;
        private PosixSignalRegistration? _sighupRegistration;
        private EventHandler? _processExitHandler;
        private Action? _callback;
        private int _invoked;

        /// <inheritdoc/>
        public void Register(Action onTerminationSignal)
        {
            _callback = onTerminationSignal;

            // Register POSIX signals (works on Unix/macOS/Windows in .NET 6+)
            _sigtermRegistration = PosixSignalRegistration.Create(
                PosixSignal.SIGTERM, OnSignalReceived);

            _sighupRegistration = PosixSignalRegistration.Create(
                PosixSignal.SIGINT, OnSignalReceived);

            // Keep ProcessExit as fallback for non-signal termination scenarios
            _processExitHandler = (_, _) => InvokeCallback();
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        }

        private void OnSignalReceived(PosixSignalContext context)
        {
            // Cancel default termination to allow graceful shutdown
            context.Cancel = true;
            InvokeCallback();
        }

        private void InvokeCallback()
        {
            // Ensure callback only runs once
            if (Interlocked.CompareExchange(ref _invoked, 1, 0) == 0)
            {
                _callback?.Invoke();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _sigtermRegistration?.Dispose();
            _sighupRegistration?.Dispose();
            if (_processExitHandler != null)
                AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        }
    }
#else
    /// <summary>
    /// Legacy implementation for .NET Standard 2.0 / .NET Framework.
    /// Uses ProcessExit only (no POSIX signal support available).
    /// </summary>
    internal sealed class LegacyTerminationSignalHandler : ITerminationSignalHandler
    {
        private EventHandler? _processExitHandler;
        private int _invoked;

        /// <inheritdoc/>
        public void Register(Action onTerminationSignal)
        {
            _processExitHandler = (_, _) =>
            {
                if (Interlocked.CompareExchange(ref _invoked, 1, 0) == 0)
                {
                    onTerminationSignal();
                }
            };
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_processExitHandler != null)
                AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        }
    }
#endif
}
