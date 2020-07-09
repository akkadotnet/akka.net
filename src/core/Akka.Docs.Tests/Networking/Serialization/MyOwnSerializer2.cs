//-----------------------------------------------------------------------
// <copyright file="MyOwnSerializer2.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Serialization;

namespace DocsExamples.Networking.Serialization
{
    #region CustomSerialization
    public class MyOwnSerializer2 : SerializerWithStringManifest
    {
        private const string CustomerManifest = "customer";
        private const string UserManifest = "user";

        public MyOwnSerializer2(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// Completely unique value to identify this implementation of the
        /// <see cref="Serializer"/> used to optimize network traffic
        /// 0 - 40 is reserved by Akka.NET itself
        /// </summary>
        public override int Identifier { get; } = 1234567;

        /// <summary>
        /// The manifest (type hint) that will be provided in the fromBinary method Use <see cref="string.Empty"/> if manifest is not needed.
        /// </summary>
        public override string Manifest(object obj)
        {
            switch (obj)
            {
                case Customer _: return CustomerManifest;
                case User _: return UserManifest;
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Serializes the given object to an Array of Bytes
        /// </summary>
        public override byte[] ToBinary(object obj)
        {
            // Put the real code that serializes the object here
            switch (obj)
            {
                case Customer c: return Encoding.UTF8.GetBytes(c.Name);
                case User c: return Encoding.UTF8.GetBytes(c.Name);
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Deserializes the given array, using the type hint
        /// </summary>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case CustomerManifest: return new Customer(Encoding.UTF8.GetString(bytes));
                case UserManifest: return new User(Encoding.UTF8.GetString(bytes));
                default: throw new SerializationException();
            }
        }
    }
    #endregion

    public class Customer
    {
        public Customer(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }

    public class User
    {
        public User(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }
}
