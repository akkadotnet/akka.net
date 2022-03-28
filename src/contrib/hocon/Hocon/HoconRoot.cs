//-----------------------------------------------------------------------
// <copyright file="HoconRoot.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hocon.Abstraction;

namespace Hocon
{
    /// <summary>
    /// This class represents the root element in a HOCON (Human-Optimized Config Object Notation)
    /// configuration string.
    /// </summary>
    public class HoconRoot: IHoconRoot
    {
        /*
        /// <summary>
        /// Initializes a new instance of the <see cref="HoconRoot"/> class.
        /// </summary>
        protected HoconRoot()
        {
        }
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="HoconRoot"/> class.
        /// </summary>
        /// <param name="value">The value to associate with this element.</param>
        /// <param name="substitutions">An enumeration of substitutions to associate with this element.</param>
        public HoconRoot(IHoconValue value, IEnumerable<IHoconSubstitution> substitutions)
        {
            Value = value;
            Substitutions = substitutions;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HoconRoot"/> class.
        /// </summary>
        /// <param name="value">The value to associate with this element.</param>
        public HoconRoot(IHoconValue value)
        {
            Value = value;
            Substitutions = Enumerable.Empty<HoconSubstitution>();
        }

        /// <summary>
        /// Retrieves the value associated with this element.
        /// </summary>
        public IHoconValue Value { get; private set; }
        /// <summary>
        /// Retrieves an enumeration of substitutions associated with this element.
        /// </summary>
        public IEnumerable<IHoconSubstitution> Substitutions { get; private set; }
        
        public IHoconValue? GetNode(string path)
        {
            var parsedPath = path.SplitDottedPathHonouringQuotes();
            var currentNode = Value;
            if (currentNode == null)
            {
                throw new InvalidOperationException("Current node should not be null");
            }
            foreach (var key in parsedPath)
            {
                currentNode = currentNode.GetChildObject(key);
                if (currentNode == null) return null;
            }
            return currentNode;
        }
        
    }
}

