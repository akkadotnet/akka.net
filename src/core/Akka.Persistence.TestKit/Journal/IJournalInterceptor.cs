//-----------------------------------------------------------------------
// <copyright file="IJournalInterceptor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;

    /// <summary>
    ///     Interface to object which will intercept written and recovered messages in <see cref="TestJournal"/>.
    /// </summary>
    public interface IJournalInterceptor
    {
        /// <summary>
        ///     Method will be called for each individual message before it is written or recovered.
        /// </summary>
        /// <param name="message">Written or recovered message.</param>
        Task InterceptAsync(IPersistentRepresentation message);
    }
}
