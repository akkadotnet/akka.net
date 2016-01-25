//-----------------------------------------------------------------------
// <copyright file="HoconLiteral.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// Retrieves a list of elements associated with this element.
        /// </summary>
        /// <returns>
        /// A list of elements associated with this element.
        /// </returns>
        /// <exception cref="System.NotImplementedException">
        /// This element is a string literal. It is not an array.
        /// Therefore this method will throw an exception.
        /// </exception>
        public IList<HoconValue> GetArray()
        {
            throw new NotImplementedException();
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

