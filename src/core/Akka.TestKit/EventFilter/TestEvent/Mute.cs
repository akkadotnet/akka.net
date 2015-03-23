using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Internal;

namespace Akka.TestKit.TestEvent
{
    public sealed class Mute : NoSerializationVerificationNeeded
    {
        private readonly IReadOnlyCollection<EventFilterBase> _filters;

        public Mute(params EventFilterBase[] filters)
        {
            _filters = filters;
        }

        public Mute(IReadOnlyCollection<EventFilterBase> filters)
        {
            _filters = filters;
        }

        public IReadOnlyCollection<EventFilterBase> Filters { get { return _filters; } }
    }
}