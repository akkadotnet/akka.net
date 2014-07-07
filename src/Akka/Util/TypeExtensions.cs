using System;

namespace Akka.Util
{
    public static class TypeExtensions
    {
        /// <summary>
        /// Returns true if <paramref name="type"/> implements/inherits <typeparamref name="T"/>.
        /// <example>
        /// <para>typeof(object[]).Implements&lt;IEnumerable&gt;() --> true</para>
        /// </example>
        /// </summary>
        public static bool Implements<T>(this Type type)
        {
            return Implements(type, typeof(T));
        }

        /// <summary>
        /// Returns true if <paramref name="type"/> implements/inherits <paramref name="moreGeneralType"/>.
        /// <example>
        /// <para>typeof(object[]).Implements(typeof(IEnumerable)) --> true</para>
        /// </example>
        /// </summary>
        public static bool Implements(this Type type, Type moreGeneralType)
        {
            return moreGeneralType.IsAssignableFrom(type);
        }
    }
}