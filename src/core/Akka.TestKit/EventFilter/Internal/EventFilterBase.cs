//-----------------------------------------------------------------------
// <copyright file="EventFilterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text;
using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;

namespace Akka.TestKit.Internal
{
    public delegate void EventMatched(EventFilterBase eventFilter, LogEvent logEvent);

    /// <summary>Internal! 
    /// Facilities for selectively filtering out expected events from logging so
    /// that you can keep your test run’s console output clean and do not miss real
    /// error messages.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public abstract class EventFilterBase : IEventFilter
    {
        private readonly IStringMatcher _sourceMatcher;
        private readonly IStringMatcher _messageMatcher;

        protected EventFilterBase(IStringMatcher messageMatcher, IStringMatcher sourceMatcher)
        {
            _messageMatcher = messageMatcher ?? MatchesAll.Instance;
            _sourceMatcher = sourceMatcher ?? MatchesAll.Instance;
        }

        public event EventMatched EventMatched;

        /// <summary>
        /// Determines whether the specified event should be filtered or not.
        /// </summary>
        /// <returns><c>true</c> to filter the event.</returns>
        protected abstract bool IsMatch(LogEvent evt);  //In Akka JVM this is called matches

        public bool Apply(LogEvent logEvent)
        {
            if(IsMatch(logEvent))
            {
                OnEventMatched(logEvent);
                return true;
            }

            return false;
        }

        protected virtual void OnEventMatched(LogEvent logEvent)
        {
            var delegt = EventMatched;
            if(delegt != null) delegt(this, logEvent);
        }

        /// <summary>Internal helper.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        protected bool InternalDoMatch(string src, object msg)
        {
            var msgstr = msg == null ? "null" : msg.ToString();
            return _sourceMatcher.IsMatch(src) && _messageMatcher.IsMatch(msgstr);
        }

        protected abstract string FilterDescriptiveName { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            //if(_occurences > 1)
            //    sb.Append(_occurences == int.MaxValue ? "infinite" : _occurences.ToString(CultureInfo.InvariantCulture)).Append(" occurences of ");
            sb.Append(FilterDescriptiveName);
            var hasMessageMatcher = !(_messageMatcher is MatchesAll);
            var hasSourceMatcher = !(_sourceMatcher is MatchesAll);
            var hasBothMessageAndSourceMatcher = hasMessageMatcher && hasSourceMatcher;
            if(hasMessageMatcher || hasSourceMatcher)
            {
                sb.Append(" when");
            }
            if(hasMessageMatcher)
            {
                sb.Append(" Message ");
                sb.Append(_messageMatcher);
            }
            if(hasBothMessageAndSourceMatcher)
            {
                sb.Append(" and");
            }
            if(hasSourceMatcher)
            {
                sb.Append(" Source ");
                sb.Append(_sourceMatcher);
            }
            return sb.ToString();
        }
    }
}
