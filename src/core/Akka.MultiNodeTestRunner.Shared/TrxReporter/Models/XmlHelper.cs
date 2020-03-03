//-----------------------------------------------------------------------
// <copyright file="XmlHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    internal static class XmlHelper
    {
        public static readonly XNamespace NS = @"http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        private static XElement CreateElement(string name, IEnumerable<object> content)
        {
            var items = new List<object>();
            foreach (var item in content ?? Enumerable.Empty<object>())
            {
                if (item == null)
                {
                    continue;
                }

                switch (item)
                {
                    case ITestEntity te:
                        items.Add(te.Serialize());
                        break;

                    default:
                        items.Add(item);
                        break;
                }
            }

            return new XElement(NS + name, items.ToArray());
        }

        public static XElement Elem(string name, params object[] content) => CreateElement(name, content);

        public static XElement Elem(string name, IEnumerable<object> content) => CreateElement(name, content);

        public static XAttribute Attr(string name, object value) => value == null ? null : new XAttribute(name, value);

        public static XText Text(string value) => string.IsNullOrWhiteSpace(value) ? null : new XText(value);

        private static XElement CreateElementList<T>(string containerElement, IEnumerable<T> items)
        {
            // ReSharper disable once PossibleMultipleEnumeration
            if (items == null || !items.Any())
            {
                return null;
            }

            // ReSharper disable once PossibleMultipleEnumeration
            return CreateElement(containerElement, items.Cast<object>());
        }

        public static XElement ElemList(string containerElement, IEnumerable<XElement> items) => CreateElementList(containerElement, items);

        public static XElement ElemList(string containerElement, IEnumerable<ITestEntity> items) => CreateElementList(containerElement, items);
    }
}
