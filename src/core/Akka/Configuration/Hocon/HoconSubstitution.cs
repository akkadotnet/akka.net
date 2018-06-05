//-----------------------------------------------------------------------
// <copyright file="HoconSubstitution.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class represents a substitution element in a HOCON (Human-Optimized Config Object Notation)
    /// configuration string.
    /// <code>
    /// akka {  
    ///   defaultInstances = 10
    ///   deployment{
    ///     /user/time{
    ///       nr-of-instances = $defaultInstances
    ///     }
    ///   }
    /// }
    /// </code>
    /// </summary>
    public class HoconSubstitution : IHoconElement, IMightBeAHoconObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HoconSubstitution"/> class.
        /// </summary>
        protected HoconSubstitution()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="HoconSubstitution" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        public HoconSubstitution(string path)
        {
            Path = path;
        }

        /// <summary>
        ///     The full path to the value which should substitute this instance.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        ///     The evaluated value from the Path property
        /// </summary>
        public HoconValue ResolvedValue { get; set; }

        /// <summary>
        /// Determines whether this element is a string.
        /// </summary>
        /// <returns><c>true</c> if this element is a string; otherwise <c>false</c></returns>
        public bool IsString()
        {
            return ResolvedValue.IsString();
        }

        /// <summary>
        /// Retrieves the string representation of this element.
        /// </summary>
        /// <returns>The string representation of this element.</returns>
        public string GetString()
        {
            return ResolvedValue.GetString();
        }

        /// <summary>
        /// Determines whether this element is an array.
        /// </summary>
        /// <returns><c>true</c> if this element is aan array; otherwise <c>false</c></returns>
        public bool IsArray()
        {
            return ResolvedValue.IsArray();
        }

        /// <summary>
        /// Retrieves a list of elements associated with this element.
        /// </summary>
        /// <returns>A list of elements associated with this element.</returns>
        public IList<HoconValue> GetArray()
        {
            return ResolvedValue.GetArray();
        }

        /// <summary>
        /// Determines whether this element is a HOCON object.
        /// </summary>
        /// <returns><c>true</c> if this element is a HOCON object; otherwise <c>false</c></returns>
        public bool IsObject()
        {
            return ResolvedValue != null && ResolvedValue.IsObject();
        }

        /// <summary>
        /// Retrieves the HOCON object representation of this element.
        /// </summary>
        /// <returns>The HOCON object representation of this element.</returns>
        public HoconObject GetObject()
        {
            return ResolvedValue.GetObject();
        }
    }
}

