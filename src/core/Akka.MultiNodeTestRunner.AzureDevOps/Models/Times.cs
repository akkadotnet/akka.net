// -----------------------------------------------------------------------
//  <copyright file="Times.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.AzureDevOps.Models
{
    using System;
    using System.Xml.Linq;

    public class Times : ITestEntity
    {
        public Times()
        {
            var now = DateTime.UtcNow;
            
            Creation = now;
            Queuing = now;
            Start = now;
            Finish = now;
        }

        public DateTime Creation { get; set; }
        public DateTime Queuing { get; set; }
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }

        public XElement Serialize() => XmlHelper.Elem("Times",
            XmlHelper.Attr("creation", Creation.ToString("O")),
            XmlHelper.Attr("queuing", Queuing.ToString("O")),
            XmlHelper.Attr("start", Start.ToString("O")),
            XmlHelper.Attr("finish", Finish.ToString("O"))
        );
    }
}