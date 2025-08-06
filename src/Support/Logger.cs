using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace csfind
{
    public enum LogLevel { Debug, Init, Info, Warning, Error, Critical, Match, Notice }

    public static class Logger
    {
        static string _path = string.Empty;

        internal static bool Write(string message, string path = "", LogLevel level = LogLevel.Info, bool console = true)
        {
            try
            {
                //var fn = System.IO.Path.GetFileNameWithoutExtension(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);

                if (string.IsNullOrEmpty(path))
                    _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results.log");
                else
                    _path = path;

                #region [console output]
                if (console)
                {
                    ConsoleColor previous = Console.ForegroundColor;
                    switch (level)
                    {
                        case LogLevel.Init:
                        case LogLevel.Debug: Console.ForegroundColor = ConsoleColor.DarkGray; break;
                        case LogLevel.Notice: Console.ForegroundColor = ConsoleColor.Cyan; break;
                        case LogLevel.Match: Console.ForegroundColor = ConsoleColor.Green; break;
                        case LogLevel.Warning: Console.ForegroundColor = ConsoleColor.DarkYellow; break;
                        case LogLevel.Error: Console.ForegroundColor = ConsoleColor.DarkRed; break;
                        case LogLevel.Critical: Console.ForegroundColor = ConsoleColor.Red; break;
                        case LogLevel.Info: Console.ForegroundColor = ConsoleColor.Gray; break;
                    }
                    Console.WriteLine($"[{level}] {message}");
                    Console.ForegroundColor = previous;
                }
                #endregion

                using (StreamWriter writer = new StreamWriter(path: _path, append: true, encoding: Encoding.UTF8))
                {
                    writer.WriteLine($"[{ DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss.fff tt")}]\t{level}\t{message}");
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string GetLogName()
        {
            if (string.IsNullOrEmpty(_path))
                return "Path has not been set.";
            else
                return _path;
        }

        /// <summary>
        /// Logs a message, including the source of the caller.
        /// </summary>
        /// <param name="message">text data</param>
        /// <param name="origin">callers member/function name</param>
        /// <param name="filePath">source code file path</param>
        /// <param name="lineNumber">line number in the code file of the caller</param>
        /// <param name="values">additional object arguments (optional)</param>
        internal static void WriteSourceInclude(string message, 
            LogLevel logLevel = LogLevel.Info,
            [CallerMemberName] string origin = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            params object[] values)
        {

            string optional = string.Empty;
            if (values != null && values.Length > 0)
            {
                var sb = new StringBuilder();
                sb.AppendJoin(",", values);
                optional = $" [OPTIONAL ▷ {sb}]";
            }
            //var pre = args.Prepend(origin, filePath, lineNumber);
            Write($"[MESSAGE ▷ {message}] [ORIGIN ▷ {origin}] [FILE ▷ {filePath}] [LINE ▷ {lineNumber}]{optional}", "", logLevel);
        }
    }
}
