using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using CompanyAttribute = System.Reflection.AssemblyCompanyAttribute;
using ConfigurationAttribute = System.Reflection.AssemblyConfigurationAttribute;
using FileVersionAttribute = System.Reflection.AssemblyFileVersionAttribute;
using ProductAttribute = System.Reflection.AssemblyProductAttribute;
using TargetFrameworkAttribute = System.Runtime.Versioning.TargetFrameworkAttribute;

namespace csfind
{
    public static class Extensions
    {
        #region [Random Helper]
        private static readonly WeakReference s_random = new WeakReference(null);
        /// <summary>
        /// NOTE: In later versions of .NET a "Random.Shared" property was introduced to alleviate the need for this.
        /// </summary>
        public static Random Rnd
        {
            get
            {
                var r = (Random)s_random.Target;
                if (r == null) { s_random.Target = r = new Random(); }
                return r;
            }
        }
        #endregion

        public const double Epsilon = 0.000000000001;
        static readonly Regex _uncRegex = new Regex(@"^\\\\[^\\\/:*?""<>|\r\n]+\\[^\\\/:*?""<>|\r\n]+(?:\\[^\\\/:*?""<>|\r\n]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns true if the path string matches the UNC format \\server\share\…
        /// </summary>
        public static bool IsValidUncSyntax(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // Quick prefix check
            if (!path.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            // Reject illegal path characters
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return false;

            // Final regex validation
            return _uncRegex.IsMatch(path);
        }

        /// <summary>
        /// Checks both the UNC syntax and whether the directory actually exists.
        /// Useful for network shares you expect to be online.
        /// </summary>
        public static bool IsValidAndReachableUnc(this string uncPath)
        {
            if (!IsValidUncSyntax(uncPath))
                return false;

            try
            {
                // Directory.Exists will return false if server/share is unreachable
                return Directory.Exists(uncPath);
            }
            catch (UnauthorizedAccessException)
            {
                // You don’t have permission, but path exists in principle
                return true;
            }
            catch (IOException)
            {
                // Network error, treat as unreachable
                return false;
            }
            catch (Exception)
            {
                // Any other exception, treat as unreachable
                return false;
            }
        }

        /// <summary>
        /// An updated string truncation helper.
        /// </summary>
        /// <remarks>
        /// This can be helpful when the CharacterEllipsis TextTrimming Property is not available.
        /// </remarks>
        public static string Truncate(this string text, int maxLength, string mesial = "…")
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (maxLength > 0 && text.Length > maxLength)
            {
                var limit = maxLength / 2;
                if (limit > 1)
                {
                    return String.Format("{0}{1}{2}", text.Substring(0, limit).Trim(), mesial, text.Substring(text.Length - limit).Trim());
                }
                else
                {
                    var tmp = text.Length <= maxLength ? text : text.Substring(0, maxLength).Trim();
                    return String.Format("{0}{1}", tmp, mesial);
                }
            }
            return text;
        }

        public static List<string> GetLogicalDrives()
        {
            List<string> drives = new List<string>();

            foreach (var drive in Directory.GetLogicalDrives())
                drives.Add(drive);

            return drives;
        }

        /// <summary>
        /// Brute force alpha removal of <see cref="Version"/> text
        /// is not always the best approach, for example the following:
        /// "3.0.0-zmain.2211 (DCPP(199ff10ec000000)(cloudtest).160101.0800)"
        /// converts to "3.0.0.221119910000000.160101.0800", which is not accurate.
        /// </summary>
        /// <param name="fullPath">the entire path to the file</param>
        /// <returns>sanitized <see cref="Version"/></returns>
        public static Version GetFileVersion(this string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return new Version(); // 0.0

            try
            {
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullPath).FileVersion;
                if (string.IsNullOrEmpty(ver)) { return new Version(); }
                if (ver.HasSpace())
                {   // Some assemblies contain versions such as "10.0.22622.1030 (WinBuild.160101.0800)"
                    // This will cause the Version constructor to throw an exception, so just take the first piece.
                    var chunk = ver.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var firstPiece = Regex.Replace(chunk[0].Replace(',', '.'), "[^.0-9]", "");
                    return new Version(firstPiece);
                }
                string cleanVersion = Regex.Replace(ver, "[^.0-9]", "");
                return new Version(cleanVersion);
            }
            catch (Exception)
            {
                return new Version(); // 0.0
            }
        }

        /// <summary>
        /// Returns the assembly's description (if available).
        /// </summary>
        /// <param name="fullPath">the entire path to the file</param>
        public static string GetFileDescription(this string fullPath)
        {
            string result = string.Empty;
            if (string.IsNullOrEmpty(fullPath))
                return result;
            try { result = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullPath).FileDescription; }
            catch (Exception) { }
            return result;
        }

        /// <summary>
        ///   Move current instance and rename current instance when needed
        /// <example>
        ///   FileInfo fileInfo = new FileInfo(@"c:\test.txt");
        ///   File.Create(fileInfo.FullName).Dispose();
        ///   fileInfo.MoveTo(@"d:\", true);
        /// </example>
        /// </summary>
        /// <param name="fileInfo">Current instance</param>
        /// <param name="destFileName">The Path to move current instance to, which can specify a different file name</param>
        /// <param name="renameWhenExists">Bool to specify if current instance should be renamed when exists</param>
        public static void MoveTo(FileInfo fileInfo, string destFileName, bool rollingRename = false)
        {
            string newFullPath = string.Empty;

            if (rollingRename)
            {
                int count = 1;

                string fileNameOnly = Path.GetFileNameWithoutExtension(fileInfo.FullName);
                string extension = Path.GetExtension(fileInfo.FullName);
                newFullPath = Path.Combine(destFileName, fileInfo.Name);

                while (File.Exists(newFullPath))
                {
                    string tempFileName = string.Format("{0}({1})", fileNameOnly, count++);
                    newFullPath = Path.Combine(destFileName, tempFileName + extension);
                }
            }

            try 
            {
                string dest = rollingRename ? newFullPath : destFileName;
                
                if (File.Exists(dest))
                    File.Delete(dest);
                
                fileInfo.MoveTo(dest); 
            }
            catch (Exception) { }
        }

        public static bool HasAlpha(this string str)
        {
            if (string.IsNullOrEmpty(str)) { return false; }
            return str.Any(x => char.IsLetter(x));
        }

        public static bool HasNumeric(this string str)
        {
            if (string.IsNullOrEmpty(str)) { return false; }
            return str.Any(x => char.IsNumber(x));
        }

        public static bool HasSpace(this string str)
        {
            if (string.IsNullOrEmpty(str)) { return false; }
            return str.Any(x => char.IsSeparator(x));
        }

        public static bool HasPunctuation(this string str)
        {
            if (string.IsNullOrEmpty(str)) { return false; }
            return str.Any(x => char.IsPunctuation(x));
        }

        /// <summary>
        /// Consider anything within an order of magnitude of epsilon to be zero.
        /// </summary>
        /// <param name="value">The <see cref="double"/> to check</param>
        /// <returns>
        /// True if the number is zero, false otherwise.
        /// </returns>
        public static bool IsZero(this double value) => Math.Abs(value) < Epsilon;

        public static bool IsInvalid(this double value)
        {
            if (value == double.NaN || value == double.NegativeInfinity || value == double.PositiveInfinity)
                return true;

            return false;
        }

        /// <summary>
        /// We'll consider zero anything less than or equal to zero.
        /// </summary>
        public static bool IsInvalidOrZero(this double value)
        {
            if (value == double.NaN || value == double.NegativeInfinity || value == double.PositiveInfinity || value <= 0)
                return true;

            return false;
        }

