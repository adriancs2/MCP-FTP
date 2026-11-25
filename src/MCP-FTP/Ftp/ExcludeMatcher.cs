using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MCP_FTP.Ftp
{
    public class ExcludeMatcher
    {
        private readonly List<Rule> _rules = new List<Rule>();

        private class Rule
        {
            public bool DirOnly;
            public string RawNormalized;
            public Regex Pattern;
        }

        public ExcludeMatcher(string excludeConfigText, string localRoot = null)
        {
            if (string.IsNullOrWhiteSpace(excludeConfigText))
                return;

            string normRoot = string.IsNullOrEmpty(localRoot)
                ? null
                : localRoot.Replace('\\', '/').TrimEnd('/') + "/";

            var lines = excludeConfigText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                line = line.Replace('\\', '/');

                // Allow users/AI to paste absolute paths inside localRoot
                if (normRoot != null && line.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                    line = line.Substring(normRoot.Length);

                line = line.TrimStart('/');
                if (string.IsNullOrEmpty(line))
                    continue;

                bool dirOnly = line.EndsWith("/");
                string clean = line.TrimEnd('/');
                string regexStr = "^" + Regex.Escape(clean).Replace("\\*", ".*").Replace("\\?", ".") + (dirOnly ? "(/.*)?$" : "(/.*)?$");

                _rules.Add(new Rule
                {
                    DirOnly = dirOnly,
                    RawNormalized = clean,
                    Pattern = new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Compiled)
                });
            }
        }

        public bool IsExcluded(string relativePath, bool isDirectory = false)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;

            string norm = relativePath.Replace('\\', '/').Trim('/');
            string fileName = Path.GetFileName(norm);

            foreach (var r in _rules)
            {
                if (r.Pattern.IsMatch(norm))
                    return true;

                // Match basename if rule has no slash (e.g. "*.log" or ".git")
                if (!r.RawNormalized.Contains("/") && !string.IsNullOrEmpty(fileName) && r.Pattern.IsMatch(fileName))
                    return true;
            }

            return false;
        }
    }
}
