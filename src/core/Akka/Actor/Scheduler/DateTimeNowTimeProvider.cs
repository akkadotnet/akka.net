using System;

namespace Akka.Actor
{
    public class DateTimeOffsetNowTimeProvider : ITimeProvider, IDateTimeOffsetNowTimeProvider
    {
        private static readonly DateTimeOffsetNowTimeProvider _instance = new DateTimeOffsetNowTimeProvider();
        private DateTimeOffsetNowTimeProvider() { }
        public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } }

        public static DateTimeOffsetNowTimeProvider Instance { get { return _instance; } }
    }
}