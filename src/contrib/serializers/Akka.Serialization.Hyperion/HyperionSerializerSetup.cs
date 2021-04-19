//-----------------------------------------------------------------------
// <copyright file="HyperionSerializerSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Util;

namespace Akka.Serialization.Hyperion
{
    public class HyperionSerializerSetup : Setup
    {
        public static readonly HyperionSerializerSetup Empty =
            new HyperionSerializerSetup(Option<bool>.None, Option<bool>.None);

        public static HyperionSerializerSetup Create(
            bool preserveObjectReferences, 
            bool versionTolerance, 
            Type knownTypesProvider, 
            IEnumerable<Func<string, string>> packageNameOverrides)
            => new HyperionSerializerSetup(preserveObjectReferences, versionTolerance, knownTypesProvider, packageNameOverrides);

        private HyperionSerializerSetup(
            Option<bool> preserveObjectReferences, 
            Option<bool> versionTolerance, 
            Type knownTypesProvider = null, 
            IEnumerable<Func<string, string>> packageNameOverrides = null)
        {
            PreserveObjectReferences = preserveObjectReferences;
            VersionTolerance = versionTolerance;
            KnownTypesProvider = knownTypesProvider;
            PackageNameOverrides = packageNameOverrides;
        }

        /// <summary>
        /// When true, it tells <see cref="HyperionSerializer"/> to keep
        /// track of references in serialized/deserialized object graph.
        /// </summary>
        public Option<bool> PreserveObjectReferences { get; }

        /// <summary>
        /// When true, it tells <see cref="HyperionSerializer"/> to encode
        /// a list of currently serialized fields into type manifest.
        /// </summary>
        public Option<bool> VersionTolerance { get; }

        /// <summary>
        /// A type implementing <see cref="IKnownTypesProvider"/>, that will
        /// be used when <see cref="HyperionSerializer"/> is being constructed
        /// to provide a list of message types that are supposed to be known
        /// implicitly by all communicating parties. Implementing class must
        /// provide either a default constructor or a constructor taking
        /// <see cref="ExtendedActorSystem"/> as its only parameter.
        /// </summary>
        public Type KnownTypesProvider { get; }

        /// <summary>
        /// A list of lambda functions, used to transform incoming deserialized
        /// package names before they are instantiated
        /// </summary>
        public IEnumerable<Func<string, string>> PackageNameOverrides { get; }

        internal HyperionSerializerSettings ApplySettings(HyperionSerializerSettings settings)
            => new HyperionSerializerSettings(
                PreserveObjectReferences.HasValue ? PreserveObjectReferences.Value : settings.PreserveObjectReferences,
                VersionTolerance.HasValue ? VersionTolerance.Value : settings.VersionTolerance,
                KnownTypesProvider ?? settings.KnownTypesProvider,
                PackageNameOverrides ?? settings.PackageNameOverrides
            );

        public HyperionSerializerSetup WithPreserveObjectReference(bool preserveObjectReference)
            => Copy(preserveObjectReferences: preserveObjectReference);

        public HyperionSerializerSetup WithVersionTolerance(bool versionTolerance)
            => Copy(versionTolerance: versionTolerance);

        public HyperionSerializerSetup WithKnownTypeProvider<T>()
            => Copy(knownTypesProvider: typeof(T));

        public HyperionSerializerSetup WithKnownTypeProvider(Type knownTypeProvider)
            => Copy(knownTypesProvider: knownTypeProvider);

        public HyperionSerializerSetup WithPackageNameOverrides(IEnumerable<Func<string, string>> packageNameOverrides)
            => Copy(packageNameOverrides: packageNameOverrides);

        private HyperionSerializerSetup Copy(
            bool? preserveObjectReferences = null,
            bool? versionTolerance = null,
            Type knownTypesProvider = null,
            IEnumerable<Func<string, string>> packageNameOverrides = null)
            => new HyperionSerializerSetup(
                preserveObjectReferences ?? PreserveObjectReferences,
                versionTolerance ?? VersionTolerance,
                knownTypesProvider ?? KnownTypesProvider,
                packageNameOverrides ?? PackageNameOverrides
                );
    }
}
