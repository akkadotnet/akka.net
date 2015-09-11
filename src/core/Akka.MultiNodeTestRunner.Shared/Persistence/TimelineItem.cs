// -----------------------------------------------------------------------
//  <copyright file="TimelineItem.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    public class TimelineItem
    {
        private static readonly string eventFormat = "{{ className:'{0}', content:'{1}', start:'{2}', group:{3} }}";

        private static readonly string[] CssClasses =
            {
                "vis-item-one",
                "vis-item-two",
                "vis-item-three",
                "vis-item-four",
                "vis-item-five",
                "vis-item-six",
                "vis-item-seven",
                "vis-item-eight",
                "vis-item-nine",
                "vis-item-ten",
                "vis-item-eleven",
                "vis-item-twelve",
                "vis-item-thirteen",
                "vis-item-fourteen",
                "vis-item-fifteen"
            };

        public string Classname { get; private set; }

        public string Content { get; private set; }

        public DateTime Start { get; private set; }

        public int GroupId { get; private set; }

        public string ToJavascriptString()
        {
            return string.Format(eventFormat, Classname, Content, Start.ToString("o"), GroupId);
        }

        public static TimelineItem CreateSpecMessage(string content, int groupId, long startTimeStamp)
        {
            return new TimelineItem
                       {
                           Classname = "timeline-message",
                           Content = content,
                           Start = new DateTime(startTimeStamp),
                           GroupId = groupId
                       };
        }

        public static TimelineItem CreateNodeFact(string content, int groupId, long startTimeStamp)
        {
            return new TimelineItem
                       {
                           Classname = CssClasses[startTimeStamp % 15],
                           Content = content,
                           Start = new DateTime(startTimeStamp),
                           GroupId = groupId
                       };
        }
    }
}