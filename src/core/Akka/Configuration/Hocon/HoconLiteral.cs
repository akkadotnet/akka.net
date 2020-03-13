//-----------------------------------------------------------------------
// <copyright file="HoconLiteral.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class represents a string literal element in a HOCON (Human-Optimized Config Object Notation)
    /// configuration string.
    /// <code>
    /// akka {  
    ///   actor {
    ///     provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
    ///   }
    /// }
    /// </code>
    /// </summary>
    public class HoconLiteral : IHoconElement
    {
        /// <summary>
        /// Gets or sets the value of this element.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Determines whether this element is a string.
        /// </summary>
        /// <returns><c>true</c></returns>
        public bool IsString()
        {
            return true;
        }

        /// <summary>
        /// Retrieves the string representation of this element.
        /// </summary>
        /// <returns>The value of this element.</returns>
        public string GetString()
        {
            return Value;
        }

        /// <summary>
        /// Determines whether this element is an array.
        /// </summary>
        /// <returns><c>false</c></returns>
        public bool IsArray()
        {
            return false;
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <returns>N/A</returns>
        /// <exception cref="NotImplementedException">
        /// This exception is thrown automatically since this element is a string literal and not an array.
        /// </exception>
        public IList<HoconValue> GetArray()
        {
            throw new NotImplementedException("This element is a string literal and not an array.");
        }

        /// <summary>
        /// Returns the string representation of this element.
        /// </summary>
        /// <returns>The value of this element.</returns>
        public override string ToString()
        {
            return Value;
        }
    }
}
