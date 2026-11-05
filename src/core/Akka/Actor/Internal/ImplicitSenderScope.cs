//-----------------------------------------------------------------------
// <copyright file="ImplicitSenderScope.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;

namespace Akka.Actor.Internal
{
    internal sealed class ImplicitSenderScope : IDisposable
    {
        private static readonly AsyncLocal<ImplicitSenderScope?> Ambient = new();
        private readonly ImplicitSenderScope? _parent;
        private Func<IActorRef?>? _resolver;

        private ImplicitSenderScope(Func<IActorRef?> resolver)
        {
            _resolver = resolver;
            _parent = Ambient.Value;
        }

        internal static ImplicitSenderScope Enter(Func<IActorRef?> resolver)
        {
            var scope = new ImplicitSenderScope(resolver);
            Ambient.Value = scope;
            return scope;
        }

        internal static IActorRef? Current
        {
            get
            {
                var scope = Ambient.Value;
                return scope is null ? null : Volatile.Read(ref scope._resolver)?.Invoke();
            }
        }

        internal void Detach()
        {
            if (ReferenceEquals(Ambient.Value, this))
                Ambient.Value = _parent;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _resolver, null);
            Detach();
        }
    }
}
