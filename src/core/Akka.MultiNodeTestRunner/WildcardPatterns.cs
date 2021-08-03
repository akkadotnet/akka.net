using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Akka.MultiNodeTestRunner
{
    public class WildcardPatterns
    {
        private readonly Regex[] _regex;

        public WildcardPatterns(string patternCsv, bool matchAllByDefault)
        {
            if (string.IsNullOrWhiteSpace(patternCsv))
            {
                if (matchAllByDefault)
                {
                    patternCsv = "*";
                }
                else
                {
                    _regex = null;
                    return;
                }
            }
            var patterns = patternCsv.Split(',');

            _regex = patterns
                .Select(pattern => 
                    Regex.Escape(pattern.Trim())
                        .Replace("\\?", ".")
                        .Replace("\\*", ".*"))
                .Select(clean => new Regex($"^{clean}$", RegexOptions.Compiled | RegexOptions.IgnoreCase))
                .ToArray();
        }

        public bool IsMatch(string input) => _regex?.Any(rx => rx.IsMatch(input)) ?? false;
    }
}
