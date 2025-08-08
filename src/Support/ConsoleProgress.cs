using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace csfind.Support
{
    static class ConsoleProgress
    {
        static char[] progChars = new char[] { '─', '\\', '|', '/' };
        
        internal static void Draw(int idx, long transferred, long total, int transferStarted, int width)
        {
            Console.Write("\r");
            Console.Write(progChars[idx % progChars.Length]);
            int fillPos = (int)((float)transferred / (float)total * width);
            string filled = new string('■', fillPos);
            string empty = new string('…', width - fillPos);
            Console.Write(" [" + filled + empty + "] ");
            int seconds = (Environment.TickCount - transferStarted) / 1000;
            if (seconds == 0)
                return;
        }

        internal static void Test()
        {
            Random rnd = new Random();
            ThreadPool.QueueUserWorkItem(_ => 
            {
                Console.WriteLine();
                int init = Environment.TickCount;
                for (int idx = 0; idx < 101; idx++)
                {
                    ConsoleProgress.Draw(idx++, idx, 100, init, Console.WindowWidth / 2);
                    Thread.Sleep(rnd.Next(50, 300));
                }
            });
        }
    }
}
