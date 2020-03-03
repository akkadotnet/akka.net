//-----------------------------------------------------------------------
// <copyright file="TimelineItem.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    public class TimelineItem
    {
        private const string EventFormat = "{{ className:'{0}', content:'{1}', start:'{2}', group:{3}, title:'{4}' }}";

        public TimelineItem(string cssClass, string content, string title, DateTime dateTime, int groupId)
        {
            Classname = cssClass;
            Content = content;
            Start = dateTime;
            GroupId = groupId;
            Title = title;
        }

        public string Classname { get; private set; }

        public string Content { get; private set; }

        public string Title { get; private set; }

        public DateTime Start { get; private set; }

        public int GroupId { get; private set; }

        public string ToJavascriptString()
        {
            return string.Format(EventFormat, Classname, Content, Start.ToString("o"), GroupId, Title);
        }
    }
}
