using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Wrapper over the BepInEx logger. Code that runs every frame must only report problems through
    /// <see cref="Failure"/> or the *Once methods, so a persistent fault produces one line instead of thousands.
    /// Warnings and errors are also kept in a short history that the diagnostics report prints.
    /// </summary>
    internal static class Log
    {
        private const int MaxRecentProblems = 25;

        private static ManualLogSource _source;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();
        private static readonly Dictionary<string, int> FailureCounts = new Dictionary<string, int>();
        private static readonly Queue<string> Problems = new Queue<string>();

        public static IEnumerable<string> RecentProblems { get { return Problems; } }

        public static IEnumerable<KeyValuePair<string, int>> FailureTotals { get { return FailureCounts; } }

        public static void Init(ManualLogSource source)
        {
            _source = source;
        }

        public static void Info(string message)
        {
            _source.LogInfo(message);
        }

        public static void Warn(string message)
        {
            _source.LogWarning(message);
            Remember("WARN", message);
        }

        public static void Error(string message)
        {
            _source.LogError(message);
            Remember("ERROR", message);
        }

        public static void Verbose(string message)
        {
            if (Settings.VerboseLogging != null && Settings.VerboseLogging.Value)
            {
                _source.LogInfo("[verbose] " + message);
            }
        }

        public static void InfoOnce(string key, string message)
        {
            if (OnceKeys.Add(key)) Info(message);
        }

        public static void WarnOnce(string key, string message)
        {
            if (OnceKeys.Add(key)) Warn(message);
        }

        /// <summary>Logs the first failure of an operation in full, then only an occasional repeat count.</summary>
        public static void Failure(string operation, Exception e)
        {
            int count;
            FailureCounts.TryGetValue(operation, out count);
            FailureCounts[operation] = ++count;

            if (count == 1)
            {
                Error(operation + " failed: " + e);
            }
            else if (count == 10 || count % 1000 == 0)
            {
                Error(operation + " has now failed " + count + " times. Latest: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Remember(string level, string message)
        {
            int newline = message.IndexOf('\n');
            string firstLine = newline < 0 ? message : message.Substring(0, newline).TrimEnd('\r');
            Problems.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " " + level + " " + firstLine);
            while (Problems.Count > MaxRecentProblems) Problems.Dequeue();
        }
    }
}
