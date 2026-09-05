using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace FloatingClock
{
    // One menu instance serves the native clock and the tray, including keyboard state.
    internal sealed class ClockMenu : IDisposable
    {
        private readonly ClockSettings settings;
        private readonly ClockWindow window;
        private readonly Func<bool> getStartup;
        private readonly Font uiFont = new Font("Microsoft YaHei UI", 9.5F);
        private readonly MenuFonts fonts = new MenuFonts();
        private readonly List<Bitmap> images = new List<Bitmap>();
        private readonly Forms.Timer previewTimer;
        private readonly ClockMenuHeader header;
        private readonly Forms.ToolStripMenuItem format, ink, surface, font, size, opacity, interaction;
        private readonly Forms.ToolStripMenuItem date, seconds, hour24, topmost, locked, through, startup, visibility;
        private readonly Forms.ToolStripMenuItem[] inkOptions, surfaceOptions, fontOptions, sizeOptions, opacityOptions;
        private bool keepOpen;
        private bool disposed;
        private IntPtr returnFocus;

        public Forms.ContextMenuStrip Menu { get; private set; }

        public ClockMenu(ClockSettings settings, ClockWindow window, Func<bool> getStartup,
            Action<bool> setStartup, Action hide, Action exit)
        {
            this.settings = settings;
            this.window = window;
            this.getStartup = getStartup;
            Menu = new Forms.ContextMenuStrip
            {
                Font = uiFont,
                Padding = new Forms.Padding(6),
                ShowImageMargin = true,
                ShowCheckMargin = false,
                BackColor = MenuColors.Graphite,
                ForeColor = MenuColors.Text,
                Renderer = new ClockMenuRenderer(fonts),
                ShowItemToolTips = true
            };
            header = new ClockMenuHeader(settings, window);
            Menu.Items.Add(new Forms.ToolStripControlHost(header)
            {
                AutoSize = false,
                Size = header.Size,
                Margin = new Forms.Padding(0),
                Padding = new Forms.Padding(0)
            });
            Menu.Items.Add(new Forms.ToolStripSeparator());

            format = Branch("format", "时间格式");
            date = Choice("date", "显示日期", window.ToggleShowDate);
            seconds = Choice("seconds", "显示秒钟", window.ToggleShowSeconds);
            hour24 = Choice("hour24", "24 小时制", window.ToggleUse24Hour);
            format.DropDownItems.AddRange(new Forms.ToolStripItem[] { date, seconds, hour24 });
            ink = Branch("ink", "数字颜色");
            inkOptions = Options(ink, ClockLooks.InkNames, window.SetThemeMode);
            for (int i = 0; i < inkOptions.Length; i++)
            {
                inkOptions[i].Image = Swatch(ToColor(((Media.SolidColorBrush)ClockPalette.Create(i, 0).TimeInk).Color));
            }
            surface = Branch("surface", "背景颜色");
            surfaceOptions = Options(surface, ClockLooks.SurfaceNames, window.SetSurfaceTone);
            for (int i = 0; i < surfaceOptions.Length; i++)
            {
                surfaceOptions[i].Image = Swatch(ToColor(ClockPalette.Create(0, i).SurfaceTint));
                surfaceOptions[i].ShortcutKeyDisplayString = i < ClockLooks.TransparentSurfaceCount ? "透明" : "实色";
            }
            surface.DropDownItems.Insert(ClockLooks.TransparentSurfaceCount, new Forms.ToolStripSeparator());
            font = Branch("font", "数字字体");
            fontOptions = Options(font, ClockLooks.FontNames, window.SetFontMode);
            for (int i = 0; i < fontOptions.Length; i++)
            {
                fontOptions[i].ShortcutKeyDisplayString = "23:59";
                fontOptions[i].Tag = new MenuItemInfo { KeepOpen = true, FontSample = i };
            }
            size = Branch("size", "时钟大小");
            sizeOptions = Options(size, ClockLooks.ScaleNames, window.SetScaleMode);
            for (int i = 0; i < sizeOptions.Length; i++)
            {
                sizeOptions[i].ShortcutKeyDisplayString = (ClockLayout.Scales[i] * 100).ToString("0") + "%";
            }
            opacity = Branch("opacity", "背景浓度");
            double[] values = { OpacityPresets.Faint, OpacityPresets.Soft, OpacityPresets.Opaque };
            opacityOptions = Options(opacity, new[] { "更透", "适中", "较实" }, delegate(int i) { window.SetSurfaceOpacity(values[i]); });
            for (int i = 0; i < values.Length; i++)
            {
                opacityOptions[i].ShortcutKeyDisplayString = Math.Round(OpacityPresets.SurfaceAlpha(values[i]) * 100.0 / 255) + "%";
            }

            Menu.Items.Add(new Forms.ToolStripSeparator());
            topmost = Choice("topmost", "始终置顶", window.ToggleAlwaysOnTop);
            Menu.Items.Add(topmost);
            interaction = Branch("interaction", "位置与交互");
            locked = Choice("locked", "锁定位置", window.ToggleLocked);
            through = Choice("through", "鼠标穿透", window.ToggleClickThrough);
            interaction.DropDownItems.AddRange(new Forms.ToolStripItem[] { locked, through, new Forms.ToolStripSeparator() });
            interaction.DropDownItems.Add(Command("bottomLeft", "停靠到左下角", delegate { window.Show(); window.DockBottomLeft(); }));
            interaction.DropDownItems.Add(Command("topRight", "停靠到右上角", delegate { window.Show(); window.DockTopRight(); }));
            startup = Choice("startup", "开机自启", delegate { setStartup(!getStartup()); });
            Menu.Items.Add(startup);
            Menu.Items.Add(new Forms.ToolStripSeparator());
            visibility = Command("visibility", "隐藏时钟", delegate
            {
                if (window.IsVisible) hide();
                else { window.Show(); window.BringClockForward(); }
            });
            Menu.Items.Add(visibility);
            Forms.ToolStripMenuItem exitItem = Command("exit", "退出时钟", exit);
            exitItem.Tag = new MenuItemInfo { Destructive = true };
            Menu.Items.Add(exitItem);

            WireDropDown(Menu);
            previewTimer = new Forms.Timer { Interval = 1000 };
            previewTimer.Tick += delegate { header.Invalidate(); };
            Menu.Opening += delegate { keepOpen = false; RefreshState(); previewTimer.Start(); };
            Menu.Closed += delegate(object sender, Forms.ToolStripDropDownClosedEventArgs e)
            {
                previewTimer.Stop();
                if (returnFocus != IntPtr.Zero && (e.CloseReason == Forms.ToolStripDropDownCloseReason.Keyboard
                    || e.CloseReason == Forms.ToolStripDropDownCloseReason.CloseCalled
                    || e.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked))
                    SetForegroundWindow(returnFocus);
                returnFocus = IntPtr.Zero;
            };
            RefreshState();
        }

        public void Show(Point position)
        {
            if (disposed) return;
            if (!Menu.Visible) returnFocus = GetForegroundWindow();
            RefreshState();
            Menu.Show(position);
            // A menu opened from a nonactivating HWND still needs foreground dismissal.
            SetForegroundWindow(Menu.Handle);
        }

        private Forms.ToolStripMenuItem Branch(string name, string label)
        {
            Forms.ToolStripMenuItem item = NewItem(name, label);
            Menu.Items.Add(item);
            return item;
        }

        private Forms.ToolStripMenuItem[] Options(Forms.ToolStripMenuItem parent, string[] names, Action<int> select)
        {
            Forms.ToolStripMenuItem[] items = new Forms.ToolStripMenuItem[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int selected = i;
                items[i] = Choice(parent.Name + "." + i, names[i], delegate { select(selected); });
                parent.DropDownItems.Add(items[i]);
            }
            return items;
        }

        private Forms.ToolStripMenuItem Choice(string name, string label, Action action)
        {
            Forms.ToolStripMenuItem item = Command(name, label, action);
            item.Tag = new MenuItemInfo { KeepOpen = true };
            return item;
        }

        private Forms.ToolStripMenuItem Command(string name, string label, Action action)
        {
            Forms.ToolStripMenuItem item = NewItem(name, label);
            item.Click += delegate
            {
                action();
                if (!disposed) RefreshState();
            };
            return item;
        }

        private Forms.ToolStripMenuItem NewItem(string name, string label)
        {
            return new Forms.ToolStripMenuItem(label)
            {
                Name = name,
                Font = uiFont,
                ForeColor = MenuColors.Text,
                Padding = new Forms.Padding(4, 6, 8, 6),
                Tag = new MenuItemInfo()
            };
        }

        private void WireDropDown(Forms.ToolStripDropDown dropDown)
        {
            dropDown.BackColor = MenuColors.Graphite;
            dropDown.ForeColor = MenuColors.Text;
            dropDown.Font = uiFont;
            dropDown.Padding = new Forms.Padding(6);
            dropDown.Renderer = Menu.Renderer;
            dropDown.ItemClicked += delegate(object sender, Forms.ToolStripItemClickedEventArgs e)
            {
                MenuItemInfo info = e.ClickedItem.Tag as MenuItemInfo;
                keepOpen = info != null && info.KeepOpen;
            };
            dropDown.Closing += delegate(object sender, Forms.ToolStripDropDownClosingEventArgs e)
            {
                if (e.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked && keepOpen) e.Cancel = true;
            };
            foreach (Forms.ToolStripItem entry in dropDown.Items)
            {
                Forms.ToolStripMenuItem item = entry as Forms.ToolStripMenuItem;
                if (item != null && item.HasDropDownItems) WireDropDown(item.DropDown);
            }
        }

        public void RefreshState()
        {
            if (disposed) return;
            date.Checked = settings.ShowDate;
            seconds.Checked = settings.ShowSeconds;
            hour24.Checked = settings.Use24Hour;
            topmost.Checked = settings.AlwaysOnTop;
            locked.Checked = settings.Locked;
            through.Checked = settings.ClickThrough;
            through.ShortcutKeyDisplayString = window.IsHotKeyRegistered ? "Ctrl+Alt+T" : "热键不可用";
            through.ToolTipText = "穿透后，可单击托盘图标恢复鼠标交互。";
            startup.Checked = getStartup();
            Exclusive(inkOptions, settings.ThemeMode);
            Exclusive(surfaceOptions, settings.SurfaceTone);
            Exclusive(fontOptions, settings.FontMode);
            Exclusive(sizeOptions, settings.ScaleMode);
            Exclusive(opacityOptions, OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Faint) ? 0
                : OpacityPresets.Matches(settings.SurfaceOpacity, OpacityPresets.Soft) ? 1 : 2);
            format.ShortcutKeyDisplayString = (settings.Use24Hour ? "24 小时" : "12 小时") + (settings.ShowSeconds ? " · 秒" : "");
            ink.ShortcutKeyDisplayString = ClockLooks.InkNames[settings.ThemeMode];
            ink.Image = inkOptions[settings.ThemeMode].Image;
            surface.ShortcutKeyDisplayString = ClockLooks.SurfaceNames[settings.SurfaceTone];
            surface.Image = surfaceOptions[settings.SurfaceTone].Image;
            font.ShortcutKeyDisplayString = ClockLooks.FontNames[settings.FontMode];
            size.ShortcutKeyDisplayString = ClockLooks.ScaleNames[settings.ScaleMode];
            bool opaque = ClockLooks.IsOpaqueSurface(settings.SurfaceTone);
            opacity.Enabled = !opaque;
            opacity.ShortcutKeyDisplayString = opaque ? "实色背景" : Math.Round(OpacityPresets.SurfaceAlpha(settings.SurfaceOpacity) * 100.0 / 255) + "%";
            opacity.ToolTipText = opaque ? "浅色背景固定不透明，选择深色背景后可调整浓度。" : "仅改变背景透明度，数字保持清晰。";
            interaction.ShortcutKeyDisplayString = settings.ClickThrough ? "穿透中" : settings.Locked ? "已锁定"
                : settings.DockAnchor == 0 ? "自由位置" : settings.DockAnchor == 1 ? "右上角" : "左下角";
            visibility.Text = window.IsVisible ? "隐藏时钟" : "显示时钟";
            header.Invalidate();
        }

        private static void Exclusive(Forms.ToolStripMenuItem[] items, int selected)
        {
            for (int i = 0; i < items.Length; i++) items[i].Checked = i == selected;
        }

        private Bitmap Swatch(Color color)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            using (Pen outline = new Pen(Color.FromArgb(100, 210, 225, 222)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillEllipse(brush, 2, 2, 12, 12);
                graphics.DrawEllipse(outline, 2, 2, 12, 12);
            }
            images.Add(bitmap);
            return bitmap;
        }

        private static Color ToColor(Media.Color value) { return Color.FromArgb(value.R, value.G, value.B); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            previewTimer.Stop();
            previewTimer.Dispose();
            Menu.Dispose();
            foreach (Bitmap image in images) image.Dispose();
            fonts.Dispose();
            uiFont.Dispose();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }

    internal sealed class MenuItemInfo
    {
        public bool KeepOpen;
        public bool Destructive;
        public int FontSample = -1;
    }

    internal static class MenuColors
    {
        public static readonly Color Graphite = Color.FromArgb(24, 29, 33);
        public static readonly Color Raised = Color.FromArgb(35, 43, 47);
        public static readonly Color Line = Color.FromArgb(51, 62, 65);
        public static readonly Color Text = Color.FromArgb(235, 239, 232);
        public static readonly Color Secondary = Color.FromArgb(146, 165, 163);
        public static readonly Color Muted = Color.FromArgb(87, 107, 108);
        public static readonly Color Phosphor = Color.FromArgb(120, 226, 165);
        public static readonly Color Caution = Color.FromArgb(222, 160, 139);
    }

    internal sealed class ClockMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private readonly MenuFonts fonts;
        public ClockMenuRenderer(MenuFonts fonts) { this.fonts = fonts; RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(MenuColors.Graphite);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(MenuColors.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            using (SolidBrush brush = new SolidBrush(MenuColors.Raised)) e.Graphics.FillRectangle(brush, new Rectangle(0, 0, e.Item.Width, e.Item.Height));
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            Forms.ToolStripMenuItem item = e.Item as Forms.ToolStripMenuItem;
            MenuItemInfo info = e.Item.Tag as MenuItemInfo;
            bool detail = item != null && e.Text == item.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled ? MenuColors.Muted : detail ? MenuColors.Secondary
                : info != null && info.Destructive ? MenuColors.Caution : MenuColors.Text;
            if (detail && info != null && info.FontSample >= 0)
            {
                using (SolidBrush brush = new SolidBrush(e.TextColor))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    e.Graphics.DrawString(e.Text, fonts.Sample(info.FontSample), brush, e.TextRectangle, format);
                return;
            }
            base.OnRenderItemText(e);
            // WinForms suppresses shortcut text on parent items. Draw the selected
            // value ourselves so it stays visible before the submenu is opened.
            if (item != null && item.HasDropDownItems && !string.IsNullOrEmpty(item.ShortcutKeyDisplayString))
            {
                Forms.TextRenderer.DrawText(e.Graphics, item.ShortcutKeyDisplayString, e.TextFont,
                    new Rectangle(e.Item.Width - 166, e.TextRectangle.Y, 124, e.TextRectangle.Height),
                    item.Enabled ? MenuColors.Secondary : MenuColors.Muted,
                    Forms.TextFormatFlags.Right | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.EndEllipsis | Forms.TextFormatFlags.NoPadding);
            }
        }

        protected override void OnRenderItemImage(Forms.ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null) return;
            e.Graphics.DrawImage(e.Image, e.ImageRectangle);
            Forms.ToolStripMenuItem item = e.Item as Forms.ToolStripMenuItem;
            if (item != null && item.Checked)
            {
                using (Pen pen = new Pen(MenuColors.Phosphor, 1.4F))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawEllipse(pen, e.ImageRectangle.Left, e.ImageRectangle.Top, e.ImageRectangle.Width - 1, e.ImageRectangle.Height - 1);
                }
            }
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? MenuColors.Secondary : MenuColors.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            Rectangle rect = e.ImageRectangle;
            using (Pen pen = new Pen(MenuColors.Phosphor, 1.8F))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[] {
                    new PointF(rect.Left + 3, rect.Top + rect.Height * .52F),
                    new PointF(rect.Left + rect.Width * .43F, rect.Bottom - 4),
                    new PointF(rect.Right - 2, rect.Top + 3) });
            }
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen pen = new Pen(MenuColors.Line)) e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
    }

    internal sealed class ClockMenuHeader : Forms.Control
    {
        private readonly ClockSettings settings;
        private readonly ClockWindow window;
        private readonly Font caption = new Font("Microsoft YaHei UI", 8.5F);
        private readonly Font numbers = new Font("Consolas", 24F, FontStyle.Regular);

        public ClockMenuHeader(ClockSettings settings, ClockWindow window)
        {
            this.settings = settings;
            this.window = window;
            Size = new Size(288, 80);
            BackColor = MenuColors.Graphite;
            SetStyle(Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            AccessibleName = "悬浮时钟状态";
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            DateTime now = DateTime.Now;
            string time = now.ToString(settings.Use24Hour ? "HH:mm" : "hh:mm");
            string state = !window.IsVisible ? "已隐藏" : settings.ClickThrough ? "鼠标穿透中" : settings.Locked ? "位置已锁定" : "可拖动";
            Forms.TextRenderer.DrawText(e.Graphics, "FLOAT CLOCK", caption, new Point(12, 8), MenuColors.Secondary);
            Forms.TextRenderer.DrawText(e.Graphics, state, caption, new Rectangle(160, 8, Width - 174, 20), MenuColors.Phosphor,
                Forms.TextFormatFlags.Right | Forms.TextFormatFlags.NoPadding);
            Forms.TextRenderer.DrawText(e.Graphics, time, numbers, new Point(8, 30), MenuColors.Text);
            Forms.TextRenderer.DrawText(e.Graphics, now.ToString("MM 月 dd 日"), caption, new Rectangle(166, 36, Width - 180, 20), MenuColors.Secondary,
                Forms.TextFormatFlags.Right | Forms.TextFormatFlags.NoPadding);
            Forms.TextRenderer.DrawText(e.Graphics, settings.Use24Hour ? "本地时间" : ClockFormatter.Period(now, false), caption,
                new Rectangle(166, 54, Width - 180, 18), MenuColors.Muted, Forms.TextFormatFlags.Right | Forms.TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { caption.Dispose(); numbers.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class MenuFonts : IDisposable
    {
        private readonly PrivateFontCollection collection = new PrivateFontCollection();
        private readonly Font[] samples = new Font[8];
        private FontFamily[] families;
        private bool loaded;

        private void Load()
        {
            if (loaded) return;
            loaded = true;
            string[] files = { "Oxanium-SemiBold.ttf", "Orbitron-SemiBold.ttf", "ShareTechMono-Regular.ttf", "Exo2-SemiBold.ttf", "Rajdhani-SemiBold.ttf", "Audiowide-Regular.ttf", "Iceland-Regular.ttf", "Electrolize-Regular.ttf" };
            string[] names = { "Oxanium", "Orbitron", "Share Tech Mono", "Bahnschrift", "Rajdhani", "Audiowide", "Iceland", "Electrolize" };
            for (int i = 0; i < files.Length; i++)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, files[i]);
                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", files[i]);
                if (File.Exists(path))
                {
                    try { collection.AddFontFile(path); }
                    catch (ArgumentException) { }
                }
            }
            families = collection.Families;
            for (int i = 0; i < samples.Length; i++)
            {
                foreach (FontFamily family in families)
                {
                    if (family.Name.StartsWith(names[i], StringComparison.OrdinalIgnoreCase))
                    {
                        samples[i] = new Font(family, 11F, family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold);
                        break;
                    }
                }
                if (samples[i] == null) samples[i] = new Font(i == 3 ? "Bahnschrift" : "Consolas", 11F);
            }
        }
        public Font Sample(int index) { Load(); return samples[index]; }
        public void Dispose()
        {
            foreach (Font sample in samples) if (sample != null) sample.Dispose();
            if (families != null) foreach (FontFamily family in families) family.Dispose();
            collection.Dispose();
        }
    }
}
