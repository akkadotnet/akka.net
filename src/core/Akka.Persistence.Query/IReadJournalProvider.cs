//-----------------------------------------------------------------------
// <copyright file="IReadJournalProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.Query
{
    /// <summary>
    /// A query plugin must implement a class that implements this interface. 
    /// A read journal plugin must provide implementations for <see cref="IReadJournal"/>.
    /// </summary> 
    public interface IReadJournalProvider
    {
        /// <summary>
        /// This corresponds to the instance that is returned by <see cref="PersistenceQuery.ReadJournalFor{TJournal}"/>
        /// </summary>
        IReadJournal GetReadJournal();
    }
}
