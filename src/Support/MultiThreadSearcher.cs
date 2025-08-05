using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace csfind
{
    /// <summary>
    /// Utilizes multiple <see cref="System.Threading.Thread"/>s to search for files matching a specified pattern.
    /// Employs a thread-safe queue to manage directories and a concurrent bag to store matched files.
    /// Up to four <see cref="System.Threading.Thread"/>s will be used by default, but this can be adjusted.
    /// </summary>
    public class MultiThreadSearcher
    {
        #region ◁ Local Scope ▷
        int _activeThreads = 0;
        int _totalDirectories = 0;
        readonly int _maxThreads;
        readonly int _numMonths;
        readonly string _filePattern;
        readonly Timer _timer;
        readonly bool _verbose;
        readonly ConcurrentQueue<string> _directoryQueue = new ConcurrentQueue<string>();
        readonly ConcurrentBag<string> _matchedFiles = new ConcurrentBag<string>();
        public event EventHandler<string> OnStopwatch;
        #endregion

        #region ◁ Public ▷
        /// <summary>
        /// Creates a new instance of <see cref="MultiThreadSearcher"/> with the specified parameters.
        /// </summary>
        /// <param name="filePattern">the file pattern to match, e.g. "web.config"</param>
        /// <param name="maxThreads">the maximum amount of threads to create</param>
        /// <param name="verbose">if <c>true</c> a timer will report the current status of the search every 6 seconds</param>
        public MultiThreadSearcher(string filePattern, int maxThreads = 4, int numMonths = 0, bool verbose = false)
        {
            _filePattern = filePattern;
            _maxThreads = maxThreads;
            _numMonths = numMonths;
            _verbose = verbose;

            if (_verbose)
            {
                string formatReport = "ActiveThreads… {0,-4} QueuedDirectories… {1,-8} MatchedFiles… {2,-8}"; //negative left-justifies, while positive right-justifies
                _timer = new Timer((state) =>
                {
                    Console.Write($"  {String.Format(formatReport, _activeThreads, _directoryQueue.Count, _matchedFiles.Count)} \r");
                }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            }
        }

        public List<string> Search(string rootDirectory, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(rootDirectory))
                throw new DirectoryNotFoundException($"Root directory not found \"{rootDirectory}\"");

            _directoryQueue.Enqueue(rootDirectory);
            var threads = new List<Thread>();

            for (int i = 0; i < _maxThreads; i++)
            {
                Debug.WriteLine($"[{LogLevel.Debug}] Starting thread {i + 1} of {_maxThreads}");
                var thread = new Thread(() => SearchWorker(cancellationToken));
                thread.IsBackground = true; // Allow app to exit even if threads are running
                thread.Priority = ThreadPriority.Lowest;
                thread.Name = $"Searcher_{i+1}";
                thread.Start();
                threads.Add(thread);
                Thread.Sleep(500); // Need a delay here for the atomic count to be correct.
            }

            // Wait for all threads to complete
            foreach (var thread in threads)
                thread.Join();

            if (_verbose)
            {
                _timer?.Dispose();
                Console.WriteLine();
            }

            return _matchedFiles.ToList();
        }

        public int GetTotalDirectoryCount() => _totalDirectories;
        #endregion

        #region ◁ Private ▷
        /// <summary>
        /// We dequeue directories from the <see cref="ConcurrentQueue{T}"/> and process each one in a thread.
        /// There should never be overlapping directories processed by different threads.
        /// </summary>
        /// <param name="token"><see cref="CancellationToken"/></param>
        void SearchWorker(CancellationToken token)
        {
            Interlocked.Increment(ref _activeThreads);
            while (!token.IsCancellationRequested && _directoryQueue.TryDequeue(out string currentDir))
            {
                if (token.IsCancellationRequested) 
                    return;

                ProcessDirectory(currentDir, token);
            }
            Interlocked.Decrement(ref _activeThreads);
        }

        void ProcessDirectory(string dir, CancellationToken token)
        {
            if (token.IsCancellationRequested) 
                return;

            var startTime = DateTime.Now;

            try
            {
                // Search matching files
                var files = Directory.GetFiles(dir, _filePattern);
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) 
                        return;

                    if (file.EndsWith("Results.log", StringComparison.OrdinalIgnoreCase))
                        continue; // don't self-reference

                    if (_numMonths > 0)
                    {
                        var threshold = DateTime.Now.AddMonths(-_numMonths);
                        if (new FileInfo(file).LastWriteTime >= threshold)
                            _matchedFiles.Add(file);
                    }
                    else
                        _matchedFiles.Add(file);

                    // TODO: Add date matching parameter instead of hard-coding
                    //if (new FileInfo(file).LastWriteTime.IsBetween(new DateTime(2022, 1, 1), DateTime.Now))
                    //    _matchedFiles.Add(file);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }
            catch (Exception) { }

            try
            {
                // Discover subdirectories
                var subDirs = Directory.GetDirectories(dir);
                foreach (var subDir in subDirs)
                {
                    if (token.IsCancellationRequested)
                        return;
                    Interlocked.Increment(ref _totalDirectories);
                    _directoryQueue.Enqueue(subDir);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }
            catch (Exception) { }

            var elapsed = DateTime.Now - startTime;
            OnStopwatch?.Invoke(null, $"{elapsed.ToReadableTime()} elapsed time for directory '{dir}' ");
        }
        #endregion

        //public static void RunTest1(string rootDirectory = @"D:\", CancellationToken token = default)
        //{
        //    Console.WriteLine($"\n⇒ Beginning search...");
        //    var searcher = new MultiThreadSearcher("*.config", 4, true);
        //    var matches = searcher.Search(rootDirectory, token);
        //    foreach (var file in matches)
        //        Console.WriteLine($"\t{file}");
        //    Console.WriteLine($"\n⇒ Found {matches.Count} total matches");
        //}
    }

    #region ◁ TermSearcher ▷
    /// <summary>
    /// Similar to <see cref="MultiTermSearcher"/>, but <see cref="Task"/>-based, and includes the use of <see cref="MatchResult"/> objects.
    /// </summary>
    public class MultiTermSearcher
    {
        #region ◁ Local Scope ▷
        readonly ConcurrentQueue<string> _directoryQueue = new ConcurrentQueue<string>();
        readonly ConcurrentBag<MatchResult> _matches = new ConcurrentBag<MatchResult>();
        readonly string _filePattern;
        readonly string _contentKeyword;
        readonly List<string> _multiTermMatch;
        readonly double _requiredMatchPercent;
        readonly int _maxParallelism;
        readonly Timer _timer;
        readonly bool _verbose = false;
        int _activeThreads = 0;
        public event EventHandler<string> OnStopwatch;
        #endregion

        #region ◁ Public ▷
        public MultiTermSearcher(string filePattern, string contentKeyword = null, List<string> multiTermMatch = null, double requiredMatchPercent = 0.8, int maxParallelism = 4, bool verbose = false)
        {
            _filePattern = filePattern;
            _contentKeyword = contentKeyword;
            _multiTermMatch = multiTermMatch;
            _requiredMatchPercent = requiredMatchPercent;
            _maxParallelism = maxParallelism;
            _verbose = verbose;
            
            if (_verbose)
            {
                string formatReport = "ActiveThreads… {0,-4} QueuedDirectories… {1,-8} Matches… {2,-8}"; //negative left-justifies, while positive right-justifies
                _timer = new Timer((state) =>
                {
                    Console.Write($"  {String.Format(formatReport, _activeThreads, _directoryQueue.Count, _matches.Count)} \r");
                }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            }
        }

        public List<MatchResult> Search(string rootDirectory, CancellationToken token)
        {
            if (!Directory.Exists(rootDirectory))
                throw new DirectoryNotFoundException($"Directory not found: {rootDirectory}");

            _directoryQueue.Enqueue(rootDirectory);
            var tasks = new List<Task>();

            for (int i = 0; i < _maxParallelism; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                tasks.Add(Task.Run(() => SearchWorker(token), token));
                Thread.Sleep(500); // Need a delay here for the atomic count to be correct.
            }

            try { Task.WaitAll(tasks.ToArray(), token); }
            catch (OperationCanceledException) { } // Handle cancellations gracefully

            if (_verbose)
            {
                _timer?.Dispose();
                Console.WriteLine();
            }

            return _matches.ToList();
        }
        #endregion

        #region ◁ Private ▷
        /// <summary>
        /// We dequeue directories from the <see cref="ConcurrentQueue{T}"/> and process each one in a thread.
        /// There should never be overlapping directories processed by different threads.
        /// </summary>
        /// <param name="token"><see cref="CancellationToken"/></param>
        void SearchWorker(CancellationToken token)
        {
            Interlocked.Increment(ref _activeThreads);
            while (!token.IsCancellationRequested && _directoryQueue.TryDequeue(out var dir))
            {
                ProcessDirectory(dir, token);
            }
            Interlocked.Decrement(ref _activeThreads);
        }

        void ProcessDirectory(string dir, CancellationToken token)
        {
            if (token.IsCancellationRequested) 
                return;

            string[] files = Array.Empty<string>();
            string[] subDirs = Array.Empty<string>();

            var startTime = DateTime.Now;
            
            try { files = Directory.GetFiles(dir, _filePattern); } catch { }
            try { subDirs = Directory.GetDirectories(dir); } catch { }

            foreach (var file in files)
            {
                if (token.IsCancellationRequested) 
                    return;

                if (file.EndsWith("Results.log", StringComparison.OrdinalIgnoreCase))
                    continue; // don't self-reference

                ProcessFile(file, token);
            }

            foreach (var subDir in subDirs)
            {
                if (token.IsCancellationRequested) 
                    return;
                _directoryQueue.Enqueue(subDir);
            }

            var elapsed = DateTime.Now - startTime;
            OnStopwatch?.Invoke(null, $"{elapsed.ToReadableTime()} elapsed time for directory '{dir}' ");
        }

        void ProcessFile(string filePath, CancellationToken token)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line = string.Empty;
                    int lineNumber = 0;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (token.IsCancellationRequested)
                            return;

                        lineNumber++;

                        // Match multi-term threshold
                        if (_multiTermMatch != null && _multiTermMatch.Count > 0)
                        {
                            int matches = _multiTermMatch.Count(term => line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                            double percent = (double)matches / _multiTermMatch.Count;
                            if (percent >= _requiredMatchPercent)
                            {
                                _matches.Add(new MatchResult(filePath, lineNumber, line));
                                return;
                            }
                        }

                        // Match keyword
                        if (!string.IsNullOrEmpty(_contentKeyword) && line.ToLower().Contains(_contentKeyword.ToLower()))
                        {
                            _matches.Add(new MatchResult(filePath, lineNumber, line));
                            return;
                        }
                    }
                }
            }
            catch { }
        }
        #endregion
    }

    /// <summary>
    /// Data object for <see cref="MultiTermSearcher"/>.
    /// </summary>
    public class MatchResult
    {
        public string FilePath { get; }
        public int LineNumber { get; }
        public string LineData { get; }
        public MatchResult(string filePath, int lineNumber, string lineData)
        {
            FilePath = filePath;
            LineNumber = lineNumber;
            LineData = lineData;
        }
        public override string ToString() => $"{FilePath}\t[Line {LineNumber}]\t{LineData}";
    }

    #endregion

    /* [Regex Support]

      // ============================================
      //  Regex Line Matching (optional enhancement)
      // ============================================

        public class MultiThreadRegexSearcher
        {
            // Existing fields...
            private readonly Regex? _lineRegex;

            public MultiThreadRegexSearcher(
                string filePattern,
                string? contentKeyword = null,
                List<string>? multiTermMatch = null,
                double requiredMatchPercent = 0.8,
                Regex? lineRegex = null,
                int maxParallelism = 4)
            {
                _filePattern = filePattern;
                _contentKeyword = contentKeyword;
                _multiTermMatch = multiTermMatch;
                _requiredMatchPercent = requiredMatchPercent;
                _lineRegex = lineRegex;
                _maxParallelism = maxParallelism;
            }
            // rest of the class...

    
      // ============================================
      //  Update IsMatch() to Include Regex Logic
      // ============================================

        bool IsMatch(string filePath, CancellationToken token)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    if (token.IsCancellationRequested) return false;

                    // 1. Regex match (highest priority)
                    if (_lineRegex != null && _lineRegex.IsMatch(line))
                        return true;

                    // 2. Multi-term percentage match
                    if (_multiTermMatch != null && _multiTermMatch.Count > 0)
                    {
                        int matches = _multiTermMatch.Count(term =>
                            line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

                        double percent = (double)matches / _multiTermMatch.Count;
                        if (percent >= _requiredMatchPercent)
                            return true;
                    }

                    // 3. Simple keyword match
                    if (_contentKeyword != null &&
                        line.Contains(_contentKeyword, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

      // ============================================
      //  Example Usage
      // ============================================

        var regex = new Regex(@"\b(error|fail|exception)\b", RegexOptions.IgnoreCase);
        var searcher = new MultiThreadRegexSearcher("*.log", lineRegex: regex);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        var matches = searcher.Search(@"C:\Logs", cts.Token);
        Console.WriteLine($"Regex matched in {matches.Count} files:");
        foreach (var file in matches)
            Console.WriteLine(file);

    */
}
