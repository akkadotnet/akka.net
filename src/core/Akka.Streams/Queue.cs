using System.Threading.Tasks;

namespace Akka.Streams
{
    /// <summary>
    /// This interface allows to have the queue as a data source for some stream.
    /// </summary>
    public interface ISourceQueue<in T>
    {
        /// <summary>
        /// Method offers next element to a stream and returns task that:
        /// <para>- competes with true if element is consumed by a stream</para>
        /// <para>- competes with false when stream dropped offered element</para>
        /// <para>- fails if stream is completed or cancelled.</para>
        /// </summary>
        /// <param name="element">element to send to a stream</param>
        Task<bool> OfferAsync(T element);
    }

    /// <summary>
    /// Trait allows to have the queue as a sink for some stream.
    /// "SinkQueue" pulls data from stream with backpressure mechanism.
    /// </summary>
    public interface ISinkQueue<T>
    {
        /// <summary>
        /// Method pulls elements from stream and returns task that:
        /// <para>- fails if stream is finished</para>
        /// <para>- completes with None in case if stream is completed after we got task</para>
        /// <para>- completes with `Some(element)` in case next element is available from stream.</para>
        /// </summary>
        Task<T> PullAsync();
    }
}