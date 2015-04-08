using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class InfoFilter : EventFilterBase
    {
        public InfoFilter(IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : base(messageMatcher, sourceMatcher)
        {
        }
        protected override bool IsMatch(LogEvent evt)
        {
            var info = evt as Info;
            if(info != null)
            {
                return InternalDoMatch(info.LogSource, info.Message);
            }

            return false;
        }

        protected override string FilterDescriptiveName { get { return "Info"; } }
    }
}