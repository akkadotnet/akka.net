#region copyright
//-----------------------------------------------------------------------
// <copyright file="TypeMap.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Akka.Configuration;

namespace Akka.DistributedData.Serialization
{
    internal struct TypeMap
    {
        private readonly Dictionary<Type, uint> _typeToId;
        private readonly Dictionary<uint, Type> _idToType;

        public TypeMap(Config mappingsConfig)
        {
            _typeToId = new Dictionary<Type, uint>();
            _idToType = new Dictionary<uint, Type>();

            Populate(mappingsConfig);
        }

        public Type this[uint id]
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                if (_idToType.TryGetValue(id, out var type))
                    return type;
                else
                    throw new ArgumentException($"Tried to find a type mapping for identifier [{id}], but none was found. Please configure `type=id` mapping inside `akka.actor.serialization-settings.akka-replicated-data.mappings`.");
            }
        }

        public uint this[Type type]
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                if (_typeToId.TryGetValue(type, out var id))
                    return id;
                else
                    throw new ArgumentException($"Tried to find a type identifier for type [{type.FullName}], but none was found. Please configure `type=id` mapping inside `akka.actor.serialization-settings.akka-replicated-data.mappings`.");
            }
        }

        private void Populate(Config mappingsConfig)
        {
            if (mappingsConfig is null)
            {
                throw new ConfigurationException($"No type-id mappings found for Akka.DistributedData serializer. Path checked: `akka.actor.serialization-settings.akka-replicated-data.mappings`.");
            }

            foreach (var entry in mappingsConfig.AsEnumerable())
            {
                var type = Type.GetType(entry.Key, throwOnError: true);
                var id = (uint)entry.Value.GetInt();

                if (_typeToId.TryGetValue(type, out var otherId) && otherId != id)
                {
                    throw new ConfigurationException($"Couldn't configure mapping from type [{type.FullName}] to identifier [{id}] because a conflicting identifier [{otherId}] has already been registered for that type.");
                }

                if (_idToType.TryGetValue(id, out var otherType) && otherType != type)
                {
                    throw new ConfigurationException($"Couldn't configure mapping from type [{type.FullName}] to identifier [{id}] because a conflicting type [{otherType.FullName}] has already been using that identifier.");
                }

                _typeToId[type] = id;
                _idToType[id] = type;
            }
        }
    }
}