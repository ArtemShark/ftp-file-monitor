using System.Text.RegularExpressions;

class Rule
{
    public string Name;
    public Regex Pattern;

    public Rule(string name, string pattern, bool isRegex)
    {
        Name = name;
        Pattern = CreateRegex(pattern, isRegex);
    }

    // Wildcard patterns ("report_*.csv") are converted to an anchored regex.
    static Regex CreateRegex(string pattern, bool isRegex)
    {
        string expr;

        if (isRegex)
        {
            expr = pattern;
        }
        else
        {
            expr = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        }

        return new Regex(expr, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
