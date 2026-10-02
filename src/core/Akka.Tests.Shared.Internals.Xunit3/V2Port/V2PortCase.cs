//-----------------------------------------------------------------------
// <copyright file="V2PortCase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text;

namespace Akka.Serialization
{
    /// <summary>
    /// One message of a V2 port's corpus: the message, and the manifest the legacy serializer gives it. The V2
    /// serializer must give the same manifest, so a node that only knows the legacy id can still read V2 bytes
    /// by manifest.
    /// </summary>
    public sealed class V2PortCase
    {
        /// <param name="message">A deterministic message: no clock, no random values, or the golden bytes drift.</param>
        /// <param name="manifest">The legacy manifest token, <c>""</c> when the legacy serializer writes none.</param>
        /// <param name="name">
        /// Names the golden-bytes file (<c>&lt;name&gt;.hex</c>) and the case in failure messages. Defaults to the
        /// manifest, so give a name when two messages share a manifest, or the manifest is empty.
        /// </param>
        /// <param name="equivalence">
        /// Compares the original with its round trip, and throws when they differ. Defaults to
        /// <see cref="object.Equals(object)"/>, then to a structural comparison.
        /// </param>
        public V2PortCase(object message, string manifest, string? name = null, Action<object, object>? equivalence = null)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Name = string.IsNullOrWhiteSpace(name) ? DefaultName(manifest, message) : name!;
            Equivalence = equivalence;
        }

        /// <summary>The message to serialize.</summary>
        public object Message { get; }

        /// <summary>The legacy manifest token.</summary>
        public string Manifest { get; }

        /// <summary>Unique within a corpus. The golden-bytes file name, once made safe for a file system.</summary>
        public string Name { get; }

        /// <summary>A custom comparison, or null for the default one.</summary>
        public Action<object, object>? Equivalence { get; }

        /// <summary>The file name stem <see cref="Name"/> maps to: letters, digits, <c>.</c>, <c>-</c> and <c>_</c> only.</summary>
        public string FileStem => Sanitize(Name);

        /// <inheritdoc />
        public override string ToString() => $"{Name} ({Message.GetType().Name}, manifest \"{Manifest}\")";

        private static string DefaultName(string manifest, object message)
            => manifest.Length == 0 ? "empty-manifest-" + message.GetType().Name : manifest;

        internal static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
            return sb.Length == 0 ? "_" : sb.ToString();
        }
    }
}
