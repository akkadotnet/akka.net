//-----------------------------------------------------------------------
// <copyright file="Protocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;

namespace Akka.Persistence.Embedded.Internal
{
    /// <summary>Sent to the journal (or snapshot store) to learn when its tables are ready.</summary>
    internal sealed class EnsureInitialized
    {
        public static EnsureInitialized Instance { get; } = new();

        private EnsureInitialized()
        {
        }
    }

    /// <summary>Reply to <see cref="EnsureInitialized"/>.</summary>
    internal sealed class Initialized
    {
        public static Initialized Instance { get; } = new();

        private Initialized()
        {
        }
    }

    /// <summary>The journal tells itself when a write for a persistence id has finished.</summary>
    internal sealed class WriteFinished
    {
        public WriteFinished(string persistenceId, Task future)
        {
            PersistenceId = persistenceId;
            Future = future;
        }

        public string PersistenceId { get; }

        public Task Future { get; }
    }
}
