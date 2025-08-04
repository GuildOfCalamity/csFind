using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace csfind
{
    internal static class CommandLineHelper
    {
        // We'll use this prefix for all our switches.
        // A single tick/dash is reserved for passing negative values.
        public const string preamble = "--";

        #region [CommandLine Helpers]
        /// <summary>
        /// Convenience method to return only the first "--drive" value or empty.
        /// </summary>
        public static string GetFirstDriveValue(string[] args)
        {
            foreach (var value in GetDriveValues(args))
                return value;

            return string.Empty;
        }

        /// <summary>
        /// Scans the args for every occurrence of "--drive" and returns
        /// the argument immediately following it as a search string.
        /// </summary>
        /// <param name="args">The array of command-line arguments.</param>
        /// <returns>
        /// A list of strings provided after each "--drive" switch.
        /// If "--drive" is never provided, returns an empty list.
        /// </returns>
        public static List<string> GetDriveValues(string[] args)
        {
            if (args == null)
                return new List<string>();

            var matches = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == $"{preamble}drive" && i + 1 < args.Length)
                {
                    matches.Add(args[i + 1]);
                    i++; // don't re-parse it
                }
            }
            return matches;
        }

        /// <summary>
        /// Convenience method to return only the first "--match" value or empty.
        /// </summary>
        public static string GetFirstMatchValue(string[] args)
        {
            foreach (var value in GetMatchValues(args))
                return value;

            return string.Empty;
        }

        /// <summary>
        /// Scans the args for every occurrence of "--match" and returns
        /// the argument immediately following it as a search string.
        /// </summary>
        /// <param name="args">The array of command-line arguments.</param>
        /// <returns>
        /// A list of strings provided after each "--match" switch.
        /// If "--match" is never provided, returns an empty list.
        /// </returns>
        public static List<string> GetMatchValues(string[] args)
        {
            if (args == null)
                return new List<string>();

            var matches = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == $"{preamble}match" && i + 1 < args.Length)
                {
                    matches.Add(args[i + 1]);
                    i++; // don't re-parse it
                }
            }
            return matches;
        }

        /// <summary>
        /// Convenience method to return only the first "--pattern" value or empty.
        /// </summary>
        public static string GetFirstPatternValue(string[] args)
        {
            foreach (var value in GetPatternValues(args))
                return value;

            return string.Empty;
        }

        /// <summary>
        /// Scans the args for every occurrence of "--pattern" and returns
        /// the argument immediately following it as a search string.
        /// </summary>
        /// <param name="args">The array of command-line arguments.</param>
        /// <returns>
        /// A list of strings provided after each "--pattern" switch.
        /// If "--pattern" is never provided, returns an empty list.
        /// </returns>
        public static List<string> GetPatternValues(string[] args)
        {
            if (args == null)
                return new List<string>();

            var matches = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == $"{preamble}pattern" && i + 1 < args.Length)
                {
                    matches.Add(args[i + 1]);
                    i++; // don't re-parse it
                }
            }
            return matches;
        }

        /// <summary>
        /// Convenience method to return only the first "--pattern" value or empty.
        /// </summary>
        public static string GetFirstThreadValue(string[] args)
        {
            foreach (var value in GetThreadValues(args))
                return value;

            return string.Empty;
        }

        /// <summary>
        /// Scans the args for every occurrence of "--threads" and returns
        /// the argument immediately following it as a search string.
        /// </summary>
        /// <param name="args">The array of command-line arguments.</param>
        /// <returns>
        /// A list of strings provided after each "--threads" switch.
        /// If "--threads" is never provided, returns an empty list.
        /// </returns>
        public static List<string> GetThreadValues(string[] args)
        {
            if (args == null)
                return new List<string>();

            var matches = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == $"{preamble}threads" && i + 1 < args.Length)
                {
                    matches.Add(args[i + 1]);
                    i++; // don't re-parse it
                }
            }
            return matches;
        }

        /// <summary>
        /// Convenience method to return only the first "--percent" value or empty.
        /// </summary>
        public static string GetFirstPercentValue(string[] args)
        {
            foreach (var value in GetPercentValues(args))
                return value;

            return string.Empty;
        }

        /// <summary>
        /// Scans the args for every occurrence of "--percent" and returns
        /// the argument immediately following it as a search string.
        /// </summary>
        /// <param name="args">The array of command-line arguments.</param>
        /// <returns>
        /// A list of strings provided after each "--percent" switch.
        /// If "--percent" is never provided, returns an empty list.
        /// </returns>
        public static List<string> GetPercentValues(string[] args)
        {
            if (args == null)
                return new List<string>();

            var matches = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == $"{preamble}percent" && i + 1 < args.Length)
                {
                    matches.Add(args[i + 1]);
                    i++; // don't re-parse it
                }
            }
            return matches;
        }

        /// <summary>
        /// Splits the raw command line into tokens. 
        /// A token is either:
        ///   • a quoted string including its quotes (e.g. "hello there")
        ///   • or a run of non-space characters.
        /// </summary>
        public static IReadOnlyList<string> TokenizeAndPreserveQuotes(string rawCommandLine)
        {
            if (string.IsNullOrEmpty(rawCommandLine))
                return Array.Empty<string>();

            // Regex: match either "…?" or any sequence of non-space chars
            var matches = Regex.Matches(rawCommandLine, @"(""[^""]*""|\S+)")
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToList();
            return matches;
        }

        /// <summary>
        /// Finds every argument after "--match", preserving its surrounding quotes.
        /// </summary>
        public static List<string> GetMatchValuesWithQuotes()
        {
            // Get the raw CL, including program path and all switches
            string raw = Environment.CommandLine;

            var tokens = TokenizeAndPreserveQuotes(raw);
            var results = new List<string>();

            for (int i = 0; i < tokens.Count - 1; i++)
            {
                if (tokens[i].Equals($"{preamble}match", StringComparison.Ordinal))
                {
                    // next token still has its quotes if user typed "…"
                    results.Add(tokens[i + 1]);
                    i++;  // skip next so we don’t double‐count
                }
            }

            return results;
        }

        /// <summary>
        /// Scans the args for every occurrence of "--term" and returns
        /// the argument immediately following it as a search string.
        /// </summary>
        /// <param name="args">The array of command-line arguments.</param>
        /// <returns>
        /// A list of strings provided after each "--term" switch.
        /// If "--term" is never provided, returns an empty list.
        /// </returns>
        public static List<string> GetTermValues(string[] args)
        {
            if (args == null)
                return new List<string>();

            var matches = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == $"{preamble}term" && i + 1 < args.Length)
                {
                    matches.Add(args[i + 1]);
                    i++; // don't re-parse it
                }
            }
            return matches;
        }
        #endregion

    }
}
