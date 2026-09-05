using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FloatingClock
{
    // Runs in a separate STA process. No user settings, startup entries or cursor positions are changed.
    internal static class RegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static int Run()
        {
            List<string> report = new List<string>();
            try
            {
                Check(report, "Native hide / redraw / show", NativeVisibility);
                Check(report, "Capture loss and drag cancellation", CaptureLoss);
                Check(report, "DPI raster size, physical position and alpha buffer reuse", Rendering);
                Check(report, "Drag position survives settings round trip", DragPosition);
                Check(report, "WPF hide / show, timer lifecycle and deferred frame", WindowLifecycle);
                Check(report, "No-date layout centering", NoDateLayout);
                Check(report, "Second / minute scheduling and midnight", Scheduling);
                Check(report, "Startup preference persistence and failure", StartupPreference);
                Check(report, "Legacy position migration and monitor gaps", Geometry);
                Check(report, "Shared menu actions, selection and disabled states", MenuState);
                report.Add("PASS: 10 regression groups");
                return 0;
            }
            catch (Exception e)
            {
                report.Add("FAIL: " + e);
                return 50;
            }
            finally
            {
                try { File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.log"), report.ToArray()); }
                catch { }
            }
        }

        private static void Check(List<string> report, string name, Action test)
        {
            report.Add("RUN: " + name);
            test();
            report.Add("PASS: " + name);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static object Invoke(object instance, string name, params object[] args)
        {
            return instance.GetType().GetMethod(name, Private).Invoke(instance, args);
        }

        private static object Field(object instance, string name)
        {
            return instance.GetType().GetField(name, Private).GetValue(instance);
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetField(name, Private).SetValue(instance, value);
        }

        private static void Pump()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }

        private static LayeredSurface NewSurface(string suffix)
        {
            LayeredSurface surface = new LayeredSurface();
            surface.Create(false, "FloatingClock.Test." + suffix, -30000, -30000);
            Assert(surface.Handle != IntPtr.Zero, "Native window creation failed");
            return surface;
        }

        private static void NativeVisibility()
        {
            using (LayeredSurface surface = NewSurface("Visibility"))
            {
                for (int i = 0; i < 3; i++)
                {
                    Assert(surface.PresentSolid(32, 24, -30000, -30000, 120, 16, 28, 34), "Solid presentation failed");
                    surface.SetVisible(true);
                    Assert(NativeMethods.IsWindowVisible(surface.Handle), "Shown surface is invisible");
                    surface.SetVisible(false);
                    Assert(!NativeMethods.IsWindowVisible(surface.Handle), "Hidden surface remains visible");
                    Assert(surface.PresentSolid(32, 24, -30000, -30000, 120, 16, 28, 34), "Hidden presentation failed");
                    Assert(!NativeMethods.IsWindowVisible(surface.Handle), "Rendering unexpectedly showed a hidden window");
                    surface.SetVisible(true);
                    Assert(NativeMethods.IsWindowVisible(surface.Handle), "Show after hidden redraw failed");
                }
            }
        }

        private static void CaptureLoss()
        {
            using (LayeredSurface surface = NewSurface("Capture"))
            {
                int finished = 0;
                surface.MoveFinished = delegate { finished++; };
                uint[] cancellations = { 0x0215, 0x001F, 0x0200 };
                foreach (uint message in cancellations)
                {
                    Set(surface, "dragging", true);
                    int before = finished;
                    SendMessage(surface.Handle, message, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(surface.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                    Assert(!surface.IsDragging && finished == before + 1, "Drag cancellation must finish exactly once");
                }
                Set(surface, "dragging", true);
                surface.Locked = true;
                Assert(!surface.IsDragging, "Lock left a drag active");
                Set(surface, "dragging", true);
                surface.SetClickThrough(true);
                Assert(!surface.IsDragging, "Click-through left a drag active");
            }
        }

        private static void Rendering()
        {
            using (LayeredSurface surface = NewSurface("Rendering"))
            {
                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(128, 20, 40, 60)), null, new Rect(0, 0, 100, 30));
                }
                int[] dpiValues = { 96, 144, 192, 96 };
                foreach (int dpi in dpiValues)
                {
                    SendMessage(surface.Handle, 0x02E0, new IntPtr(dpi | (dpi << 16)), IntPtr.Zero);
                    Assert(surface.Present(visual, 100, 30, -30000, -29900), "Raster presentation failed");
                    NativeRect bounds;
                    GetWindowRect(surface.Handle, out bounds);
                    Assert(bounds.Left == -30000 && bounds.Top == -29900, "DPI scaling changed physical position");
                    Assert(bounds.Right - bounds.Left == (int)Math.Round(100 * dpi / 96.0), "Incorrect DPI raster width");
                    Assert(bounds.Bottom - bounds.Top == (int)Math.Round(30 * dpi / 96.0), "Incorrect DPI raster height");
                    object bitmap = Field(surface, "renderBitmap");
                    IntPtr buffer = (IntPtr)Field(surface, "bits");
                    for (int frame = 0; frame < 4; frame++)
                    {
                        Assert(surface.Present(visual, 100, 30, -30000, -29900), "Repeated presentation failed");
                        Assert(object.ReferenceEquals(bitmap, Field(surface, "renderBitmap")), "Bitmap was not reused");
                        Assert(buffer == (IntPtr)Field(surface, "bits"), "DIB buffer was not reused");
                        Assert(Math.Abs(Marshal.ReadByte(buffer, 3) - 128) <= 1, "Alpha accumulated across frames");
                    }
                }
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 100, 30));
                }
                Assert(surface.Present(visual, 100, 30, -30000, -29900), "Cleared presentation failed");
                Assert(Marshal.ReadByte((IntPtr)Field(surface, "bits"), 3) == 0, "Old pixels survived bitmap reuse");
            }
        }

        private static void DragPosition()
        {
            ClockSettings settings = ClockSettings.CreateDefault();
            ClockSettings restored;
            using (WindowFixture fixture = new WindowFixture(settings))
            {
                Invoke(fixture.Window, "HandleSurfaceMoveFinished");
                Assert(settings.DockAnchor == 2, "A click unexpectedly removed docking");
                Rect area = DisplayGeometry.WorkAreaAt(0, 0);
                Invoke(fixture.Window, "HandleSurfaceMoved", area.Left + 60.0, area.Top + 60.0);
                Invoke(fixture.Window, "HandleSurfaceMoveFinished");
                Assert(settings.DockAnchor == 0 && fixture.Saves > 0, "Dragging did not persist free position");
                DataContractSerializer serializer = new DataContractSerializer(typeof(ClockSettings));
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, settings);
                    stream.Position = 0;
                    restored = (ClockSettings)serializer.ReadObject(stream);
                    restored.Normalize();
                }
            }
            double left = restored.Left, top = restored.Top;
            using (WindowFixture fixture = new WindowFixture(restored))
            {
                Assert((double)Field(fixture.Window, "surfaceLeft") == left
                    && (double)Field(fixture.Window, "surfaceTop") == top, "Restart lost the dragged position");
            }
        }

        private static void WindowLifecycle()
        {
            using (WindowFixture fixture = new WindowFixture(ClockSettings.CreateDefault()))
            {
                ClockWindow window = fixture.Window;
                DispatcherTimer timer = (DispatcherTimer)Field(window, "clockTimer");
                Assert(!timer.IsEnabled, "Unshown clock timer was running");
                fixture.ShowOffscreen();
                LayeredSurface surface = (LayeredSurface)Field(window, "layeredSurface");
                Assert(NativeMethods.IsWindowVisible(surface.Handle) && timer.IsEnabled, "Initial show did not render or start timer");
                window.Hide();
                Pump();
                Assert(!NativeMethods.IsWindowVisible(surface.Handle) && !timer.IsEnabled, "Hide failed to hide surface or stop timer");
                window.RefreshCurrentTime();
                Assert(!timer.IsEnabled, "Hidden clock resumed timer on time-change notification");
                window.Show();
                Pump();
                Assert(NativeMethods.IsWindowVisible(surface.Handle) && timer.IsEnabled, "Restore did not show clock or restart timer");

                DateTime previous = DateTime.Now.AddMinutes(-2);
                Invoke(window, "UpdateClock", previous, true);
                Invoke(window, "PresentSurface");
                string oldFace = (string)Field(window, "lastPresentedFace");
                Set(surface, "dragging", true);
                Invoke(window, "UpdateClock", previous.AddMinutes(1), false);
                Assert((bool)Invoke(window, "ClockFaceChanged"), "Minute transition was not detected");
                Invoke(window, "RequestPresent");
                Assert((string)Field(window, "lastPresentedFace") == oldFace, "Skipped frame was recorded as displayed");
                SendMessage(surface.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                Pump();
                Assert((string)Field(window, "lastPresentedFace") != oldFace
                    && !(bool)Invoke(window, "ClockFaceChanged"), "Drag release did not flush current time");
                window.ToggleShowSeconds();
                Assert(timer.Interval.TotalMilliseconds <= 1010, "Enabling seconds did not reschedule timer");
                window.ToggleShowSeconds();
                Assert(timer.Interval.TotalMilliseconds <= 60010, "Minute schedule exceeds boundary");
            }
        }

        private static void NoDateLayout()
        {
            for (int font = 0; font < ClockLooks.FontNames.Length; font++)
            {
                for (int format = 0; format < 3; format++)
                {
                    ClockSettings settings = ClockSettings.CreateDefault();
                    settings.ShowDate = false;
                    settings.FontMode = font;
                    settings.ShowSeconds = format != 0;
                    settings.Use24Hour = format != 2;
                    using (WindowFixture fixture = new WindowFixture(settings))
                    {
                        Viewbox scaler = (Viewbox)Field(fixture.Window, "scaler");
                        DateTime[] times = {
                            new DateTime(2026, 9, 5, 0, 0, 0), new DateTime(2026, 9, 5, 11, 11, 11),
                            new DateTime(2026, 9, 5, 20, 2, 28), new DateTime(2026, 9, 5, 23, 59, 59)
                        };
                        foreach (DateTime sample in times)
                        {
                            Invoke(fixture.Window, "UpdateClock", sample, true);
                            scaler.Measure(new Size(fixture.Window.Width, fixture.Window.Height));
                            scaler.Arrange(new Rect(0, 0, fixture.Window.Width, fixture.Window.Height));
                            scaler.UpdateLayout();
                            TextBlock time = (TextBlock)Field(fixture.Window, "timeText");
                            GeneralTransform transform = time.TransformToAncestor(scaler);
                            Point center = transform.Transform(new Point(time.ActualWidth / 2, 0));
                            Assert(Math.Abs(center.X - fixture.Window.Width / 2) <= 1.0, "No-date text is off center for font " + font + ": " + center.X);
                            Rect textBounds = transform.TransformBounds(new Rect(0, 0, time.ActualWidth, time.ActualHeight));
                            Assert(textBounds.Left >= ClockLayout.NoDatePadding - 1
                                && textBounds.Right <= fixture.Window.Width - ClockLayout.NoDatePadding + 1, "Wide clock font overflowed its time column");
                        }
                    }
                }
            }
        }

        private static void Scheduling()
        {
            DateTime time = new DateTime(2026, 9, 5, 12, 30, 9, 250);
            Assert(ClockSchedule.NextTick(time, true).TotalMilliseconds == 760, "Second boundary is wrong");
            Assert(ClockSchedule.NextTick(time, false).TotalMilliseconds == 50760, "Minute boundary is wrong");
            time = new DateTime(2026, 12, 31, 23, 59, 59, 995);
            Assert(ClockSchedule.NextTick(time, false).TotalMilliseconds == 15, "Midnight boundary is wrong");
        }

        private static void StartupPreference()
        {
            ClockSettings settings = ClockSettings.CreateDefault();
            bool shortcut = true, saved = false;
            StartupManager.ApplyPreference(settings, false, delegate(bool enabled) { shortcut = enabled; },
                delegate { saved = !settings.StartWithWindows && !shortcut; });
            Assert(saved, "Disabling startup was not immediately persisted after deleting shortcut");
            saved = false;
            try
            {
                StartupManager.ApplyPreference(settings, true, delegate { throw new IOException("Simulated shortcut failure"); }, delegate { saved = true; });
                throw new InvalidOperationException("Shortcut failure was ignored");
            }
            catch (IOException) { }
            Assert(!settings.StartWithWindows && !saved, "Failed shortcut operation changed the preference");

            string prefix = "<FloatingClockSettings xmlns=\"http://schemas.datacontract.org/2004/07/FloatingClock\"><Version>9</Version>";
            string[] startupMembers = { "", "<StartWithWindows>false</StartWithWindows>" };
            for (int i = 0; i < startupMembers.Length; i++)
            {
                using (MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(prefix + startupMembers[i] + "</FloatingClockSettings>")))
                {
                    ClockSettings legacy = (ClockSettings)new DataContractSerializer(typeof(ClockSettings)).ReadObject(stream);
                    legacy.Normalize();
                    Assert(legacy.StartWithWindows == (i == 0), "Legacy startup default or explicit opt-out was lost");
                }
            }
        }

        private static void Geometry()
        {
            ClockSettings settings = ClockSettings.CreateDefault();
            settings.PositionInPixels = false;
            settings.Left = -800;
            settings.Top = 200;
            settings.MigratePosition(1.5);
            settings.MigratePosition(1.5);
            Assert(settings.Left == -1200 && settings.Top == 300, "Legacy coordinates were not migrated exactly once");
            Rect[] areas = { new Rect(0, 0, 1920, 1040), new Rect(-1280, 400, 1280, 960), new Rect(1920, 900, 1280, 960) };
            Rect[] windows = { new Rect(2000, 200, 200, 68), new Rect(-1000, 100, 200, 68), new Rect(6000, 100, 200, 68) };
            foreach (Rect window in windows)
            {
                Point point = DisplayGeometry.Clamp(window, areas);
                Rect clamped = new Rect(point, window.Size);
                bool contained = false;
                foreach (Rect area in areas) contained |= area.Contains(clamped);
                Assert(contained, "Clock remained in a monitor gap or outside all screens");
            }
            Point unchanged = DisplayGeometry.Clamp(new Rect(-1000, 500, 200, 68), areas);
            Assert(unchanged.X == -1000 && unchanged.Y == 500, "Valid negative monitor coordinates changed");
        }

        private static void MenuState()
        {
            ClockSettings settings = ClockSettings.CreateDefault();
            using (WindowFixture fixture = new WindowFixture(settings))
            {
                Forms.ContextMenuStrip menu = fixture.Window.SettingsMenu;
                Forms.ToolStripMenuItem seconds = (Forms.ToolStripMenuItem)menu.Items.Find("seconds", true)[0];
                seconds.PerformClick();
                Assert(settings.ShowSeconds && seconds.Checked, "Menu seconds action and check state diverged");
                Forms.ToolStripMenuItem ink = (Forms.ToolStripMenuItem)menu.Items.Find("ink.4", true)[0];
                ink.PerformClick();
                Assert(settings.ThemeMode == 4 && ink.Checked
                    && !((Forms.ToolStripMenuItem)menu.Items.Find("ink.0", true)[0]).Checked, "Color selection was not exclusive");
                Assert(((Forms.ToolStripMenuItem)menu.Items.Find("ink", true)[0]).ShortcutKeyDisplayString == ClockLooks.InkNames[4], "Color summary was stale");
                ((Forms.ToolStripMenuItem)menu.Items.Find("surface.13", true)[0]).PerformClick();
                Assert(!menu.Items.Find("opacity", true)[0].Enabled, "Opaque surface allowed ineffective opacity edits");
                ((Forms.ToolStripMenuItem)menu.Items.Find("surface.0", true)[0]).PerformClick();
                Assert(menu.Items.Find("opacity", true)[0].Enabled, "Transparent surface did not restore opacity editing");
                Forms.ToolStripMenuItem font = (Forms.ToolStripMenuItem)menu.Items.Find("font.5", true)[0];
                font.PerformClick();
                Assert(settings.FontMode == 5 && font.Checked && fixture.Saves >= 5, "Font selection was not applied and persisted");
            }
        }

        private sealed class WindowFixture : IDisposable
        {
            public readonly ClockWindow Window;
            public int Saves;

            public WindowFixture(ClockSettings settings)
            {
                Window = new ClockWindow(settings, delegate { Saves++; }, delegate(bool b) { },
                    delegate { return false; }, delegate(bool b) { }, delegate { }, delegate { });
            }

            public void ShowOffscreen()
            {
                Window.Show();
                Set(Window, "surfaceLeft", -30000.0);
                Set(Window, "surfaceTop", -30000.0);
                Pump();
            }

            public void Dispose()
            {
                Window.PrepareForExit();
                Window.Close();
                Pump();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    }
}
