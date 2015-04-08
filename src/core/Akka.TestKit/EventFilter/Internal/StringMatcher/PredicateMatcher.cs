using System;

namespace Akka.TestKit.Internal.StringMatcher
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class PredicateMatcher : IStringMatcher
    {
        private readonly Predicate<string> _predicate;
        private readonly string _hint;

        public PredicateMatcher(Predicate<string> predicate, string hint="")
        {
            _predicate = predicate;
            _hint = hint;
        }

        public bool IsMatch(string s)
        {
            return _predicate(s);
        }


        public override string ToString()
        {
            return "matches predicate "+_hint;
        }
    }
}