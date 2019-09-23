// //-----------------------------------------------------------------------
// // <copyright file="ExpressionExtensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

// //-----------------------------------------------------------------------
// // <copyright file="ExpressionExtensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Akka.DI.Core
{
    internal static class ExpressionExtensions
    {
        public static Type PropertyType<T>(this Expression<Func<T, object>> selector)
        {
            return PropertyType((LambdaExpression)selector);
        }

        public static Type PropertyType(this LambdaExpression selector)
        {
            return selector.PropertyInfo().PropertyType;
        }

        public static PropertyInfo PropertyInfo(this LambdaExpression selector)
        {
            if (selector == null)
                throw new ArgumentNullException("selector");
            var memberExpression = GetMemberExpression(selector);
            if (memberExpression == null)
                throw new ArgumentException("Must be a MemberExpression.", "selector");

            switch (memberExpression.Member.MemberType)
            {
                case MemberTypes.Property:
                    return (PropertyInfo)memberExpression.Member;
                default:
                    throw new ArgumentException("MemberExpression must use a property or field.", "selector");
            }
        }

        private static MemberExpression GetMemberExpression(LambdaExpression property)
        {
            if (property.Body.NodeType == ExpressionType.Convert)
                return ((UnaryExpression)property.Body).Operand as MemberExpression;
            if (property.Body.NodeType == ExpressionType.MemberAccess)
                return property.Body as MemberExpression;

            return null;
        }
    }
}