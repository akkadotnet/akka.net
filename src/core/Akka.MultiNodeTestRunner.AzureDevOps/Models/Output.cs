// -----------------------------------------------------------------------
//  <copyright file="Output.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.AzureDevOps.Models
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;

    public class Output : ITestEntity
    {
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public string DebugTrace { get; set; }
        public ErrorInfo ErrorInfo { get; set; }
        public List<string> TextMessages { get; } = new List<string>();

        public XElement Serialize()
        {
            return XmlHelper.Elem("Output",
                XmlHelper.Elem("StdOut", XmlHelper.Text(StdOut)),
                XmlHelper.Elem("StdErr", XmlHelper.Text(StdErr)),
                XmlHelper.Elem("DebugTrace", XmlHelper.Text(DebugTrace)),
                ErrorInfo,
                XmlHelper.ElemList(
                    "TextMessages",
                    TextMessages.Select(x => XmlHelper.Elem("Message", XmlHelper.Text(x)))
                )
            );
        }
    }
}
