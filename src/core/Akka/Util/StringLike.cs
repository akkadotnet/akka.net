using System.Text.RegularExpressions;

namespace Akka.Util
{
    public static class WildcardMatch
    {
        #region Public Methods
        public static bool Like(this string text,string pattern, bool caseSensitive = false)
        {
            pattern = pattern.Replace(".", @"\.");
            pattern = pattern.Replace("?", ".");
            pattern = pattern.Replace("*", ".*?");
            pattern = pattern.Replace(@"\", @"\\");
            pattern = pattern.Replace(" ", @"\s");
            return new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase).IsMatch(text);
        }
        #endregion
    }
}
