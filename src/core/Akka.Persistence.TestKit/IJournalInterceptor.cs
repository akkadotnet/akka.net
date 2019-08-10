namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;

    public interface IJournalInterceptor
    {
        Task InterceptAsync(IPersistentRepresentation message);
    }
}