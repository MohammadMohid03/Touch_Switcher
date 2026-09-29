using System.IO;
using System.Threading;
using System.Windows.Forms;
using TouchSwitcher.Interop;

namespace TouchSwitcher;

internal static class Program
{
    private const string MutexName = @"Local\TouchSwitcher.SingleInstance";
    private const string ShowEventName = @"Local\TouchSwitcher.ShowEvent";
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TouchSwitcher", "crash.log");

    private static readonly object LogGate = new();

    internal static void Log(string msg)
    {
        try
        {
            lock (LogGate)
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        File.AppendAllText(LogPath, $"[{DateTime.Now}] {msg}{Environment.NewLine}");
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(25);
                    }
                }
            }
        }
        catch { }
    }

    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log($"UNHANDLED: {e.ExceptionObject}");

        Mutex? mutex = null;
        bool created = false;

        try
        {
            Log("Starting TouchSwitcher...");

            try
            {
                mutex = new Mutex(true, MutexName, out created);
            }
            catch (AbandonedMutexException)
            {
                // Previous instance was abruptly terminated; we acquire ownership safely
                created = true;
                Log("Acquired abandoned single-instance mutex.");
            }

            if (!created)
            {
                Log("Another instance is already running. Signaling it via EventWaitHandle...");
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existingEvent))
                    {
                        using (existingEvent)
                        {
                            existingEvent.Set();
                        }
                        Log("Signaled existing instance successfully via EventWaitHandle.");
                    }
                    else
                    {
                        Log("Could not open existing ShowEvent.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to signal via EventWaitHandle: {ex.Message}");
                }

                // Also send broadcast message as secondary fallback
                try
                {
                    uint msg = NativeMethods.RegisterWindowMessage("TouchSwitcher_ShowDiagnostics");
                    NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }

                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.ThreadException += (_, e) => Log($"THREAD_EX: {e.Exception}");

            using var controller = new AppController();
            var appContext = new ApplicationContext();

            EventWaitHandle? showEvent = null;
            RegisteredWaitHandle? waitHandle = null;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                waitHandle = ThreadPool.RegisterWaitForSingleObject(
                    showEvent,
                    (_, _) =>
                    {
                        Log("ShowEvent triggered -> Dispatching OpenDiagnostics via AppController.Dispatch");
                        controller.Dispatch(() =>
                        {
                            Log("Executing OpenDiagnostics on UI thread via Dispatch");
                            controller.Tray.OpenDiagnostics();
                        });
                    },
                    null,
                    -1,
                    false);
            }
            catch (Exception ex)
            {
                Log($"Failed to initialize ShowEvent wait handle: {ex.Message}");
            }

            try
            {
                // Show diagnostics window on initial start so user can verify
                Log("Program: Showing diagnostics window on startup...");
                controller.Tray.OpenDiagnostics();

                Log("Entering message loop...");
                Application.Run(appContext);
                Log("Message loop ended.");
            }
            finally
            {
                waitHandle?.Unregister(null);
                showEvent?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
        }
        finally
        {
            if (mutex != null)
            {
                try
                {
                    if (created)
                    {
                        mutex.ReleaseMutex();
                    }
                }
                catch { }
                mutex.Dispose();
            }
        }
    }
}
