using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace csfind
{
    /// <summary>
    /// Contains Task extensions.
    /// </summary>
    internal static class TaskHelper
    {
        internal static async void FireAndForget(this Task task, Action<Exception> onException = null, bool continueOnCapturedContext = false)
        {
            try
            {
                await task.ConfigureAwait(continueOnCapturedContext);
            }
            catch (Exception ex) when (onException != null)
            {
                onException.Invoke(ex);
            }
            catch (Exception ex) when (onException == null)
            {
                Debug.WriteLine($"[ERROR] FireAndForget: {ex.Message}");
            }
        }

        /// <summary>
        /// Task.Factory.StartNew (() => { throw null; }).IgnoreExceptions();
        /// </summary>
        internal static void IgnoreExceptions(this Task task, bool logException = false)
        {
            task.ContinueWith(t =>
            {
                AggregateException ignore = t.Exception;
                ignore?.Flatten().Handle(ex =>
                {
                    if (logException)
                        Logger.Write($"Exception type: {ex.GetType()}\r\nException Message: {ex.Message}", level: LogLevel.Error);
                    return true; // don't re-throw
                });
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Timeout add-on for <see cref="Task"/>.
        /// var result = await SomeLongAsyncFunction().WithTimeout(TimeSpan.FromSeconds(2));
        /// </summary>
        /// <typeparam name="TResult">the type of task result</typeparam>
        /// <returns><see cref="Task"/>TResult</returns>
        internal async static Task<TResult> WithTimeout<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(timeout));

            if (winner != task)
                throw new TimeoutException();

            return await task; // Unwrap result/re-throw
        }

        /// <summary>
        /// Timeout add-on for <see cref="Task"/>.
        /// </summary>
        /// <returns>The <see cref="Task"/> with timeout.</returns>
        /// <param name="task">Task.</param>
        /// <param name="timeoutInMilliseconds">Timeout duration in Milliseconds.</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        internal async static Task<T> WithTimeout<T>(this Task<T> task, int timeoutInMilliseconds)
        {
            var retTask = await Task.WhenAny(task, Task.Delay(timeoutInMilliseconds)).ConfigureAwait(false);

            #pragma warning disable CS8603 // Possible null reference return.
            return retTask is Task<T> ? task.Result : default;
            #pragma warning restore CS8603 // Possible null reference return.
        }

        /// <summary>
        /// <see cref="CancellationToken"/> add-on for <see cref="Task"/>.
        /// var result = await SomeLongAsyncFunction().WithCancellation(cts.Token);
        /// </summary>
        /// <typeparam name="TResult">the type of task result</typeparam>
        /// <returns><see cref="Task"/>TResult</returns>
        internal static Task<TResult> WithCancellation<TResult>(this Task<TResult> task, CancellationToken cancelToken)
        {
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            CancellationTokenRegistration reg = cancelToken.Register(() => tcs.TrySetCanceled());
            task.ContinueWith(ant =>
            {
                reg.Dispose(); // NOTE: it's important to dispose of CancellationTokenRegistrations or they will hang around in memory until the application closes
                if (ant.IsCanceled)
                    tcs.TrySetCanceled();
                else if (ant.IsFaulted)
                    tcs.TrySetException(ant.Exception.InnerException);
                else
                    tcs.TrySetResult(ant.Result);
            });
            return tcs.Task;  // Return the TaskCompletionSource result
        }

        internal static Task<T> WithAllExceptions<T>(this Task<T> task)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

            task.ContinueWith(ignored =>
            {
                switch (task.Status)
                {
                    case TaskStatus.Canceled:
                        Debug.WriteLine($"[TaskStatus.Canceled]");
                        tcs.SetCanceled();
                        break;
                    case TaskStatus.RanToCompletion:
                        tcs.SetResult(task.Result);
                        Debug.WriteLine($"[TaskStatus.RanToCompletion({task.Result})]");
                        break;
                    case TaskStatus.Faulted:
                        // SetException will automatically wrap the original AggregateException
                        // in another one. The new wrapper will be removed in TaskAwaiter, leaving
                        // the original intact.
                        Debug.WriteLine($"[TaskStatus.Faulted]: {task.Exception.Message}");
                        tcs.SetException(task.Exception);
                        break;
                    default:
                        Debug.WriteLine($"[TaskStatus: Continuation called illegally.]");
                        tcs.SetException(new InvalidOperationException("Continuation called illegally."));
                        break;
                }
            });

            return tcs.Task;
        }

        internal static async Task Throttle(this IEnumerable<Func<Task>> toRun, int throttleTo = 4)
        {
            var running = new List<Task>(throttleTo);
            foreach (var taskToRun in toRun)
            {
                running.Add(taskToRun());
                if (running.Count == throttleTo)
                {
                    var comTask = await Task.WhenAny(running);
                    running.Remove(comTask);
                }
            }
        }

        internal static async Task<IEnumerable<T>> Throttle<T>(IEnumerable<Func<Task<T>>> toRun, int throttleTo = 4)
        {
            var running = new List<Task<T>>(throttleTo);
            var completed = new List<Task<T>>(toRun.Count());
            foreach (var taskToRun in toRun)
            {
                running.Add(taskToRun());
                if (running.Count == throttleTo)
                {
                    var comTask = await Task.WhenAny(running);
                    running.Remove(comTask);
                    completed.Add(comTask);
                }
            }
            return completed.Select(t => t.Result);
        }
    }
}
