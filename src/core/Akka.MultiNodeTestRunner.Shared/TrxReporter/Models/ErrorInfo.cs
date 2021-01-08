//-----------------------------------------------------------------------
// <copyright file="ErrorInfo.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public class ErrorInfo : ITestEntity
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }

        public XElement Serialize()
        {
            return Elem("ErrorInfo",
                Elem("Message", Text(Message ?? "")),
                Elem("StackTrace", Text(StackTrace ?? ""))
            );
        }
    }
}
