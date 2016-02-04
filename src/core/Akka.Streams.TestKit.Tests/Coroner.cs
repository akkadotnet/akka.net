namespace Akka.Streams.TestKit.Tests
{
    public interface IWatchedByCoroner
    {
        
    }

    /**
     * The Coroner can be used to print a diagnostic report of the JVM state,
     * including stack traces and deadlocks. A report can be printed directly, by
     * calling `printReport`. Alternatively, the Coroner can be asked to `watch`
     * the JVM and generate a report at a later time - unless the Coroner is canceled
     * by that time.
     *
     * The latter method is useful for printing diagnostics in the event that, for
     * example, a unit test stalls and fails to cancel the Coroner in time. The
     * Coroner will assume that the test has "died" and print a report to aid in
     * debugging.
     */
    public class Coroner
    {
         
    }
}