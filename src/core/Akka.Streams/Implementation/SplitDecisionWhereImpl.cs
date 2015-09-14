namespace Akka.Streams.Implementation
{
    public enum SplitDecision
    {
        /** Splits before the current element. The current element will be the first element in the new substream. */
        SplitBefore,
        /** Splits after the current element. The current element will be the last element in the current substream. */
        SplitAfter,
        /** Emit this element into the current substream. */
        Continue
    }

    public class SplitDecisionWhereImpl
    {

    }
}