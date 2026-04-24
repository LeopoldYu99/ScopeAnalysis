using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ScopeAnalysis
{
    public partial class App : Application
    {
        public App()
        {
            AppLog.Initialize();
            RegisterGlobalExceptionHandlers();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLog.Info("Application startup.");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppLog.Info("Application exit. ExitCode=" + e.ApplicationExitCode);
            base.OnExit(e);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            SessionEnding += OnSessionEnding;

            try
            {
                System.Windows.Forms.Application.SetUnhandledExceptionMode(
                    System.Windows.Forms.UnhandledExceptionMode.CatchException);
                System.Windows.Forms.Application.ThreadException += OnWindowsFormsThreadException;
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to register Windows Forms exception handler.", ex);
            }

            AppLog.Info("Registered global exception handlers.");
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLog.LogUnhandledException("Application.DispatcherUnhandledException", e.Exception, false);
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            if (exception != null)
            {
                AppLog.LogUnhandledException(
                    "AppDomain.CurrentDomain.UnhandledException",
                    exception,
                    e.IsTerminating);
                return;
            }

            AppLog.Error(
                "AppDomain.CurrentDomain.UnhandledException received a non-Exception object. "
                + "Type=" + (e.ExceptionObject == null ? "<null>" : e.ExceptionObject.GetType().FullName)
                + ", IsTerminating=" + e.IsTerminating);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLog.LogUnhandledException("TaskScheduler.UnobservedTaskException", e.Exception, false);
            e.SetObserved();
            AppLog.Warn("Marked TaskScheduler.UnobservedTaskException as observed.");
        }

        private void OnWindowsFormsThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            AppLog.LogUnhandledException("System.Windows.Forms.Application.ThreadException", e.Exception, false);
        }

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            AppLog.Info("Windows session ending. Reason=" + e.ReasonSessionEnding);
        }
    }
}

