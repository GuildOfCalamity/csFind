using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace csfind
{
    /// <summary>
    /// ╒◍──────────────────◍╕
    /// │   C# Grep Utility   │
    /// ╘◍──────────────────◍╛
    /// </summary>
    internal class Program
    {
        #region ◁ Local scope ▷
        public static bool _debugMode = false;
        public static bool _locateMode = false;
        public static bool _mtsMode = false;
        static bool _noIssue = true;
        static string _driveText = "C:\\";
        static string _patternText = "*.config";
        static int _totalMatchCount = 0;
        static int _numThreads = 4;
        static int _numMonths = 0;
        static double _mtsPercent = 0.8; // 80% required for multi-term search
        static CancellationTokenSource _cts;
        static List<string> _terms = new List<string>();
        #endregion

        static void Main(string[] args)
        {
            #region ◁ Init ▷
            AppDomain.MonitoringIsEnabled = true;
            Console.OutputEncoding = Encoding.UTF8;
            ConfigManager.OnError += OnConfigError;

            var timeout = ConfigManager.Get<double>(Keys.TimeoutInMinutes, defaultValue: 120);
            var truncate = ConfigManager.Get<int>(Keys.TruncateLength, defaultValue: 100);
            _cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeout));
            var level = ConfigManager.Get<LogLevel>(Keys.LogLevel);
            var lastCount = ConfigManager.Get(Keys.LastCount, defaultValue: "0");
            var lastCmd = ConfigManager.Get<string>(Keys.LastCommand, defaultValue: string.Empty);
            var showStats = ConfigManager.Get<bool>(Keys.ShowStats, defaultValue: true);
            var appendLog = ConfigManager.Get<bool>(Keys.AppendLog, defaultValue: true);
            var firstRun = ConfigManager.Get<bool>(Keys.FirstRun, defaultValue: true);
            if (!appendLog)
            {
                // If the user doesn't want to append the log, then rename existing log each run.
                var log = Logger.GetLogName();
                if (File.Exists(log))
                {
                    var fi = new FileInfo(log);
                    Extensions.MoveTo(fi, $"{fi.FullName}.previous");
                }
            }
            Logger.Write(Extensions.ReflectAssemblyFramework(typeof(Program)), level: LogLevel.Init);
            #endregion

            #region ◁ Domain ▷
            var framework = AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName; // ".NETFramework,Version=v4.8"
            var appbase = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            var cfgfile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
            var cachepath = AppDomain.CurrentDomain.SetupInformation.CachePath ?? Path.GetTempPath();
            //AppDomain.CurrentDomain.SetupInformation.AppDomainInitializer += (string[] arguments) => { Console.WriteLine($"[AppDomainInitializer] Length={arguments.Length}"); };
            //AppDomain.CurrentDomain.ProcessExit += (sender, e) => { Logger.Write($"Process exited at {DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss.fff tt", CultureInfo.InvariantCulture)}"); };

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _cts?.Cancel();
                Console.WriteLine();
                Logger.Write("▷ Process canceled by user! ", level: LogLevel.Warning);
                Console.ForegroundColor = ConsoleColor.Gray;
                if (args.Length == 0)
                {
                    Extensions.ExecuteAfter(() => { ForceExitNow(true); }, TimeSpan.FromSeconds(1));
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Logger.Write($"\r\nUnhandledException: {(e.ExceptionObject as Exception)}", level: LogLevel.Critical);
                _noIssue = false;
            };

            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                if (e.Exception != null && // Ignore common exceptions that are not critical
                   !e.Exception.Message.StartsWith("Could not load file or assembly") &&
                   !e.Exception.Message.StartsWith("Could not find a part of the path") &&
                   !e.Exception.Message.StartsWith("The symbolic link cannot be followed") &&
                   !e.Exception.Message.StartsWith("The process cannot access the file") &&
                   !e.Exception.Message.StartsWith("The operation was canceled") &&
                   !e.Exception.Message.StartsWith("Access to the path"))
                {
                    Logger.Write($"\r\nFirstChanceException: {e.Exception.Message}", level: LogLevel.Error);
                    _noIssue = false;
                }
                else if (e.Exception != null &&
                    e.Exception.Message.StartsWith("Second path fragment must not be a drive or UNC name"))
                {
                    Logger.Write($"\r\nFirstChanceException: {e.Exception.Message}", level: LogLevel.Error);
                    Thread.Sleep(3000);
                    ForceExitNow(true);
                }
            };

            var trust = AppDomain.CurrentDomain.ApplicationTrust;
            if (!trust.IsApplicationTrustedToRun)
            {
                AppDomain.CurrentDomain.ApplicationTrust.IsApplicationTrustedToRun = true; // Ensure the application is trusted to run
            }
            if (_debugMode)
            {
                Console.WriteLine("IsApplicationTrustedToRun? " + trust.IsApplicationTrustedToRun);
                Console.WriteLine("Permissions granted: " + trust.DefaultGrantSet.PermissionSet.ToXml());
            }

            LogDomainAssemblies();
            Logger.Write($"You have {Environment.ProcessorCount} thread cores available", level: LogLevel.Init);
            if (firstRun)
                Logger.Write($"Additional settings —▷ {ConfigManager.FilePath}", level: LogLevel.Notice);
            #endregion

            #region ◁ Parse command line ▷
            // Test for no arguments.
            if (args.Length == 0 && !string.IsNullOrEmpty(lastCmd))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"[{LogLevel.Info}] LastCommand ▷ {lastCmd}");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"[{LogLevel.Notice}] No arguments were provided, would you like to re-use your last command? [Y/N] ");
                Console.ForegroundColor = ConsoleColor.Gray;
                if (Console.ReadKey(false).Key == ConsoleKey.Y)
                {
                    args = lastCmd.Split(' ');
                    args.ForEach(a => 
                    {
                        if (a.Equals($"{CommandLineHelper.preamble}debug", StringComparison.CurrentCultureIgnoreCase))
                            _debugMode = true;
                        if (a.Equals($"{CommandLineHelper.preamble}locate", StringComparison.CurrentCultureIgnoreCase))
                            _locateMode = true;
                    });
                }
                Console.WriteLine();
            }
            else // Test for basic flag modes.
            {
                Environment.GetCommandLineArgs().Skip(1).ToList().ForEach(arg =>
                {
                    if (arg.Equals($"{CommandLineHelper.preamble}debug", StringComparison.CurrentCultureIgnoreCase))
                    {
                        _debugMode = true;
                        Logger.Write("Debug mode enabled", level: LogLevel.Info);
                    }
                    if (arg.Equals($"{CommandLineHelper.preamble}locate", StringComparison.CurrentCultureIgnoreCase))
                    {
                        _locateMode = true;
                        Logger.Write("Locate mode enabled", level: LogLevel.Info);
                    }
                });
            }

            _terms = CommandLineHelper.GetTermValues(args);
            if (_terms.Count > 0 && !_locateMode)
            {
                _mtsMode = true;
                Logger.Write("MultiTermSearch mode enabled", level: LogLevel.Info);
                Logger.Write($"Using terms: {string.Join(", ", _terms)}", level: LogLevel.Info);
            }
            else if (_terms.Count > 0 && _locateMode)
            {
                Logger.Write("MultiTermSearch mode will be ignored because Locate mode was supplied", level: LogLevel.Warning);
                Logger.Write($"If you wish to use MultiTermSearch mode then remove the \"{CommandLineHelper.preamble}locate\" switch", level: LogLevel.Notice);
            }
            else if (_terms.Count == 0 && !_locateMode)
            {
                Logger.Write($"You must supply \"{CommandLineHelper.preamble}term\" values for multi-term search mode, or supply \"{CommandLineHelper.preamble}locate\" for file finding mode", level: LogLevel.Warning);
            }

            // Check for passed drive value
            string firstDrive = CommandLineHelper.GetFirstDriveValue(args);
            if (string.IsNullOrWhiteSpace(firstDrive))
            {
                Console.WriteLine($"[{LogLevel.Notice}] You can also supply a drive value. Use {CommandLineHelper.preamble}drive <value> to specify a drive term.");
                Logger.Write($"Using default drive value [{_driveText}]", level: LogLevel.Info);
            }
            else
            {
                _driveText = firstDrive;
                if (!_driveText.EndsWith("\\"))
                    _driveText = $"{_driveText}\\"; // Ensure trailing backslash
                Logger.Write($"Using drive term: {_driveText}", level: LogLevel.Info);
            }

            // Check for passed search pattern value
            string firstPattern = CommandLineHelper.GetFirstPatternValue(args);
            if (string.IsNullOrWhiteSpace(firstPattern))
            {
                Console.WriteLine($"[{LogLevel.Notice}] You can also supply a search pattern value. Use {CommandLineHelper.preamble}pattern <value> to specify a search pattern term.");
                Logger.Write($"Using default search pattern value [{_patternText}]", level: LogLevel.Info);
            }
            else
            {
                _patternText = firstPattern;
                Logger.Write($"Using pattern: {_patternText}", level: LogLevel.Info);
            }

            // Check for passed thread value
            string firstThread = CommandLineHelper.GetFirstThreadValue(args);
            if (string.IsNullOrWhiteSpace(firstThread))
            {
                Console.WriteLine($"[{LogLevel.Notice}] You can also supply a thread count value. Use {CommandLineHelper.preamble}threads <value> to specify the number of threads to use during a search.");
            }
            else
            {
                if (!int.TryParse(firstThread, out _numThreads))
                    Logger.Write($"Could not convert {firstThread} into a thread count.", level: LogLevel.Warning);
                else if (_numThreads < 1)
                    _numThreads = 1;
                else if (_numThreads > Environment.ProcessorCount)
                    Logger.Write($"Your processor count is {Environment.ProcessorCount}, it's recommended that you don't exceed {Environment.ProcessorCount}.", level: LogLevel.Warning);
            }

            if (_locateMode)
            {
                // Check for passed month value
                string firstMonth = CommandLineHelper.GetFirstMonthValue(args);
                if (string.IsNullOrWhiteSpace(firstMonth))
                {
                    Console.WriteLine($"[{LogLevel.Notice}] You can also supply a month count value. Use {CommandLineHelper.preamble}months <value> to specify the age to use during a search.");
                }
                else
                {
                    if (!int.TryParse(firstMonth, out _numMonths))
                        Logger.Write($"Could not convert {firstMonth} into a month count.", level: LogLevel.Warning);
                    else if (_numMonths < 0)
                        _numMonths = 0;
                }
            }

            if (!_locateMode)
            {
                // Check for passed % value
                string firstPercent = CommandLineHelper.GetFirstPercentValue(args);
                if (string.IsNullOrWhiteSpace(firstPercent))
                {
                    Console.WriteLine($"[{LogLevel.Notice}] You can also supply a percent count value. Use {CommandLineHelper.preamble}percent <value> to specify the percentage of a positive multi-term match.");
                }
                else
                {
                    if (!double.TryParse(firstPercent, out _mtsPercent))
                        Logger.Write($"Could not convert {firstPercent} into a percentage.", level: LogLevel.Warning);
                    else if (_mtsPercent > 1.0) // 100% is the maximum
                        _mtsPercent = 1.0;
                    else if (_mtsPercent < 0.0) // 10% is the minimum
                        _mtsPercent = 0.1;

                    Logger.Write($"Using percent: {_mtsPercent} ({_mtsPercent * 100}%)", level: LogLevel.Info);
                }
            }
            #endregion

            #region ◁ Basic validation ▷
            if (!Directory.Exists(_driveText))
            {
                Logger.Write($"Cannot find \"{_driveText}\". Try a different root folder.", level: LogLevel.Critical);
                Thread.Sleep(3000);
                return;
            }
            #endregion

            #region ◁ Search ▷
            var startTime = DateTime.Now;

            if (_locateMode) // file find mode
            {
                Logger.Write($"Searching with {_numThreads} threads ");
                Logger.Write($"You can press <Ctrl-C> to cancel the search at any time.  Any currently collected results will be displayed. ", level: LogLevel.Notice);
                var findit = new MultiThreadSearcher($"{_patternText}", maxThreads: _numThreads, numMonths: _numMonths, verbose: true);
                var fmatches = findit.Search($"{_driveText}", _cts.Token);
                if (fmatches.Count > 0)
                {
                    Console.WriteLine($"[{LogLevel.Info}] Analyzing {fmatches.Count} {(fmatches.Count == 1 ? "result" : "results")}…");
                    foreach (var file in fmatches)
                    {
                        _totalMatchCount++;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[{LogLevel.Match}]");
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  ▷ {file.Truncate(truncate)} ");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Logger.Write($"Found \"{file}\"", level: LogLevel.Match, console: false);
                    }
                }
                Logger.Write($"Total match count was {_totalMatchCount}", level: LogLevel.Notice);
                if (showStats)
                {
                    var metrics = findit.GetMetrics();
                    if (_debugMode)
                       Logger.Write($"Shortest traverse was {metrics.Select(o => o.Elapsed).Min().ToReadableTime(reportMilliseconds: true)}", level: LogLevel.Info);
                    Logger.Write($"Longest traverse was {metrics.Select(o => o.Elapsed).Max().ToReadableTime(reportMilliseconds: true)} (average was {metrics.Select(o => o.Elapsed).Average().ToReadableTime(reportMilliseconds: true)})", level: LogLevel.Info);
                }
            }
            else if (_mtsMode && _terms.Count > 0) // file parse mode
            {
                Logger.Write($"Searching with {_numThreads} threads ");
                Logger.Write($"You can press <Ctrl-C> to cancel the search at any time.  Any currently collected results will be displayed. ", level: LogLevel.Notice);
                var mtSearcher = new MultiTermSearcher($"{_patternText}", "", multiTermMatch: _terms, requiredMatchPercent: _mtsPercent, maxParallelism: _numThreads, verbose: true);
                var results = mtSearcher.Search($"{_driveText}", _cts.Token);
                Logger.Write($"{results.Count} files matched {_mtsPercent * 100}% {(_mtsPercent.IsOne() ? "" : "(or more)")} of given terms", level: LogLevel.Notice);
                foreach (var file in results)
                {
                    _totalMatchCount++;
                    Logger.Write($"{file}", "", LogLevel.Match, console: false);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[{LogLevel.Match}] {file.FilePath}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  ▷ Line #{file.LineNumber} ▷ {file.LineData.Truncate(truncate)} ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
                if (results.Count > 0)
                    Console.WriteLine($"[{LogLevel.Info}] Line details in the console will be truncated to {truncate} chars max.  Full output will be saved in the log file. ");
                if (showStats)
                {
                    var metrics = mtSearcher.GetMetrics();
                    if (_debugMode)
                        Logger.Write($"Shortest traverse was {metrics.Select(o => o.Elapsed).Min().ToReadableTime(reportMilliseconds: true)}", level: LogLevel.Info);
                    Logger.Write($"Longest traverse was {metrics.Select(o => o.Elapsed).Max().ToReadableTime(reportMilliseconds: true)} (average was {metrics.Select(o => o.Elapsed).Average().ToReadableTime(reportMilliseconds: true)})", level: LogLevel.Info);
                }
            }
            else if (_mtsMode && _terms.Count == 0)
            {
                Logger.Write($"You must specify at least one {CommandLineHelper.preamble}term argument", level: LogLevel.Warning);
            }
            #endregion

            #region ◁ Results ▷
            var elapsed = DateTime.Now - startTime;
            if (showStats)
            {
                Logger.Write($"Elapsed time during search was {elapsed.ToReadableTime()}");
                var process = Process.GetCurrentProcess();
                Logger.Write($"Total memory use was {((ulong)process.PrivateMemorySize64).ToFileSize()}");
                if (_debugMode)
                    Logger.Write($"Total working set was {((ulong)process.WorkingSet64).ToFileSize()}");
                Logger.Write($"Total processor use was {process.TotalProcessorTime.ToReadableTime()}");
                //Logger.Write($"MonitoringTotalProcessorTime was {AppDomain.CurrentDomain.MonitoringTotalProcessorTime.Divide(_numThreads).ToReadableTime()}");
                //Logger.Write($"UserProcessorTime {process.UserProcessorTime.ToReadableTime()}");
                //Logger.Write($"Survived memory size {((ulong)AppDomain.CurrentDomain.MonitoringSurvivedMemorySize).ToFileSize()}");
                //Logger.Write($"Privileged processor time {process.PrivilegedProcessorTime.ToReadableTime()}");
            }

            Console.WriteLine($"[{LogLevel.Notice}] Log file —▷ {Logger.GetLogName()}");
            Console.WriteLine($"[{LogLevel.Notice}] Process completed {(_noIssue ? "without issue" : "with issues")}");
            if (_noIssue)
            {
                // Only save settings if there were no problems.
                ConfigManager.Set(Keys.LastCount, value: _totalMatchCount);
                ConfigManager.Set(Keys.LastUse, value: DateTime.Now);
                ConfigManager.Set(Keys.TruncateLength, value: truncate);
                if (firstRun)
                {
                    // Set initial configuration values that may be missing.
                    ConfigManager.Set(Keys.FirstRun, value: false);
                    ConfigManager.Set(Keys.AppendLog, value: appendLog);
                    ConfigManager.Set(Keys.ShowStats, value: showStats);
                    if (Debugger.IsAttached)
                        ConfigManager.Set(Keys.LogLevel, LogLevel.Debug);
                    else
                        ConfigManager.Set(Keys.LogLevel, LogLevel.Info);
                }
                if (args.Length > 0)
                    ConfigManager.Set(Keys.LastCommand, string.Join(" ", args));
                Console.WriteLine();
                Thread.Sleep(3000);
            }
            else
            {
                Console.WriteLine($"[{LogLevel.Notice}] Press any key to exit");
                var key = Console.ReadKey(true).Key;
            }
            #endregion
        }

        #region ◁ Events ▷
        static void OnConfigError(object sender, Exception e)
        {
            Logger.Write($"[{LogLevel.Error}] ConfigManager error: {e.Message}");
        }
        #endregion

        #region ◁ Helpers ▷
        /// <summary>
        /// Gets the assemblies loaded in the current execution context of the application domain.
        /// </summary>
        static void LogDomainAssemblies()
        {
            try
            {
                Assembly[] assemblies = Thread.GetDomain().GetAssemblies();
                foreach (var assem in assemblies)
                {
                    var name = assem.GetName().FullName;
                    var pkt = assem.GetName().GetPublicKeyToken();
                    if (pkt != null && pkt.Length > 0) // ignore null/empty keys
                    {
                        Logger.Write($"Loaded assembly: {name}", level: LogLevel.Init);
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// This is an alternative way to set command line arguments as part of a dictionary.
        /// </summary>
        static void TestAlternateFlagSetting()
        {
            // Sample dictionary to hold app flags.
            var dict = new Dictionary<string, bool>
            {
                { "debug",   false }, { "locate",   false },
                { "term",    false }, { "drive",    false },
                { "match",   false }, { "pattern",  false },
                { "threads", false }, { "percent",  false }
            };

            Dictionary<string, bool> argResults = SetCommandArgs(dict);

            Console.WriteLine($"[{LogLevel.Debug}] Arguments: {string.Join(", ", argResults.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        }

        /// <summary>
        /// Modifies the provided dictionary with command line arguments that were matched.
        /// Calls <see cref="Environment.GetCommandLineArgs"/> to get the current command line arguments.
        /// </summary>
        /// <param name="kvps">original <see cref="Dictionary{TKey, TValue}"/></param>
        /// <param name="preamble">optional prefix to use for matching arguments</param>
        /// <returns>modified <see cref="Dictionary{TKey, TValue}"/></returns>
        /// <remarks>Arguments will be tested with and without <paramref name="preamble"/></remarks>
        static Dictionary<string, bool> SetCommandArgs(Dictionary<string, bool> kvps, string preamble = "--")
        {
            string[] envArgs = Environment.GetCommandLineArgs();
            
            // Clone original dictionary to avoid modifying it directly.
            var modified = new Dictionary<string, bool>(kvps);
            foreach (var env in kvps)
            {
                modified[env.Key] = envArgs.Where(arg => arg.ToLower().Contains($"{env.Key}") || arg.ToLower().Contains($"{preamble}{env.Key}")).Count() > 0;
            }
           
            return modified;
        }

        static void ForceExitNow(bool useEnvironment = false)
        {
            if (useEnvironment)
                Environment.Exit(0);
            else
                Process.GetCurrentProcess().Kill();
        }
        #endregion
    }

    #region ◁ Config ▷
    /// <summary>
    /// Simplify access to common config key names.
    /// </summary>
    internal struct Keys
    {
        public const string AppendLog        = "AppendLog";
        public const string FirstRun         = "FirstRun";
        public const string LastCommand      = "LastCommand";
        public const string LastCount        = "LastCount";
        public const string LastUse          = "LastUse";
        public const string LogLevel         = "LogLevel";
        public const string ShowStats        = "ShowStats";
        public const string TimeoutInMinutes = "TimeoutInMinutes";
        public const string TruncateLength   = "TruncateLength";
    }
    #endregion
}
