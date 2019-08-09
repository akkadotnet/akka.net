namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;

    public interface IJournalWriteInterceptor
    {
        Task InterceptAsync(IPersistentRepresentation message);
    }
}