        public static bool IsOne(this double value)
        {
            return Math.Abs(value) >= 1d - Epsilon && Math.Abs(value) <= 1d + Epsilon;
        }

        public static string GetLastChars(string input, int length, char pad = ' ')
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            else if (input.Length >= length)
                return input.Substring(input.Length - length, length);
            else if (pad.Equals(' '))
                return input;
            else
                return input.PadLeft(length, pad);
        }

        /// <summary>
        /// Use ICMP to determine if the system has Internet access.
        /// </summary>
        /// <returns><c>true</c> if Internet accessible, <c>false</c> otherwise</returns>
        public static bool HasInternetAccess()
        {
            bool result = false;

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ping",
                    Arguments = "-n 1 8.8.8.8",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true,
                //SynchronizingObject = syncInvoker
            };

            proc.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    //Console.WriteLine($"[{Thread.CurrentThread.ManagedThreadId}] {e.Data}");
                    if (e.Data.Contains("Reply from 8.8.8.8"))
                        result = true;
                    //else if (e.Data.Contains("Request timed out") || e.Data.Contains("Destination host unreachable"))
                    //    result = false;
                }
            };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.WaitForExit();
            return result;
        }

        /// <summary>
        /// Home-brew parallel invoke that will not block while actions run.
        /// </summary>
        /// <param name="actions">array of <see cref="Action"/>s</param>
        public static void ParallelInvokeAndForget(params Action[] action)
        {
            action.ForEach(a =>
            {
                try
                {
                    ThreadPool.QueueUserWorkItem((obj) => { a.Invoke(); });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] ParallelInvokeAndForget: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// An un-optimized, home-brew parallel for each implementation.
        /// </summary>
        public static void ParallelForEach<T>(IEnumerable<T> source, Action<T> action)
        {
            var tasks = from item in source select Task.Run(() => action(item));
            Task.WaitAll(tasks.ToArray());
        }

        /// <summary>
        /// An optimized, home-brew parallel ForEach implementation.
        /// Creates branched execution based on available processors.
        /// </summary>
        public static void ParallelForEachUsingEnumerator<T>(IEnumerable<T> source, Action<T> action, Action<Exception> onError)
        {
            IEnumerator<T> e = source.GetEnumerator();
            IEnumerable<Task> tasks = from i 
                 in Enumerable.Range(0, Environment.ProcessorCount)
                 select Task.Run(() =>
                 {
                     while (true)
                     {
                         T item;
                         lock (e)
                         {
                             if (!e.MoveNext()) { return; }
                             item = e.Current;
                         }
                         #region [Must stay outside locking scope, or defeats the purpose of parallelism]
                         try
                         {
                             action(item);
                         }
                         catch (Exception ex)
                         {
                             onError?.Invoke(ex);
                         }
                         #endregion
                     }
                 });
            Task.WaitAll(tasks.ToArray());
        }

        /// <summary>
        /// An optimized, home-brew parallel ForEach implementation.
        /// Creates branched execution based on available processors.
        /// </summary>
        public static void ParallelForEachUsingPartitioner<T>(IEnumerable<T> source, Action<T> action, Action<Exception> onError, EnumerablePartitionerOptions options = EnumerablePartitionerOptions.NoBuffering)
        {
            //IList<IEnumerator<T>> partitions = Partitioner.Create(source, options).GetPartitions(Environment.ProcessorCount);
            IEnumerable<Task> tasks = from partition 
                in Partitioner.Create(source, options).GetPartitions(Environment.ProcessorCount)
                select Task.Run(() =>
                {
                    using (partition) // partitions are disposable
                    {
                        while (partition.MoveNext())
                        {
                            try
                            {
                                action(partition.Current);
                            }
                            catch (Exception ex)
                            {
                                onError?.Invoke(ex);
                            }
                        }
                    }
                });
            Task.WaitAll(tasks.ToArray());
        }

        /// <summary>
        /// An optimized, home-brew parallel ForEach implementation.
        /// </summary>
        public static void ParallelForEachUsingPartitioner<T>(IList<T> list, Action<T> action, Action<Exception> onError, EnumerablePartitionerOptions options = EnumerablePartitionerOptions.NoBuffering)
        {
            //IList<IEnumerator<T>> partitions = Partitioner.Create(list, options).GetPartitions(Environment.ProcessorCount);
            IEnumerable<Task> tasks = from partition 
                in Partitioner.Create(list, options).GetPartitions(Environment.ProcessorCount)
                select Task.Run(() =>
                {
                    using (partition) // partitions are disposable
                    {
                        while (partition.MoveNext())
                        {
                            try
                            {
                                action(partition.Current);
                            }
                            catch (Exception ex)
                            {
                                onError?.Invoke(ex);
                            }
                        }
                    }
                });
            Task.WaitAll(tasks.ToArray());
        }

        public static void ForEach<T>(this IEnumerable<T> ie, Action<T> action)
        {
            foreach (var i in ie)
            {
                try { action(i); }
                catch (Exception) { }
            }
        }

        public static void ForEach<T>(this IEnumerable<T> ie, Action<T> action, Action<Exception> onError)
        {
            foreach (var i in ie)
            {
                try { action(i); }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
        }

        /// <summary>
        /// Converts long file size into typical browser file size.
        /// </summary>
        public static string ToFileSize(this ulong size)
        {
            if (size < 1024) { return (size).ToString("F0") + " Bytes"; }
            if (size < Math.Pow(1024, 2)) { return (size / 1024).ToString("F0") + "KB"; }
            if (size < Math.Pow(1024, 3)) { return (size / Math.Pow(1024, 2)).ToString("F0") + "MB"; }
            if (size < Math.Pow(1024, 4)) { return (size / Math.Pow(1024, 3)).ToString("F0") + "GB"; }
            if (size < Math.Pow(1024, 5)) { return (size / Math.Pow(1024, 4)).ToString("F0") + "TB"; }
            if (size < Math.Pow(1024, 6)) { return (size / Math.Pow(1024, 5)).ToString("F0") + "PB"; }
            return (size / Math.Pow(1024, 6)).ToString("F0") + "EB";
        }

        /// <summary>
        /// Converts <see cref="TimeSpan"/> objects to a simple human-readable string.
        /// e.g. 420 milliseconds, 3.1 seconds, 2 minutes, 4.231 hours, etc.
        /// </summary>
        /// <param name="span"><see cref="TimeSpan"/></param>
        /// <param name="significantDigits">number of right side digits in output (precision)</param>
        /// <returns></returns>
        public static string ToTimeString(this TimeSpan span, int significantDigits = 3)
        {
            var format = $"G{significantDigits}";
            return span.TotalMilliseconds < 1000 ? span.TotalMilliseconds.ToString(format) + " milliseconds"
                    : (span.TotalSeconds < 60 ? span.TotalSeconds.ToString(format) + " seconds"
                    : (span.TotalMinutes < 60 ? span.TotalMinutes.ToString(format) + " minutes"
                    : (span.TotalHours < 24 ? span.TotalHours.ToString(format) + " hours"
                    : span.TotalDays.ToString(format) + " days")));
        }

        /// <summary>
        /// Converts <see cref="TimeSpan"/> objects to a simple human-readable string.
        /// e.g. 420 milliseconds, 3.1 seconds, 2 minutes, 4.231 hours, etc.
        /// </summary>
        /// <param name="span"><see cref="TimeSpan"/></param>
        /// <param name="significantDigits">number of right side digits in output (precision)</param>
        /// <returns></returns>
        public static string ToTimeString(this TimeSpan? span, int significantDigits = 3)
        {
            var format = $"G{significantDigits}";
            return span?.TotalMilliseconds < 1000 ? span?.TotalMilliseconds.ToString(format) + " milliseconds"
                    : (span?.TotalSeconds < 60 ? span?.TotalSeconds.ToString(format) + " seconds"
                    : (span?.TotalMinutes < 60 ? span?.TotalMinutes.ToString(format) + " minutes"
                    : (span?.TotalHours < 24 ? span?.TotalHours.ToString(format) + " hours"
                    : span?.TotalDays.ToString(format) + " days")));
        }

        /// <summary>
        /// Display a readable sentence as to when the time will happen.
        /// e.g. "in one second" or "in 2 days"
        /// </summary>
        /// <param name="value"><see cref="TimeSpan"/>the future time to compare from now</param>
        /// <returns>human friendly format</returns>
        public static string ToReadableTime(this TimeSpan value, bool reportMilliseconds = false)
        {
            double delta = value.TotalSeconds;
            if (delta < 1 && !reportMilliseconds) { return "less than one second"; }
            if (delta < 1 && reportMilliseconds) { return $"{value.TotalMilliseconds:N1} milliseconds"; }
            if (delta < 60) { return value.Seconds == 1 ? "one second" : value.Seconds + " seconds"; }
            if (delta < 120) { return "a minute"; }                  // 2 * 60
            if (delta < 3000) { return value.Minutes + " minutes"; } // 50 * 60
            if (delta < 5400) { return "an hour"; }                  // 90 * 60
            if (delta < 86400) { return value.Hours + " hours"; }    // 24 * 60 * 60
            if (delta < 172800) { return "one day"; }                // 48 * 60 * 60
            if (delta < 2592000) { return value.Days + " days"; }    // 30 * 24 * 60 * 60
            if (delta < 31104000)                                    // 12 * 30 * 24 * 60 * 60
            {
                int months = Convert.ToInt32(Math.Floor((double)value.Days / 30));
                return months <= 1 ? "one month" : months + " months";
            }
            int years = Convert.ToInt32(Math.Floor((double)value.Days / 365));
            return years <= 1 ? "one year" : years + " years";
        }

        /// <summary>
        /// Similar to <see cref="GetReadableTime(TimeSpan)"/>.
        /// </summary>
        /// <param name="timeSpan"><see cref="TimeSpan"/></param>
        /// <returns>formatted text</returns>
        public static string ToReadableString(this TimeSpan span)
        {
            var parts = new StringBuilder();
            if (span.Days > 0)
                parts.Append($"{span.Days} day{(span.Days == 1 ? string.Empty : "s")} ");
            if (span.Hours > 0)
                parts.Append($"{span.Hours} hour{(span.Hours == 1 ? string.Empty : "s")} ");
            if (span.Minutes > 0)
                parts.Append($"{span.Minutes} minute{(span.Minutes == 1 ? string.Empty : "s")} ");
            if (span.Seconds > 0)
                parts.Append($"{span.Seconds} second{(span.Seconds == 1 ? string.Empty : "s")} ");
            if (span.Milliseconds > 0)
                parts.Append($"{span.Milliseconds} millisecond{(span.Milliseconds == 1 ? string.Empty : "s")} ");

            if (parts.Length == 0) // result was less than 1 millisecond
                return $"{span.TotalMilliseconds:N4} milliseconds"; // similar to span.Ticks
            else
                return parts.ToString().Trim();
        }

        /// <summary>
        /// Display a readable sentence as to when that time happened.
        /// e.g. "5 minutes ago" or "in 2 days"
        /// </summary>
        /// <param name="value"><see cref="DateTime"/>the past/future time to compare from now</param>
        /// <returns>human friendly format</returns>
        public static string ToReadableTime(this DateTime value, bool useUTC = false)
        {
            TimeSpan ts;
            if (useUTC) { ts = new TimeSpan(DateTime.UtcNow.Ticks - value.Ticks); }
            else { ts = new TimeSpan(DateTime.Now.Ticks - value.Ticks); }

            double delta = ts.TotalSeconds;
            if (delta < 0) // in the future
            {
                delta = Math.Abs(delta);
                if (delta < 1) { return "in less than one second"; }
                if (delta < 60) { return Math.Abs(ts.Seconds) == 1 ? "in one second" : "in " + Math.Abs(ts.Seconds) + " seconds"; }
                if (delta < 120) { return "in a minute"; }
                if (delta < 3000) { return "in " + Math.Abs(ts.Minutes) + " minutes"; } // 50 * 60
                if (delta < 5400) { return "in an hour"; } // 90 * 60
                if (delta < 86400) { return "in " + Math.Abs(ts.Hours) + " hours"; } // 24 * 60 * 60
                if (delta < 172800) { return "tomorrow"; } // 48 * 60 * 60
                if (delta < 2592000) { return "in " + Math.Abs(ts.Days) + " days"; } // 30 * 24 * 60 * 60
                if (delta < 31104000) // 12 * 30 * 24 * 60 * 60
                {
                    int months = Convert.ToInt32(Math.Floor((double)Math.Abs(ts.Days) / 30));
                    return months <= 1 ? "in one month" : "in " + months + " months";
                }
                int years = Convert.ToInt32(Math.Floor((double)Math.Abs(ts.Days) / 365));
                return years <= 1 ? "in one year" : "in " + years + " years";
            }
            else // in the past
            {
                if (delta < 1) { return "less than one second ago"; }
                if (delta < 60) { return ts.Seconds == 1 ? "one second ago" : ts.Seconds + " seconds ago"; }
                if (delta < 120) { return "a minute ago"; }
                if (delta < 3000) { return ts.Minutes + " minutes ago"; } // 50 * 60
                if (delta < 5400) { return "an hour ago"; } // 90 * 60
                if (delta < 86400) { return ts.Hours + " hours ago"; } // 24 * 60 * 60
                if (delta < 172800) { return "yesterday"; } // 48 * 60 * 60
                if (delta < 2592000) { return ts.Days + " days ago"; } // 30 * 24 * 60 * 60
                if (delta < 31104000) // 12 * 30 * 24 * 60 * 60
                {
                    int months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                    return months <= 1 ? "one month ago" : months + " months ago";
                }
                int years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
                return years <= 1 ? "one year ago" : years + " years ago";
            }
        }

        /// <summary>
        /// Display a readable sentence as to when the time will happen.
        /// e.g. "8 minutes 0 milliseconds"
        /// </summary>
        /// <param name="milliseconds">integer value</param>
        /// <returns>human friendly format</returns>
        public static string ToReadableTime(int milliseconds)
        {
            if (milliseconds < 0)
                throw new ArgumentException("Milliseconds cannot be negative.");

            TimeSpan timeSpan = TimeSpan.FromMilliseconds(milliseconds);

            if (timeSpan.TotalHours >= 1)
            {
                return string.Format("{0:0} hour{1} {2:0} minute{3}",
                    timeSpan.Hours, timeSpan.Hours == 1 ? "" : "s",
                    timeSpan.Minutes, timeSpan.Minutes == 1 ? "" : "s");
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return string.Format("{0:0} minute{1} {2:0} second{3}",
                    timeSpan.Minutes, timeSpan.Minutes == 1 ? "" : "s",
                    timeSpan.Seconds, timeSpan.Seconds == 1 ? "" : "s");
            }
            else
            {
                return string.Format("{0:0} second{1} {2:0} millisecond{3}",
                    timeSpan.Seconds, timeSpan.Seconds == 1 ? "" : "s",
                    timeSpan.Milliseconds, timeSpan.Milliseconds == 1 ? "" : "s");
            }
        }

        public static string ToHoursMinutesSeconds(this TimeSpan ts) => ts.Days > 0 ? (ts.Days * 24 + ts.Hours) + ts.ToString("':'mm':'ss") : ts.ToString("hh':'mm':'ss");

        public static long TimeToTicks(int hour, int minute, int second)
        {
            long MaxSeconds = long.MaxValue / 10000000; // => MaxValue / TimeSpan.TicksPerSecond
            long MinSeconds = long.MinValue / 10000000; // => MinValue / TimeSpan.TicksPerSecond

            // "totalSeconds" is bounded by 2^31 * 2^12 + 2^31 * 2^8 + 2^31,
            // which is less than 2^44, meaning we won't overflow totalSeconds.
            long totalSeconds = (long)hour * 3600 + (long)minute * 60 + (long)second;

            if (totalSeconds > MaxSeconds || totalSeconds < MinSeconds)
                throw new Exception("Argument out of range: TimeSpan too long.");

            return totalSeconds * 10000000; // => totalSeconds * TimeSpan.TicksPerSecond
        }

        /// <summary>
        /// Converts a <see cref="TimeSpan"/> into a human-friendly readable string.
        /// </summary>
        /// <param name="timeSpan"><see cref="TimeSpan"/> to convert (can be negative)</param>
        /// <returns>human-friendly string representation of the given <see cref="TimeSpan"/></returns>
        public static string ToHumanFriendlyString(this TimeSpan timeSpan)
        {
            if (timeSpan == TimeSpan.Zero)
                return "0 seconds";

            bool isNegative = false;
            List<string> parts = new List<string>();

            // Check for negative TimeSpan.
            if (timeSpan < TimeSpan.Zero)
            {
                isNegative = true;
                timeSpan = timeSpan.Negate(); // Make it positive for the calculations.
            }

            if (timeSpan.Days > 0)
                parts.Add($"{timeSpan.Days} day{(timeSpan.Days > 1 ? "s" : "")}");
            if (timeSpan.Hours > 0)
                parts.Add($"{timeSpan.Hours} hour{(timeSpan.Hours > 1 ? "s" : "")}");
            if (timeSpan.Minutes > 0)
                parts.Add($"{timeSpan.Minutes} minute{(timeSpan.Minutes > 1 ? "s" : "")}");
            if (timeSpan.Seconds > 0)
                parts.Add($"{timeSpan.Seconds} second{(timeSpan.Seconds > 1 ? "s" : "")}");

            // If no large amounts so far, try milliseconds.
            if (parts.Count == 0 && timeSpan.Milliseconds > 0)
                parts.Add($"{timeSpan.Milliseconds} millisecond{(timeSpan.Milliseconds > 1 ? "s" : "")}");

            // If no milliseconds, use ticks (nanoseconds).
            if (parts.Count == 0 && timeSpan.Ticks > 0)
            {
                // A tick is equal to 100 nanoseconds. While this maps well into units of time
                // such as hours and days, any periods longer than that aren't representable in
                // a succinct fashion, e.g. a month can be between 28 and 31 days, while a year
                // can contain 365 or 366 days. A decade can have between 1 and 3 leap-years,
                // depending on when you map the TimeSpan into the calendar. This is why TimeSpan
                // does not provide a "Years" property or a "Months" property.
                // Internally TimeSpan uses long (Int64) for its values, so:
                //  - TimeSpan.MaxValue = long.MaxValue
                //  - TimeSpan.MinValue = long.MinValue
                //  - TimeSpan.TicksPerMicrosecond = 10 (not available in older .NET versions)
                parts.Add($"{(timeSpan.Ticks * 10)} microsecond{((timeSpan.Ticks * 10) > 1 ? "s" : "")}");
            }

            // Join the sections with commas & "and" for the last one.
            if (parts.Count == 1)
                return isNegative ? $"Negative {parts[0]}" : parts[0];
            else if (parts.Count == 2)
                return isNegative ? $"Negative {string.Join(" and ", parts)}" : string.Join(" and ", parts);
            else
            {
                string lastPart = parts[parts.Count - 1];
                parts.RemoveAt(parts.Count - 1);
                return isNegative ? $"Negative " + string.Join(", ", parts) + " and " + lastPart : string.Join(", ", parts) + " and " + lastPart;
            }
        }

        /// <summary>
        /// uint max = 4,294,967,295 (4.29 Gbps)
        /// </summary>
        /// <returns>formatted bit-rate string</returns>
        public static string FormatBitrate(this uint amount)
        {
            var sizes = new string[]
            {
                "bps",
                "Kbps", // kilo
                "Mbps", // mega
                "Gbps", // giga
                "Tbps", // tera
            };
            var order = amount.OrderOfMagnitude();
            var speed = amount / Math.Pow(1000, order);
            return $"{speed:0.##} {sizes[order]}";
        }

        /// <summary>
        /// ulong max = 18,446,744,073,709,551,615 (18.45 Ebps)
        /// </summary>
        /// <returns>formatted bit-rate string</returns>
        public static string FormatBitrate(this ulong amount)
        {
            var sizes = new string[]
            {
                "bps",
                "Kbps", // kilo
                "Mbps", // mega
                "Gbps", // giga
                "Tbps", // tera
                "Pbps", // peta
                "Ebps", // exa
                "Zbps", // zetta
                "Ybps"  // yotta
            };
            var order = amount.OrderOfMagnitude();
            var speed = amount / Math.Pow(1000, order);
            return $"{speed:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Returns the order of magnitude (10^3)
        /// </summary>
        public static int OrderOfMagnitude(this ulong amount) => (int)Math.Floor(Math.Log(amount, 1000));

        /// <summary>
        /// Returns the order of magnitude (10^3)
        /// </summary>
        public static int OrderOfMagnitude(this uint amount) => (int)Math.Floor(Math.Log(amount, 1000));

        /// <summary>
        /// Checks to see if a date is between <paramref name="begin"/> and <paramref name="end"/>.
        /// </summary>
        /// <returns>
        /// <c>true</c> if <paramref name="dt"/> is between <paramref name="begin"/> and <paramref name="end"/>, otherwise <c>false</c>
        /// </returns>
        public static bool IsBetween(this DateTime dt, DateTime begin, DateTime end) => dt.Ticks >= begin.Ticks && dt.Ticks <= end.Ticks;

        /// <summary>
        /// Determine if the current time is between two <see cref="TimeSpan"/>s.
        /// </summary>
        /// <param name="ts">DateTime.Now.TimeOfDay</param>
        /// <param name="start">TimeSpan.Parse("23:00:00")</param>
        /// <param name="end">TimeSpan.Parse("02:30:00")</param>
        /// <returns><c>true</c> if between start and end, <c>false</c> otherwise</returns>
        public static bool IsBetween(this TimeSpan ts, TimeSpan start, TimeSpan end)
        {
            // Are we in the same day.
            if (start <= end)
                return ts >= start && ts <= end;

            // Are we on different days.
            return ts >= start || ts <= end;
        }

        /// <summary>
        /// Compares the current <see cref="DateTime.Now.TimeOfDay"/> to the 
        /// given <paramref name="start"/> and <paramref name="end"/> times.
        /// </summary>
        /// <returns><c>true</c> if between start and end, <c>false</c> otherwise</returns>
        public static bool IsNowBetween(string start = "10:00:00", string end = "14:00:00")
        {
            try
            {
                var tsNow = DateTime.Now.TimeOfDay;
                var tsStart = TimeSpan.Parse(start);
                var tsEnd = TimeSpan.Parse(end);
                if (tsStart <= tsEnd)
                    return tsNow >= tsStart && tsNow <= tsEnd;

                return tsNow >= tsStart || tsNow <= tsEnd;
            }
            catch (Exception ex) { Debug.WriteLine($"[ERROR] IsNowBetween: {ex.Message}"); }
            return false;
        }

        /// <summary>
        /// Compares two <see cref="DateTime"/>s ignoring the hours, minutes and seconds.
        /// </summary>
        public static bool AreDatesSimilar(this DateTime? date1, DateTime? date2)
        {
            if (date1 is null && date2 is null)
                return true;

            if (date1 is null || date2 is null)
                return false;

            return date1.Value.Year == date2.Value.Year &&
                   date1.Value.Month == date2.Value.Month &&
                   date1.Value.Day == date2.Value.Day;
        }

        /// <summary>
        /// Returns the start of the day (midnight) for a given <see cref="DateTime"/>.
        /// </summary>
        /// <param name="dateTime"><see cref="DateTime"/></param>
        /// <returns>A new DateTime representing the start of the day</returns>
        public static DateTime StartOfDay(this DateTime dateTime) => dateTime.Date; // or new DateTime(dateTime.Year, dateTime.Month, dateTime.Day);

        /// <summary>
        /// Returns the end of the day (23:59:59.999) for a given <see cref="DateTime"/>.
        /// </summary>
        /// <param name="dateTime"><see cref="DateTime"/></param>
        /// <returns>A new DateTime representing the end of the day</returns>
        public static DateTime EndOfDay(this DateTime dateTime) => new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 23, 59, 59, 999);

        /// <summary>
        /// Returns a range of <see cref="DateTime"/> objects matching the criteria provided.
        /// </summary>
        /// <example>
        /// IEnumerable<DateTime> dateRange = DateTime.Now.GetDateRangeTo(DateTime.Now.AddDays(80));
        /// </example>
        /// <param name="self"><see cref="DateTime"/></param>
        /// <param name="toDate"><see cref="DateTime"/></param>
        /// <returns><see cref="IEnumerable{DateTime}"/></returns>
        public static IEnumerable<DateTime> GetDateRangeTo(this DateTime self, DateTime toDate)
        {
            // Query Syntax:
            //IEnumerable<int> range = Enumerable.Range(0, new TimeSpan(toDate.Ticks - self.Ticks).Days);
            //IEnumerable<DateTime> dates = from p in range select self.Date.AddDays(p);

            // Method Syntax:
            IEnumerable<DateTime> dates = Enumerable.Range(0, new TimeSpan(toDate.Ticks - self.Ticks).Days).Select(p => self.Date.AddDays(p));

            return dates;
        }

        /// <summary>
        /// Returns an inclusive sequence of <see cref="TimeSpan"/>s from <paramref name="start"/> 
        /// to <paramref name="end"/>, stepping by <paramref name="step"/> each iteration.
        /// </summary>
        /// <param name="start">The first <see cref="TimeSpan"/> in the sequence.</param>
        /// <param name="end">The last <see cref="TimeSpan"/> in the sequence (inclusive).</param>
        /// <param name="step">The increment between consecutive <see cref="TimeSpan"/>s.</param>
        /// <returns><see cref="IEnumerable{T}"/></returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if <paramref name="step"/> is zero or negative,
        /// or if <paramref name="end"/> is earlier than <paramref name="start"/>.
        /// </exception>
        public static IEnumerable<TimeSpan> Range(TimeSpan start, TimeSpan end, TimeSpan step)
        {
            if (step <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");
            if (end < start)
                throw new ArgumentOutOfRangeException(nameof(end), "End must be greater than or equal to start.");

            // Calculate how many steps will fit (inclusive)
            long totalTicks = end.Ticks - start.Ticks;
            long stepTicks = step.Ticks;
            int stepCount = (int)(totalTicks / stepTicks) + 1;

            return Enumerable.Range(0, stepCount).Select(i => TimeSpan.FromTicks(start.Ticks + i * stepTicks));
        }

        /// <summary>
        /// Returns an inclusive sequence of <see cref="TimeSpan"/>s from <paramref name="start"/> 
        /// to <paramref name="end"/>, stepping by 1 tick each iteration.
        /// </summary>
        /// <param name="start">The first <see cref="TimeSpan"/> in the sequence.</param>
        /// <param name="end">The last <see cref="TimeSpan"/> in the sequence (inclusive).</param>
        /// <returns><see cref="IEnumerable{T}"/></returns>
        public static IEnumerable<TimeSpan> Range(TimeSpan start, TimeSpan end)
        {
            return Range(start, end, TimeSpan.FromTicks(1));
        }

        /// <summary>
        /// Tries to execute the given <paramref name="action"/> for a maximum of 
        /// <paramref name="max"/> time stepping by 1 additional second each iteration.
        /// </summary>
        /// <returns><c>true</c> if successful, <c>false</c> otherwise</returns>
        public static bool TryForThisLongOrUntilSuccessful(Action action, TimeSpan max)
        {
            //ThreadPool.QueueUserWorkItem(_ => {
                bool success = false;
                
                if (max <= TimeSpan.FromSeconds(1))
                    max = TimeSpan.FromSeconds(2);

                foreach (var ts in Extensions.Range(TimeSpan.FromSeconds(1), max, TimeSpan.FromSeconds(1)))
                {
                    try
                    {
                        action();
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] {ex.Message}");
                        Console.WriteLine($"Trying again in {ts.ToReadableString()}…");
                        Thread.Sleep(ts);
                    }

                    if (success)
                        break; // Exit the loop if action was successful
                }
            //});
            
            return success;
        }

        /// <summary>
        /// Schedules the given action to run once after the specified delay.
        /// This is fire-and-forget: the action runs on a ThreadPool thread.
        /// </summary>
        /// <param name="action">The callback to invoke.</param>
        /// <param name="delay">How long to wait before invoking.</param>
        /// <exception cref="ArgumentNullException">If action is null.</exception>
        public static void ExecuteAfter(Action action, TimeSpan delay, Action<Exception> onError = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            // We need to capture the timer so we can dispose it after firing
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer(_ =>
            {
                // Clean up the timer to avoid leaks
                timer.Dispose();
                try { action(); }
                catch (Exception ex) { onError?.Invoke(ex); }   
            },
            state: null,
            dueTime: delay,
            period: System.Threading.Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Asynchronously waits for the delay, then invokes the action.
        /// Exceptions thrown by the action will fault the returned Task.
        /// </summary>
        /// <param name="action">The callback to invoke.</param>
        /// <param name="delay">How long to wait before invoking.</param>
        /// <returns>A Task that completes once the action has run.</returns>
        /// <exception cref="ArgumentNullException">If action is null.</exception>
        public static async Task ExecuteAfterAsync(Action action, TimeSpan delay, CancellationToken token = default, Action<Exception> onError = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            try
            {
                // We don't care about the code below the Task continuing on the
                // original SynchronizationContext, so we'll use ConfigureAwait(false)
                // to save some thread syncing time (a small gain).
                await Task.Delay(delay, token).ConfigureAwait(false);
                action();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                //throw; // Re-throw to allow caller to handle
            }
        }

        /// <summary>
        /// Executes an action on a new thread using the <see cref="ThreadPool"/>.
        /// </summary>
        /// <param name="action">The <see cref="Action"/> to perform.</param>
        public static void RunThreaded(Action action)
        {
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] RunThreaded: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Returns an <see cref="Int32"/> amount of days between two <see cref="DateTime"/> objects.
        /// </summary>
        /// <param name="self"><see cref="DateTime"/></param>
        /// <param name="toDate"><see cref="DateTime"/></param>
        public static int GetDaysBetween(this DateTime self, DateTime toDate) => new TimeSpan(toDate.Ticks - self.Ticks).Days;

        /// <summary>
        /// Returns a <see cref="TimeSpan"/> amount between two <see cref="DateTime"/> objects.
        /// </summary>
        /// <param name="self"><see cref="DateTime"/></param>
        /// <param name="toDate"><see cref="DateTime"/></param>
        public static TimeSpan GetTimeSpanBetween(this DateTime self, DateTime toDate) => new TimeSpan(toDate.Ticks - self.Ticks);

        /// <summary>
        /// Determines if the given <paramref name="dateTime"/> is older than <paramref name="days"/>.
        /// </summary>
        /// <returns><c>true</c> if older, <c>false</c> otherwise</returns>
        public static bool IsOlderThanDays(this DateTime dateTime, double days = 1.0)
        {
            if (days.IsInvalidOrZero())
                throw new ArgumentOutOfRangeException(nameof(days), "Days cannot be zero or negative.");

            TimeSpan timeDifference = DateTime.Now - dateTime;
            return timeDifference.TotalDays > days;
        }

        /// <summary>
        /// Determine the Next date by passing in a DayOfWeek (i.e. from this date, when is the next Tuesday?)
        /// </summary>
        public static DateTime Next(this DateTime current, DayOfWeek dayOfWeek)
        {
            int offsetDays = dayOfWeek - current.DayOfWeek;
            if (offsetDays <= 0)
            {
                offsetDays += 7;
            }
            DateTime result = current.AddDays(offsetDays);
            return result;
        }

        /// <summary>
        /// Converts a DateTime to a DateTimeOffset with the specified offset
        /// </summary>
        /// <param name="date">The DateTime to convert</param>
        /// <param name="offset">The offset to apply to the date field</param>
        /// <returns>The corresponding DateTimeOffset</returns>
        public static DateTimeOffset ToOffset(this DateTime date, TimeSpan offset) => new DateTimeOffset(date).ToOffset(offset);

        /// <summary>
        /// Accounts for once the <paramref name="date1"/> is past <paramref name="date2"/>
        /// or falls within the amount of <paramref name="days"/>.
        /// </summary>
        public static bool WithinDaysOrPast(this DateTime date1, DateTime date2, double days = 7.0)
        {
            if (date1 > date2) // Account for past-due amounts.
                return true;
            else
            {
                TimeSpan difference = date1 - date2;
                return Math.Abs(difference.TotalDays) <= days;
            }
        }

        /// <summary>
        /// Multiplies the given <see cref="TimeSpan"/> by the scalar amount provided.
        /// </summary>
        public static TimeSpan Multiply(this TimeSpan timeSpan, double scalar) => new TimeSpan((long)(timeSpan.Ticks * scalar));

        /// <summary>
        /// Returns a new TimeSpan equal to the original TimeSpan divided by <paramref name="divisor"/>.
        /// </summary>
        /// <param name="timeSpan">The TimeSpan to divide.</param>
        /// <param name="divisor">A positive integer to divide the TimeSpan by.</param>
        /// <returns>A TimeSpan representing timeSpan/divisor.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="divisor"/> is less than or equal to zero.</exception>
        public static TimeSpan Divide(this TimeSpan timeSpan, int divisor)
        {
            if (divisor <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(divisor), "Divisor must be greater than zero.");

            // TimeSpan.Ticks is a long (100‐ns units). Integer division truncates any remainder.
            long newTicks = timeSpan.Ticks / divisor;
            return TimeSpan.FromTicks(newTicks);
        }

        /// <summary>
        /// Returns the average TimeSpan from a sequence.
        /// Throws <see cref="InvalidOperationException"/> if the sequence is empty.
        /// </summary>
        public static TimeSpan Average(this IEnumerable<TimeSpan> source)
        {
            if (source == null)
                return TimeSpan.Zero;

            // We need at least one element to compute an average
            long count = source.LongCount();
            if (count == 0)
                throw new InvalidOperationException("Sequence contains no elements");

            // Average the underlying Ticks and reconstruct a TimeSpan
            double avgTicks = source.Average(ts => ts.Ticks);
            return TimeSpan.FromTicks(Convert.ToInt64(avgTicks));
        }

        /// <summary>
        /// Gets a <see cref="DateTime"/> object representing the time until midnight.
        /// <example><code>
        /// var hoursUntilMidnight = TimeUntilMidnight().TimeOfDay.TotalHours;
        /// </code></example>
        /// </summary>
        public static DateTime TimeUntilMidnight()
        {
            DateTime now = DateTime.Now;
            DateTime midnight = now.Date.AddDays(1);
            TimeSpan timeUntilMidnight = midnight - now;
            return new DateTime(timeUntilMidnight.Ticks);
        }

        public static IEnumerable<(T value, int index)> WithIndex<T>(this IEnumerable<T> source) => source.Select((value, index) => (value, index));

        /// <summary>
        /// Writes each string in the array as a new line to the specified file.
        /// Overwrites the file if it already exists.
        /// </summary>
        /// <param name="filePath">The full path to the output file.</param>
        /// <param name="lines">An array of strings to write.</param>
        /// <param name="append"><c>true</c> or <c>false</c></param>
        public static void WriteToFile(this string[] lines, string filePath, bool append = false)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var writer = new StreamWriter(filePath, append, Encoding.UTF8))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Calls <see cref="WriteToFile(string[], string, bool)"/> with a list of strings."/>
        /// </summary>
        public static void WriteToFile(this List<string> lines, string filePath, bool append = false)
        {
            WriteToFile(lines.ToArray(), filePath, append);
        }

        /// <summary>
        /// When you have an old‐style BeginXxx/EndXxx Asynchronous Programming Model (APM pattern) and you want to 
        /// convert it into a modern Task-based Asynchronous Pattern (TAP) style so you can await it, you can use 
        /// <see cref="TaskFactory.FromAsync(Func{AsyncCallback, object, IAsyncResult}, Action{IAsyncResult}, object)"/>.
        /// </summary>
        public static Task<int> FileReadAsync(FileStream fs, byte[] buffer, int offset, int count)
        {
            // Factory.FromAsync takes a BeginMethod, an EndMethod, the state to pass (null if you don’t need one).
            return Task<int>.Factory.FromAsync((cb, state) =>
                fs.BeginRead(buffer, offset, count, cb, state),
                fs.EndRead,
                state: null);
        }

        /// <summary>
        /// TAP helper - Wraps an APM operation with a result (TResult) into a Task<TResult>.
        /// </summary>
        public static Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object state = null)
        {
            return Task.Factory.FromAsync(beginMethod, endMethod, state);
        }

        /// <summary>
        /// TAP helper - Wraps an APM operation that returns void into a Task.
        /// </summary>
        public static Task FromAsync(Func<AsyncCallback, object, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object state = null)
        {
            return Task.Factory.FromAsync(beginMethod, endMethod, state);
        }

        public static Encoding SniffEncoding(this FileInfo file) => SniffEncoding(file.FullName);
        public static Encoding SniffEncoding(string filePath = @"C:\path\to\config.xml")
        {
            try
            {   // detectEncodingFromByteOrderMarks = true enables BOM sniffing
                using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    return reader.CurrentEncoding;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{MethodInfo.GetCurrentMethod().Name}[ERROR]: {ex.Message}");
            }
            return Encoding.Default;
        }

        /// <summary>
        /// Gets the full path for a temporary file.
        /// </summary>
        /// <param name="suffix">the extension</param>
        /// <returns>full path to temp file</returns>
        /// <remarks><see cref="Path.GetTempFileName"/> can also be used</remarks>
        public static string GetTempFileName(string suffix)
        {
            var tempDirectory = Path.GetTempPath();
            for (;;)
            {   // 8DOT3 format
                var fileName = Guid.NewGuid().ToString().Substring(0, 8) + suffix;
                var tempPath = Path.Combine(tempDirectory, fileName);
                if (!File.Exists(tempPath))
                    return tempPath;
            }
        }

        /// <summary>
        /// Iterate through all files in the path and return a collection of <see cref="FileInfo"/>.
        /// </summary>
        /// <returns>collection of <see cref="IEnumerable{T}"/></returns>
        public static IEnumerable<FileInfo> GetDirectoryFilesInfo(string path, string ext = "*.dll", System.IO.SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            foreach (var f in Directory.GetFiles(path, ext, searchOption))
            {
                yield return new FileInfo(f);
            }
        }

        /// <summary>
        /// IEnumerable file list using recursion.
        /// </summary>
        /// <param name="basePath">root folder to search</param>
        /// <returns><see cref="IEnumerable{T}"/></returns>
        public static IEnumerable<string> GetAllFilesUnder(string basePath)
        {
            foreach (var f in Directory.GetFiles(basePath))
                yield return f;

            foreach (var d in Directory.GetDirectories(basePath).Select(GetAllFilesUnder).SelectMany(files => files))
                yield return d;
        }

        /// <summary>
        /// Appends the string representation of each element in <paramref name="values"/>,
        /// separated by <paramref name="separator"/>, to the StringBuilder.
        /// Mimics StringBuilder.AppendJoin(IEnumerable&lt;T&gt;) in .NET Core.
        /// </summary>
        public static StringBuilder AppendJoin<T>(this StringBuilder sb, string separator, IEnumerable<T> values)
        {
            if (sb == null) 
                throw new ArgumentNullException(nameof(sb));
            if (separator == null) 
                throw new ArgumentNullException(nameof(separator));
            if (values == null) 
                return sb;

            using (var e = values.GetEnumerator())
            {
                if (!e.MoveNext())
                    return sb;  // nothing to append

                // append first element
                sb.Append(e.Current);

                // append remaining with separator
                while (e.MoveNext())
                {
                    sb.Append(separator)
                      .Append(e.Current);
                }
            }

            return sb;
        }

        /// <summary>
        /// Overload that takes a params array.
        /// </summary>
        public static StringBuilder AppendJoin<T>(this StringBuilder sb, string separator, params T[] values) => sb.AppendJoin(separator, (IEnumerable<T>)values);

        /// <summary>
        /// Overload that takes a single char as separator.
        /// </summary>
        public static StringBuilder AppendJoin<T>(this StringBuilder sb, char separator, IEnumerable<T> values) => sb.AppendJoin(separator.ToString(), values);

        /// <summary>
        /// Creates a new Dictionary&lt;string,bool&gt; containing the same entries as the source.
        /// </summary>
        public static Dictionary<string, bool> Clone(this IDictionary<string, bool> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            // The built-in ctor taking IDictionary will copy all key/value pairs
            return new Dictionary<string, bool>(source);
        }

        /// <summary>
        /// Copies all entries from source into target. Clears the target first.
        /// </summary>
        public static void CopyTo(this IDictionary<string, bool> source, IDictionary<string, bool> target)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            target.Clear();
            foreach (var kvp in source)
            {
                target[kvp.Key] = kvp.Value;
            }
        }

        public static string NameOf(this object o)
        {
            if (o == null)
                return "null";

            // Similar: System.Reflection.MethodBase.GetCurrentMethod().DeclaringType.Name
            return $"{o.GetType().Name} ⇒ {o.GetType().BaseType.Name}";
        }

        public static bool IsDisposable(this Type type)
        {
            if (!typeof(IDisposable).IsAssignableFrom(type))
                return false; //throw new ArgumentException($"Type not disposable: {type.Name}");

            return true;
        }

        public static bool IsClonable(this Type type)
        {
            if (!typeof(ICloneable).IsAssignableFrom(type))
                return false; //throw new ArgumentException($"Type not clonable: {type.Name}");

            return true;
        }

        public static bool IsComparable(this Type type)
        {
            if (!typeof(IComparable).IsAssignableFrom(type))
                return false; //throw new ArgumentException($"Type not comparable: {type.Name}");

            return true;
        }

        public static bool IsConvertible(this Type type)
        {
            if (!typeof(IConvertible).IsAssignableFrom(type))
                return false; //throw new ArgumentException($"Type not convertible: {type.Name}");

            return true;
        }

        public static bool IsFormattable(this Type type)
        {
            if (!typeof(IFormattable).IsAssignableFrom(type))
                return false; //throw new ArgumentException($"Type not formattable: {type.Name}");

            return true;
        }

        public static bool IsEnumerable<T>(this Type type)
        {
            if (!typeof(IEnumerable<T>).IsAssignableFrom(type))
                return false; //throw new ArgumentException($"Type not enumerable: {type.Name}");
            return true;
        }

        /// <summary>
        ///   Returns a multi‐line string of all public static properties on the given type.
        ///   Each line is formatted as "PropertyName = PropertyValue".
        /// </summary>
        /// <remarks>
        ///   Be wary of recursive conditions.
        /// </remarks>
        public static string DumpPublicStaticProperties(this Type type)
        {
            if (type == null)
                return string.Empty;

            var sb = new StringBuilder();
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Static).OrderBy(p => p.Name);
            foreach (var prop in props)
            {
                object value;
                try
                {   // static property => pass null for the instance
                    value = prop.GetValue(null);
                }
                catch (Exception ex)
                {
                    value = $"<error: {ex.GetType().Name}>";
                }
                sb.Append(prop.Name).Append(" = ").AppendLine(value?.ToString() ?? "null");
            }
            return $"{sb}";
        }

        /// <summary>
        /// Reflective AssemblyInfo attributes
        /// </summary>
        public static string ReflectAssemblyFramework(Type type)
        {
            try
            {
                System.Reflection.Assembly assembly = type.Assembly;
                if (assembly != null)
                {
                    var fileVerAttr = (FileVersionAttribute)assembly.GetCustomAttributes(typeof(FileVersionAttribute), false)[0];
                    var confAttr = (ConfigurationAttribute)assembly.GetCustomAttributes(typeof(ConfigurationAttribute), false)[0];
                    var frameAttr = (TargetFrameworkAttribute)assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)[0];
                    var compAttr = (CompanyAttribute)assembly.GetCustomAttributes(typeof(CompanyAttribute), false)[0];
                    var nameAttr = (ProductAttribute)assembly.GetCustomAttributes(typeof(ProductAttribute), false)[0];
                    return string.Format("{0} {1} {2} {4} – {3} ({5})", nameAttr.Product, fileVerAttr.Version, string.IsNullOrEmpty(confAttr.Configuration) ? "–" : confAttr.Configuration, string.IsNullOrEmpty(frameAttr.FrameworkDisplayName) ? frameAttr.FrameworkName : frameAttr.FrameworkDisplayName, !string.IsNullOrEmpty(compAttr.Company) ? compAttr.Company : Environment.UserName, Environment.OSVersion);
                }
            }
            catch (Exception) { }
            return string.Empty;
        }

        /// <summary>
        /// Fetch all referenced <see cref="System.Reflection.AssemblyName"/> used by the current process.
        /// </summary>
        /// <returns><see cref="List{T}"/></returns>
        public static List<string> ListAllAssemblies()
        {
            List<string> results = new List<string>();
            try
            {
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                System.Reflection.AssemblyName main = assembly.GetName();
                results.Add($"Main Assembly: {main.Name}, Version: {main.Version}");
                IOrderedEnumerable<System.Reflection.AssemblyName> names = assembly.GetReferencedAssemblies().OrderBy(o => o.Name);
                foreach (var sas in names)
                    results.Add($"Sub Assembly: {sas.Name}, Version: {sas.Version}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] ListAllAssemblies: {ex.Message}");
            }
            return results;
        }

        public static void DumpProcessModuleCollection()
        {
            string self = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".exe";
            System.Diagnostics.ProcessModuleCollection pmc = System.Diagnostics.Process.GetCurrentProcess().Modules;
            IOrderedEnumerable<ProcessModule> pmQuery = pmc
                .OfType<ProcessModule>()
                .Where(pt => pt.ModuleMemorySize > 0)
                .OrderBy(o => o.ModuleName);
            foreach (var item in pmQuery)
            {
                if (!item.ModuleName.Contains($"{self}"))
                    Console.WriteLine($"> Module Name: {item.ModuleName}, v{item.FileVersionInfo.FileVersion}");
                try { item.Dispose(); }
                catch { }
            }
        }

        public static T Retry<T>(this Func<T> operation, int attempts)
        {
            while (true)
            {
                try
                {
                    attempts--;
                    return operation();
                }
                catch (Exception ex) when (attempts > 0)
                {
                    Console.WriteLine($"Failed: {ex.Message}");
                    Console.WriteLine($"Attempts left: {attempts}");
                    Thread.Sleep(2000);
                }
            }
        }

        /// <summary>
        /// Func<string, int> getUserId = (id) => { ... };
        /// int userId = getUserId.Retry(3)("Email");
        /// </summary>
        public static Func<TArg, TResult> Retry<TArg, TResult>(this Func<TArg, TResult> func, int maxRetry, int retryDelay = 2000)
        {
            return (arg) =>
            {
                int tryCount = 0;
                while (true)
                {
                    try
                    {
                        return func(arg);
                    }
                    catch (Exception ex)
                    {
                        if (++tryCount > maxRetry)
                        {
                            throw new Exception($"Retry attempts exhausted: {ex.Message}", ex);
                        }
                        else
                        {
                            Console.WriteLine($"Failed: {ex.Message}");
                            Console.WriteLine($"Attempts left: {maxRetry - tryCount}");
                            Thread.Sleep(retryDelay);
                        }
                    }
                }
            };
        }

        /// <summary>
        ///   Generic retry mechanism with exponential back-off
        /// <example><code>
        ///   Retry(() => MethodThatHasNoReturnValue());
        /// </code></example>
        /// </summary>
        public static void Retry(this Action action, int maxRetry = 3, int retryDelay = 1000)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    action();
                    break;
                }
                catch (Exception ex)
                {
                    retries++;
                    if (retries > maxRetry)
                    {
                        throw new TimeoutException($"Operation failed after {maxRetry} retries: {ex.Message}", ex);
                    }
                    Console.WriteLine($"⇒ Retry {retries}/{maxRetry} after failure: {ex.Message}. Retrying in {retryDelay} ms...");
                    Thread.Sleep(retryDelay);
                    retryDelay *= 2; // Double the delay after each attempt.
                }
            }
        }

        /// <summary>
        ///   Modified retry mechanism for return value with exponential back-off.
        /// <example><code>
        ///   int result = Retry(() => MethodThatReturnsAnInteger());
        /// </code></example>
        /// </summary>
        public static T Retry<T>(this Func<T> func, int maxRetry = 3, int retryDelay = 1000)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    return func();
                }
                catch (Exception ex)
                {
                    retries++;
                    if (retries > maxRetry)
                    {
                        throw new TimeoutException($"Operation failed after {maxRetry} retries: {ex.Message}", ex);
                    }
                    Console.WriteLine($"⇒ Retry {retries}/{maxRetry} after failure: {ex.Message}. Retrying in {retryDelay} ms...");
                    Thread.Sleep(retryDelay);
                    retryDelay *= 2; // Double the delay after each attempt.
                }
            }
        }

        /// <summary>
        ///   Generic retry mechanism with exponential back-off
        /// <example><code>
        ///   await RetryAsync(() => AsyncMethodThatHasNoReturnValue());
        /// </code></example>
        /// </summary>
        public static async Task RetryAsync(this Func<Task> action, int maxRetry = 3, int retryDelay = 1000)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    await action();
                    break;
                }
                catch (Exception ex)
                {
                    retries++;
                    if (retries > maxRetry)
                    {
                        throw new InvalidOperationException($"Operation failed after {maxRetry} retries: {ex.Message}", ex);
                    }
                    Console.WriteLine($"⇒ Retry {retries}/{maxRetry} after failure: {ex.Message}. Retrying in {retryDelay} ms...");
                    await Task.Delay(retryDelay);
                    retryDelay *= 2; // Double the delay after each attempt.
                }
            }
        }

        /// <summary>
        ///   Modified retry mechanism for return value with exponential back-off.
        /// <example><code>
        ///   int result = await RetryAsync(() => AsyncMethodThatReturnsAnInteger());
        /// </code></example>
        /// </summary>
        public static async Task<T> RetryAsync<T>(this Func<Task<T>> func, int maxRetry = 3, int retryDelay = 1000)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    return await func();
                }
                catch (Exception ex)
                {
                    retries++;
                    if (retries > maxRetry)
                    {
                        throw new InvalidOperationException($"Operation failed after {maxRetry} retries: {ex.Message}", ex);
                    }
                    Console.WriteLine($"⇒ Retry {retries}/{maxRetry} after failure: {ex.Message}. Retrying in {retryDelay} ms...");
                    await Task.Delay(retryDelay);
                    retryDelay *= 2; // Double the delay after each attempt.
                }
            }
        }

        /// <summary>
        /// Tests whether an array contains the index, and returns the value if true or the defaultValue if false
        /// </summary>
        /// <param name="array"></param>
        /// <param name="index"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public static string GetIndex(this string[] array, int index, string defaultValue = "") => (index < array.Length) ? array[index] : defaultValue;

        /// <summary>
        /// Chunks a large list into smaller n-sized list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list">List to be chunked</param>
        /// <param name="nSize">Size of chunks</param>
        /// <returns>IEnumerable list of <typeparam name="T"></typeparam> broken up into <paramref name="nSize"/> chunks</returns>
        public static IEnumerable<List<T>> SplitList<T>(List<T> list, int nSize = 30)
        {
            for (int i = 0; i < list.Count; i += nSize)
            {
                yield return list.GetRange(i, Math.Min(nSize, list.Count - i));
            }
        }

        public static IEnumerable<T> JoinLists<T>(this IEnumerable<T> list1, IEnumerable<T> list2)
        {
            var joined = new[] { list1, list2 }.Where(x => x != null).SelectMany(x => x);
            return joined ?? Enumerable.Empty<T>();
        }

        public static IEnumerable<T> JoinMany<T>(params IEnumerable<T>[] array)
        {
            var final = array.Where(x => x != null).SelectMany(x => x);
            return final ?? Enumerable.Empty<T>();
        }

        /// <summary>
        /// A more accurate averaging method by removing the outliers.
        /// </summary>
        public static int CalculateMedian(this List<int> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            values.Sort();

            // Find the middle index
            int count = values.Count;
            float medianAverage;

            if (count % 2 == 0)
            {   // Even number of elements: average the two middle elements
                int mid1 = count / 2 - 1;
                int mid2 = count / 2;
                medianAverage = (values[mid1] + values[mid2]) / 2.0f;
            }
            else
            {   // Odd number of elements: take the middle element
                int mid = count / 2;
                medianAverage = values[mid];
            }

            return (int)medianAverage;
        }

        /// <summary>
        /// Adds an ordinal to a number.
        /// int number = 1;
        /// var ordinal = number.AddOrdinal(); // 1st
        /// </summary>
        /// <param name="number">The number to add the ordinal too.</param>
        /// <returns>A string with an number and ordinal</returns>
        public static string AddOrdinal(this int number)
        {
            if (number <= 0)
                return number.ToString();

            switch (number % 100)
            {
                case 11: case 12: case 13: return number + "th";
            }

            switch (number % 10)
            {
                case 1: return number + "st";
                case 2: return number + "nd";
                case 3: return number + "rd";
                default: return number + "th";
            }
        }

    }
}
