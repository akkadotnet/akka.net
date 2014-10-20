using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit.Internal;

namespace Akka.TestKit.TestEvent
{
    public sealed class Unmute : NoSerializationVerificationNeeded
    {
        private readonly IReadOnlyCollection<EventFilterBase> _filters;

        public Unmute(params EventFilterBase[] filters)
        {
            _filters = filters;
        }

        public Unmute(IReadOnlyCollection<EventFilterBase> filters)
        {
            _filters = filters;
        }

        public IReadOnlyCollection<EventFilterBase> Filters { get { return _filters; } }
    }
}