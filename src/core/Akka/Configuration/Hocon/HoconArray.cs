using System;
using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// Class HoconArray.
    /// </summary>
    public class HoconArray : List<HoconValue>, IHoconElement
    {
        /// <summary>
        /// Determines whether this instance is string.
        /// </summary>
        /// <returns><c>true</c> if this instance is string; otherwise, <c>false</c>.</returns>
        public bool IsString()
        {
            return false;
        }

        /// <summary>
        /// Gets the string.
        /// </summary>
        /// <returns>System.String.</returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public string GetString()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Determines whether this instance is array.
        /// </summary>
        /// <returns><c>true</c> if this instance is array; otherwise, <c>false</c>.</returns>
        public bool IsArray()
        {
            return true;
        }

        /// <summary>
        /// Gets the array.
        /// </summary>
        /// <returns>IList&lt;HoconValue&gt;.</returns>
        public IList<HoconValue> GetArray()
        {
            return this;
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return "[" + string.Join(",", this) + "]";
        }
    }
}