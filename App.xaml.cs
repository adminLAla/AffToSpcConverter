using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AffToSpcConverter
{
    public partial class App : Application
    {
        private static readonly object RuntimeLogLock = new();
        private static string RuntimeLogPath => Path.Combine(AppContext.BaseDirectory, "runtime-errors.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            // 统一记录软件运行期间的异常信息到 exe 同目录。
            HookGlobalExceptionLogging();
            base.OnStartup(e);
        }

        // 供业务层在 catch 中主动记录异常，避免只依赖全局未处理异常事件。
        public static void LogHandledException(string context, Exception ex)
        {
            WriteExceptionLog("HandledException", context, ex);
        }

        private void HookGlobalExceptionLogging()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteExceptionLog("DispatcherUnhandledException", "UI线程未处理异常", e.Exception);
        }

        private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception
                     ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception object.");
            WriteExceptionLog(
                "AppDomainUnhandledException",
                $"非UI线程未处理异常（IsTerminating={e.IsTerminating}）",
                ex);
        }

        private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteExceptionLog("TaskSchedulerUnobservedTaskException", "未观察到的 Task 异常", e.Exception);
        }

        private static void WriteExceptionLog(string category, string context, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(new string('=', 96));
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Category: {category}");
                sb.AppendLine($"Context: {context}");
                sb.AppendLine($"ProcessId: {Environment.ProcessId}");
                sb.AppendLine($"ExeDir: {AppContext.BaseDirectory}");
                sb.AppendLine("Exception:");
                AppendException(sb, ex, 0);
                sb.AppendLine();

                lock (RuntimeLogLock)
                {
                    File.AppendAllText(RuntimeLogPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不再抛出，避免影响主流程。
            }
        }

        private static void AppendException(StringBuilder sb, Exception ex, int depth)
        {
            string indent = new(' ', depth * 2);
            sb.AppendLine($"{indent}{ex.GetType().FullName}: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                foreach (string line in ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    sb.AppendLine($"{indent}{line}");
            }

            if (ex.InnerException != null)
            {
                sb.AppendLine($"{indent}InnerException:");
                AppendException(sb, ex.InnerException, depth + 1);
            }
        }
    }
}
