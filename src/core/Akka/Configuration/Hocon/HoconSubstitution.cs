using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    ///     HOCON Substitution, e.g. $foo.bar
    /// </summary>
    public class HoconSubstitution : IHoconElement, IMightBeAHoconObject
    {
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
        /// <value>The path.</value>
        public string Path { get; private set; }

        /// <summary>
        ///     The evaluated value from the Path property
        /// </summary>
        /// <value>The resolved value.</value>
        public HoconValue ResolvedValue { get; set; }

        /// <summary>
        ///     Determines whether this instance is string.
        /// </summary>
        /// <returns><c>true</c> if this instance is string; otherwise, <c>false</c>.</returns>
        public bool IsString()
        {
            return ResolvedValue.IsString();
        }

        /// <summary>
        ///     Returns the value of this instance as a string.
        /// </summary>
        /// <returns>System.String.</returns>
        public string GetString()
        {
            return ResolvedValue.GetString();
        }

        /// <summary>
        ///     Determines whether this instance is array.
        /// </summary>
        /// <returns><c>true</c> if this instance is array; otherwise, <c>false</c>.</returns>
        public bool IsArray()
        {
            return ResolvedValue.IsArray();
        }

        /// <summary>
        ///     Returns the value of this instance as an array.
        /// </summary>
        /// <returns>IList&lt;HoconValue&gt;.</returns>
        public IList<HoconValue> GetArray()
        {
            return ResolvedValue.GetArray();
        }

        /// <summary>
        ///     Determines whether this instance is an HOCON object.
        /// </summary>
        /// <returns><c>true</c> if this instance is object; otherwise, <c>false</c>.</returns>
        public bool IsObject()
        {
            return ResolvedValue != null && ResolvedValue.IsObject();
        }

        /// <summary>
        ///     Returns the value of this instance as an HOCON object.
        /// </summary>
        /// <returns>HoconObject.</returns>
        public HoconObject GetObject()
        {
            return ResolvedValue.GetObject();
        }

        #region Implicit operators

        /// <summary>
        ///     Performs an implicit conversion from <see cref="HoconSubstitution" /> to <see cref="HoconObject" />.
        /// </summary>
        /// <param name="substitution">The substitution.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator HoconObject(HoconSubstitution substitution)
        {
            return substitution.GetObject();
        }

        #endregion
    }
}