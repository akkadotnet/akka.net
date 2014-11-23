using System;
using Akka.Configuration;

namespace Akka.Util
{
    /// <summary>
    /// Class TypeExtensions.
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>
        /// Returns true if <paramref name="type" /> implements/inherits <typeparamref name="T" />.
        /// <example><para>typeof(object[]).Implements&lt;IEnumerable&gt;() --&gt; true</para></example>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public static bool Implements<T>(this Type type)
        {
            return Implements(type, typeof(T));
        }

        /// <summary>
        /// Returns true if <paramref name="type" /> implements/inherits <paramref name="moreGeneralType" />.
        /// <example><para>typeof(object[]).Implements(typeof(IEnumerable)) --&gt; true</para></example>
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="moreGeneralType">Type of the more general.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public static bool Implements(this Type type, Type moreGeneralType)
        {
            return moreGeneralType.IsAssignableFrom(type);
        }

        /// <summary>
        /// Resolvesa Type from typeName, throws ArgumentException if typeName is not a valid type.
        /// </summary>
        /// <param name="typeName">Name of the type to resolve</param>
        /// <returns>A resolved type</returns>
        public static Type ResolveType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type == null)
            {
                throw new ArgumentException("Provided type name did not resolve an actual type", "typeName");
            }
            return type;
        }

        /// <summary>
        /// Resolvesa Type from typeName.
        /// Throws ArgumentException if typeName is not a valid type.
        /// </summary>
        /// <param name="typeName"></param>
        /// <param name="implementsType"></param>
        /// <returns></returns>
        public static Type ResolveType(string typeName, Type implementsType)
        {
            var type = ResolveType(typeName);
            if (implementsType == null) 
                return type;

            if (!implementsType.IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    string.Format("Provided type name '{0}' is not assignable to type '{1}'", typeName,
                        implementsType.Name));
            }
            return type;
        }

        /// <summary>
        /// Resolves a Type from typeName.
        /// Throws ConfigurationException if typeName is not a valid type.
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        public static Type ResolveConfiguredType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type == null)
            {
                throw new ConfigurationException(string.Format("Provided type name '{0}' did not resolve an actual type", typeName));
            }

            return type;
        }

        /// <summary>
        /// Resolves a Type from typeName.
        /// Throws ConfigurationException if typeName is not a valid type.
        /// Throws ConfigurationException if resolved type is not compatible with implementsType.
        /// </summary>
        /// <param name="typeName"></param>
        /// <param name="implementsType"></param>
        /// <returns></returns>
        public static Type ResolveConfiguredType(string typeName,Type implementsType)
        {
            var type = ResolveConfiguredType(typeName);
            if (implementsType == null) 
                return type;

            if (!implementsType.IsAssignableFrom(type))
            {
                throw new ConfigurationException(
                    string.Format("Provided type name '{0}' is not assignable to type '{1}'", typeName,
                        implementsType.Name));
            }
            return type;
        }
    }
}