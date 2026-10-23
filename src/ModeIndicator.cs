using System;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShinelonModeIndicator
{
    internal enum ModeState { Unknown, On, Off, Error }

    internal sealed class IndicatorContext : ApplicationContext
    {
        private const string ExpectedGuid = "{ABBC0F72-8EA1-11d1-00A0-C90629100000}";
        private const string ExpectedInstance = @"ACPI\PNP0C14\1_0";
        private readonly NotifyIcon indicator = new NotifyIcon();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private ManagementEventWatcher watcher;
        private Icon currentIcon;
        private ModeState state = ModeState.Unknown;
        private DateTime nextFrequencyRead = DateTime.MinValue;
        private DateTime nextWatcherRetry = DateTime.MinValue;
        private bool eventObserved;
        private bool closing;
        private int pendingEvent;
        private int pendingFrequency;
        private int frequencyReadRunning;
        private int watcherStopped;

        public IndicatorContext()
        {
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem("退出", delegate { ExitThread(); }));
            indicator.ContextMenu = menu;
            SetState(ModeState.Unknown);
            indicator.Visible = true;

            timer.Interval = 500;
            timer.Tick += OnTick;
            timer.Start();
            StartWatcher();
            QueueFrequencyRead();
        }

        private void OnTick(object sender, EventArgs args)
        {
            if (Interlocked.Exchange(ref watcherStopped, 0) != 0)
            {
                StopWatcher();
                eventObserved = false; // A transition could have been missed.
                SetState(ModeState.Unknown);
            }

            int eventCode = Interlocked.Exchange(ref pendingEvent, 0);
            if (eventCode == 49 || eventCode == 50)
            {
                eventObserved = true;
                SetState(eventCode == 50 ? ModeState.On : ModeState.Off);
            }

            int frequency = Interlocked.Exchange(ref pendingFrequency, 0);
            if (!eventObserved && frequency != 0)
                SetState(frequency == 1 ? ModeState.On :
                    frequency == 2 ? ModeState.Off : ModeState.Error);

            if (watcher == null && DateTime.UtcNow >= nextWatcherRetry)
                StartWatcher();
            if (!eventObserved && DateTime.UtcNow >= nextFrequencyRead)
                QueueFrequencyRead();
        }

        private void QueueFrequencyRead()
        {
            nextFrequencyRead = DateTime.UtcNow.AddSeconds(5);
            if (Interlocked.CompareExchange(ref frequencyReadRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int result = 3;
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                        @"root\cimv2", "SELECT PercentofMaximumFrequency FROM " +
                        "Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'"))
                    using (ManagementObjectCollection rows = searcher.Get())
                    {
                        foreach (ManagementObject row in rows)
                        {
                            using (row)
                            {
                                double percent = Convert.ToDouble(row["PercentofMaximumFrequency"]);
                                if (percent < 0 || percent > 1000) break;
                                result = percent > 80.0 ? 1 : 2;
                                break;
                            }
                        }
                    }
                }
                catch (Exception) { /* No guess if the counter is unavailable. */ }
                if (!closing) Interlocked.Exchange(ref pendingFrequency, result);
                Interlocked.Exchange(ref frequencyReadRunning, 0);
            });
        }

        private void StartWatcher()
        {
            nextWatcherRetry = DateTime.UtcNow.AddSeconds(10);
            if (closing || watcher != null) return;
            try
            {
                using (ManagementClass cls = new ManagementClass(@"\\.\root\wmi:AcpiTest_EventULong"))
                {
                    cls.Get();
                    string guid = Convert.ToString(cls.Qualifiers["guid"].Value);
                    if (!String.Equals(guid, ExpectedGuid, StringComparison.OrdinalIgnoreCase)) return;
                }
                ManagementEventWatcher next = new ManagementEventWatcher(
                    @"\\.\root\wmi", "SELECT * FROM AcpiTest_EventULong");
                next.EventArrived += OnEvent;
                next.Stopped += OnStopped;
                try { next.Start(); }
                catch { next.Dispose(); throw; }
                watcher = next;
            }
            catch (Exception) { /* Frequency-based status remains available. */ }
        }

        private void OnEvent(object sender, EventArrivedEventArgs args)
        {
            try
            {
                string instance = Convert.ToString(args.NewEvent["InstanceName"]);
                if (!String.Equals(instance, ExpectedInstance, StringComparison.OrdinalIgnoreCase)) return;
                uint value = Convert.ToUInt32(args.NewEvent["ULong"]);
                if (value == 49 || value == 50)
                    Interlocked.Exchange(ref pendingEvent, (int)value);
            }
            catch (Exception) { /* Ignore malformed or unrelated notifications. */ }
        }

        private void OnStopped(object sender, StoppedEventArgs args)
        {
            if (!closing) Interlocked.Exchange(ref watcherStopped, 1);
        }

        private void StopWatcher()
        {
            ManagementEventWatcher old = watcher;
            watcher = null;
            if (old == null) return;
            try { old.Stop(); } catch (Exception) { }
            old.Dispose();
        }

        private void SetState(ModeState next)
        {
            if (state == next && currentIcon != null) return;
            state = next;
            Icon icon = CreateIcon(next);
            Icon previous = currentIcon;
            currentIcon = icon;
            indicator.Icon = icon;
            if (previous != null) previous.Dispose();
        }

        protected override void ExitThreadCore()
        {
            closing = true;
            timer.Stop();
            StopWatcher();
            indicator.Visible = false;
            indicator.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            timer.Dispose();
            base.ExitThreadCore();
        }

        private static Icon CreateIcon(ModeState mode)
        {
            Color color = mode == ModeState.On ? Color.FromArgb(38, 209, 91) :
                mode == ModeState.Off ? Color.FromArgb(130, 139, 151) :
                mode == ModeState.Error ? Color.FromArgb(235, 88, 66) :
                Color.FromArgb(245, 180, 50);
            using (Bitmap bitmap = new Bitmap(64, 64))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush fill = new SolidBrush(color))
            using (Pen border = new Pen(Color.FromArgb(25, 28, 32), 3))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(fill, 3, 3, 58, 58);
                graphics.DrawEllipse(border, 3, 3, 58, 58);
                if (mode == ModeState.On)
                {
                    Point[] bolt = { new Point(35, 9), new Point(19, 34), new Point(30, 34),
                        new Point(26, 55), new Point(45, 27), new Point(33, 27) };
                    graphics.FillPolygon(Brushes.White, bolt);
                }
                else if (mode == ModeState.Off)
                {
                    using (Pen line = new Pen(Color.White, 6))
                    {
                        line.StartCap = line.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        graphics.DrawLine(line, 20, 43, 44, 19);
                    }
                }
                else
                {
                    using (Font font = new Font("Arial", 34, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center })
                        graphics.DrawString(mode == ModeState.Error ? "!" : "?", font,
                            Brushes.White, new RectangleF(0, 1, 64, 59), format);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    internal static class Program
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "ShinelonModeIndicator";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--install")
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunPath))
                        key.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --startup",
                            RegistryValueKind.String);
                    return 0;
                }
                catch (Exception) { return 1; }
            }
            if (args.Length == 1 && args[0] == "--uninstall")
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunPath, true))
                        if (key != null) key.DeleteValue(RunName, false);
                    return 0;
                }
                catch (Exception) { return 1; }
            }
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--startup")) return 2;

            using (MutexGuard singleton = new MutexGuard())
            {
                if (!singleton.Acquired) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new IndicatorContext());
            }
            return 0;
        }

        private sealed class MutexGuard : IDisposable
        {
            private readonly Mutex mutex;
            public readonly bool Acquired;

            public MutexGuard()
            {
                mutex = new Mutex(true, @"Local\ShinelonModeIndicator", out Acquired);
            }

            public void Dispose()
            {
                if (Acquired) mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }
    }
}
