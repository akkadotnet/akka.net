// -----------------------------------------------------------------------
//  <copyright file="TypeLoader.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Configuration;

public static class TypeLoader
{
    private static readonly Dictionary<TypeInfo, Type> TypeMap = new();

    public static void Register<T>()
        => Register(typeof(T));
    
    public static void Register(Type type)
    {
        TypeMap[TypeInfo.Create(type)] = type;
    }

    public static void Register(string typeName, Type type)
    {
        TypeMap[TypeInfo.Parse(typeName)] = type;
    }

    public static Type? GetType(string typeName, bool throwOnError = false)
    {
        if(string.IsNullOrEmpty(typeName))
        {
            if(throwOnError)
                throw new TypeLoadException($"Argument {nameof(typeName)} cannot be null or empty.");
            return null;
        }
        
        var type = TypeInfo.Parse(typeName);
        if (type.IsEmpty)
        {
            if(throwOnError)
                throw new TypeLoadException($"Argument {nameof(typeName)} cannot parse to empty {nameof(TypeInfo)}.");
            return null;
        }
        
        var foundType = TypeMap.Where(kvp => kvp.Key.Equals(type)).ToArray();
        
        if(foundType.Length > 0)
            return foundType[0].Value;
        
        if(throwOnError)
            throw new TypeLoadException($"Type [{typeName}] is not registered");
        return null;
    }

    public static void Clear()
    {
        TypeMap.Clear();
    }
}

internal sealed class TypeInfo : IEqualityComparer<TypeInfo>
{
    public static readonly TypeInfo Empty = new TypeInfo(string.Empty, null, null);
    
    public static TypeInfo Create<T>()
        => Create(typeof(T));
    
    public static TypeInfo Create(Type type)
        => new TypeInfo(type.Name, type.FullName, type.Assembly.GetName().Name);
    
    public static TypeInfo Parse(string info)
    {
        if (string.IsNullOrEmpty(info))
            return Empty;
        
        var parts = info.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var typeName = parts[0].Trim();
        var nameParts = typeName.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        
        var fullName = nameParts.Length > 1 ? typeName : null;
        var assemblyName = parts.Length > 1 ? parts[1].Trim() : null;
        
        return new TypeInfo(nameParts.Last(), fullName, assemblyName);
    }
    
    private TypeInfo(string name, string? fullName, string? assemblyName)
    {
        Name = name;
        FullName = fullName;
        AssemblyName = assemblyName;
    }

    public string Name { get; }
    public string? FullName { get; }
    public string? AssemblyName { get; }

    public bool IsEmpty => ReferenceEquals(this, Empty) || string.IsNullOrEmpty(Name);

    public override bool Equals(object? obj)
    {
        return obj is TypeInfo other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Name.GetHashCode();
            hashCode = (hashCode * 397) ^ (FullName != null ? FullName.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ (AssemblyName != null ? AssemblyName.GetHashCode() : 0);
            return hashCode;
        }
    }

    public bool Equals(TypeInfo? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        
        if(FullName is not null && other.FullName is not null && FullName != other.FullName) 
            return false;

        if (AssemblyName is not null && other.AssemblyName is not null && AssemblyName != other.AssemblyName)
            return false;
        
        return Name == other.Name;
    }

    public bool Equals(TypeInfo? x, TypeInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        
        if(x.FullName is not null && y.FullName is not null && x.FullName != y.FullName) 
            return false;

        if (x.AssemblyName is not null && y.AssemblyName is not null && x.AssemblyName != y.AssemblyName)
            return false;
        
        return x.Name == y.Name;
    }

    public int GetHashCode(TypeInfo obj)
    {
        return obj.GetHashCode();
    }
}