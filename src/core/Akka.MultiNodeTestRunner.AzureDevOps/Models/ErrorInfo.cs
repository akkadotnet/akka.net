// -----------------------------------------------------------------------
//  <copyright file="ErrorInfo.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.AzureDevOps.Models
{
    using System.Xml.Linq;

    public class ErrorInfo : ITestEntity
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }

        public XElement Serialize()
        {
            return XmlHelper.Elem("ErrorInfo",
                XmlHelper.Elem("Message", XmlHelper.Text(Message ?? "")),
                XmlHelper.Elem("StackTrace", XmlHelper.Text(StackTrace ?? ""))
            );
        }
    }
}