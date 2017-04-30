using System;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Akka.Actor;

namespace Akka.Util.Reflection
{
    internal static class ReflectionHelpers
    {
        private sealed class Entry
        {
            public readonly string Name;
            public readonly Type Type;
            public readonly object Value;

            public Entry(string name, Type type, object value = null)
            {
                Name = name;
                Type = type;
                Value = value;
            }

            public override int GetHashCode()
            {
                return Name.GetHashCode() ^ Type.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                var other = obj as Entry;
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Name == other.Name && Type == other.Type;
            }
        }

        public static MethodInfo DependencyResolverResolve = typeof(IDependencyResolver).GetMethod(nameof(IDependencyResolver.Resolve));

        public static Func<object> ResolveWithArgs(this IDependencyResolver resolver, Type type, params object[] args)
        {
            if (args.Length == 0)
            {
                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor != null)
                {
                    var expr = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object)));
                    return expr.Compile();
                }
                else
                {
                    return resolver?.ResolveProducer(type);
                }
            }
            else
            {
                foreach (var ctor in type.GetConstructors())
                {
                    var parameters = ctor.GetParameters();
                    if (parameters.Length == args.Length)
                    {
                        var found = true;
                        var exprs = new Expression[args.Length];
                        for (int i = 0; i < args.Length; i++)
                        {
                            var arg = args[i];
                            if (arg != null)
                            {
                                var parameter = parameters[i];
                                if (!parameter.ParameterType.IsInstanceOfType(arg))
                                {
                                    found = false;
                                    break;
                                }

                                exprs[i] = Expression.Constant(arg, parameter.ParameterType);
                            }
                            else if (resolver != null)
                            {
                                exprs[i] = Expression.Call(Expression.Constant(resolver),
                                    ReflectionHelpers.DependencyResolverResolve,
                                    Expression.Constant(parameters[i].ParameterType));
                            }
                            else return null;
                        }

                        if (found)
                        {
                            var ctorExpr = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor, exprs), typeof(object)));
                            return ctorExpr.Compile();
                        }
                    }
                }

                return null;
            }
        }

        public static object[] ArgsFromDynamic(Type type, object dynamicObject)
        {
            var properties = dynamicObject
                .GetType()
                .GetProperties()
                .Select(p => new Entry(p.Name, p.PropertyType, p.GetValue(dynamicObject)))
                .ToImmutableHashSet();
            foreach (var ctor in type.GetConstructors())
            {
                var parameters = ctor.GetParameters()
                    .Select(p => new Entry(p.Name, p.ParameterType))
                    .ToArray();
                if (properties.IsSubsetOf(parameters))
                {
                    var args = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        Entry e;
                        args[i] = properties.TryGetValue(parameters[i], out e) ? e.Value : null;
                    }

                    return args;
                }
            }

            return new object[0];
        }
    }
}