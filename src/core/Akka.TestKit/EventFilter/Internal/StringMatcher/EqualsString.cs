using System;

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class EqualsString : IStringMatcher
    {
        private readonly string _s;

        public EqualsString(string s)
        {
            _s = s;
        }

        public bool IsMatch(string s)
        {
            return String.Equals(_s, s, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return "== \"" + _s + "\"";
        }
    }
}