//-----------------------------------------------------------------------
// <copyright file="IHoconElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// Marker interface to make it easier to retrieve HOCON
    /// (Human-Optimized Config Object Notation) objects for
    /// substitutions.
    /// </summary>
    public interface IMightBeAHoconObject
    {
        /// <summary>
        /// Determines whether this element is a HOCON object.
        /// </summary>
        /// <returns><c>true</c> if this element is a HOCON object; otherwise <c>false</c></returns>
        bool IsObject();

        /// <summary>
        /// Retrieves the HOCON object representation of this element.
        /// </summary>
        /// <returns>The HOCON object representation of this element.</returns>
        HoconObject GetObject();
    }

    /// <summary>
    /// This interface defines the contract needed to implement
    /// a HOCON (Human-Optimized Config Object Notation) element.
    /// </summary>
    public interface IHoconElement
    {
        /// <summary>
        /// Determines whether this element is a string.
        /// </summary>
        /// <returns><c>true</c> if this element is a string; otherwise <c>false</c></returns>
        bool IsString();
        /// <summary>
        /// Retrieves the string representation of this element.
        /// </summary>
        /// <returns>The string representation of this element.</returns>
        string GetString();
        /// <summary>
        /// Determines whether this element is an array.
        /// </summary>
        /// <returns><c>true</c> if this element is aan array; otherwise <c>false</c></returns>
        bool IsArray();
        /// <summary>
        /// Retrieves a list of elements associated with this element.
        /// </summary>
        /// <returns>A list of elements associated with this element.</returns>
        IList<HoconValue> GetArray();
    }
}

