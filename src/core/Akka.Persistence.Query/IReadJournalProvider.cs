namespace Akka.Persistence.Query
{
    /// <summary>
    /// A query plugin must implement a class that implements this trait.
    /// It provides the concrete implementations for the Java and Scala APIs.
    /// 
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