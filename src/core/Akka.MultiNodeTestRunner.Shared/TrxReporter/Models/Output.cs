//-----------------------------------------------------------------------
// <copyright file="Output.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public class Output : ITestEntity
    {
        public List<string> StdOut { get; } = new List<string>();
        public List<string> StdErr { get; } = new List<string>();
        public List<string> DebugTrace { get; } = new List<string>();
        public ErrorInfo ErrorInfo { get; set; }
        public List<string> TextMessages { get; } = new List<string>();

        public XElement Serialize()
        {
            XElement TextElem(string element, List<string> lines) =>
                lines.Count > 0
                    ? Elem(element, Text(string.Join(Environment.NewLine, lines)))
                    : null;

            return Elem("Output",
                TextElem("StdOut", StdOut),
                TextElem("StdErr", StdErr),
                TextElem("DebugTrace", DebugTrace),
                ErrorInfo,
                ElemList(
                    "TextMessages",
                    TextMessages.Select(x => Elem("Message", Text(x)))
                )
            );
        }
    }
}
