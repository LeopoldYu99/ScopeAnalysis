using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace LCWpf
{
    internal static class AppLog
    {
        private static readonly object SyncRoot = new object();
        private static bool _initialized;
        private static string _logFilePath;

        internal static string LogFilePath
        {
            get { return _logFilePath ?? string.Empty; }
        }

        internal static void Initialize()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                string logDirectory = ResolveLogDirectory();
                Directory.CreateDirectory(logDirectory);

                int processId = Process.GetCurrentProcess().Id;
                string fileName = string.Format(
                    "LCWpf_{0:yyyyMMdd_HHmmss}_pid{1}.log",
                    DateTime.Now,
                    processId);

                _logFilePath = Path.Combine(logDirectory, fileName);
                _initialized = true;
            }

            Info("Log initialized. File=" + LogFilePath);
            WriteSessionHeader();
        }

        internal static void Info(string message)
        {
            Write("INFO", message, null);
        }

        internal static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        internal static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        internal static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        internal static void LogUnhandledException(string source, Exception exception, bool isTerminating)
        {
            Write(
                "FATAL",
                source + " triggered. IsTerminating=" + isTerminating,
                exception);
        }

        private static string ResolveLogDirectory()
        {
            string[] candidates =
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LCWpf",
                    "Logs"),
                Path.Combine(Path.GetTempPath(), "LCWpf", "Logs")
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
                catch
                {
                }
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static void WriteSessionHeader()
        {
            Assembly entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = entryAssembly.GetName();
            Version version = assemblyName.Version;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("=== Session start ===");
            builder.AppendLine("UtcNow: " + DateTime.UtcNow.ToString("o"));
            builder.AppendLine("ProcessId: " + Process.GetCurrentProcess().Id);
            builder.AppendLine("ProcessName: " + Process.GetCurrentProcess().ProcessName);
            builder.AppendLine("Executable: " + entryAssembly.Location);
            builder.AppendLine("AppVersion: " + (version == null ? "<unknown>" : version.ToString()));
            builder.AppendLine("MachineName: " + Environment.MachineName);
            builder.AppendLine("UserName: " + Environment.UserName);
            builder.AppendLine("OSVersion: " + Environment.OSVersion);
            builder.AppendLine("Is64BitProcess: " + Environment.Is64BitProcess);
            builder.AppendLine("CLRVersion: " + Environment.Version);
            builder.AppendLine("CurrentDirectory: " + Environment.CurrentDirectory);
            builder.AppendLine("=====================");

            WriteRaw(builder.ToString());
        }

        private static void Write(string level, string message, Exception exception)
        {
            if (_initialized == false)
            {
                try
                {
                    Initialize();
                }
                catch
                {
                    return;
                }
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" [");
            builder.Append(level);
            builder.Append("] ");
            builder.Append(message ?? string.Empty);
            builder.Append(" | Thread=");
            builder.Append(Thread.CurrentThread.ManagedThreadId);

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            builder.AppendLine();
            WriteRaw(builder.ToString());
        }

        private static void WriteRaw(string text)
        {
            lock (SyncRoot)
            {
                if (string.IsNullOrWhiteSpace(_logFilePath))
                {
                    return;
                }

                try
                {
                    File.AppendAllText(_logFilePath, text, Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
