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
        private readonly Forms.ToolStripMenuItem theme, format, ink, surface, font, size, opacity, interaction;
        private readonly Forms.ToolStripMenuItem date, seconds, hour24, topmost, locked, through, startup, visibility;
        private readonly Forms.ToolStripMenuItem[] themeOptions, inkOptions, surfaceOptions, fontOptions, sizeOptions, opacityOptions;
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
                MinimumSize = new Size(332, 0),
                ShowImageMargin = true,
                ShowCheckMargin = false,
                BackColor = MenuColors.Midnight,
                ForeColor = MenuColors.Text,
                Renderer = new ClockMenuRenderer(fonts),
                ShowItemToolTips = true
            };
            header = new ClockMenuHeader(settings, window, fonts);
            Menu.Items.Add(new Forms.ToolStripControlHost(header)
            {
                AutoSize = false,
                Size = header.Size,
                Margin = new Forms.Padding(0),
                Padding = new Forms.Padding(0)
            });
            Menu.Items.Add(new Forms.ToolStripSeparator());

            theme = Branch("theme", "主题预设");
            themeOptions = Options(theme, ClockThemePresets.Names, window.SetThemePreset);
            for (int i = 0; i < themeOptions.Length; i++)
            {
                themeOptions[i].ShortcutKeyDisplayString = ClockThemePresets.Captions[i];
                themeOptions[i].ToolTipText = ClockThemePresets.Descriptions[i] + "；保留大小、透明度和时间格式。";
                themeOptions[i].Image = ThemeSwatch(ClockThemePresets.Palette(i));
            }
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
            Menu.Items.Add(new Forms.ToolStripSeparator());
            ClockMenuFooter footer = new ClockMenuFooter();
            Menu.Items.Add(new Forms.ToolStripControlHost(footer)
            {
                AutoSize = false,
                Size = footer.Size,
                Margin = new Forms.Padding(0),
                Padding = new Forms.Padding(0)
            });

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
                Padding = new Forms.Padding(6, 6, 10, 6),
                Tag = new MenuItemInfo()
            };
        }

        private void WireDropDown(Forms.ToolStripDropDown dropDown)
        {
            dropDown.BackColor = MenuColors.Midnight;
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
            int ordinal = 0;
            foreach (Forms.ToolStripItem entry in dropDown.Items)
            {
                Forms.ToolStripMenuItem item = entry as Forms.ToolStripMenuItem;
                if (dropDown == Menu && item != null)
                    ((MenuItemInfo)item.Tag).Ordinal = ++ordinal;
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
            startup.Enabled = window.CanSaveSettings;
            startup.ToolTipText = window.CanSaveSettings ? "更改后立即保存。" : "设置文件读取失败或来自更新版本，本次禁止更改开机自启。";
            int preset = ClockThemePresets.Match(settings);
            Exclusive(themeOptions, preset);
            theme.ShortcutKeyDisplayString = preset >= 0 ? ClockThemePresets.Names[preset] : "自定义";
            theme.Image = preset >= 0 ? themeOptions[preset].Image : null;
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
                Point[] shape = { new Point(5, 2), new Point(15, 2), new Point(11, 14), new Point(1, 14) };
                graphics.FillPolygon(brush, shape);
                graphics.DrawPolygon(outline, shape);
            }
            images.Add(bitmap);
            return bitmap;
        }

        private Bitmap ThemeSwatch(ClockPalette palette)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush surfaceBrush = new SolidBrush(ToColor(palette.SurfaceTint)))
            using (SolidBrush inkBrush = new SolidBrush(ToColor(((Media.SolidColorBrush)palette.TimeInk).Color)))
            using (Pen border = new Pen(MenuColors.Cyan))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Point[] shape = { new Point(5, 2), new Point(15, 2), new Point(11, 14), new Point(1, 14) };
                graphics.FillPolygon(surfaceBrush, shape);
                graphics.FillPolygon(inkBrush, new[] { new Point(10, 2), new Point(15, 2), new Point(11, 14), new Point(6, 14) });
                graphics.DrawPolygon(border, shape);
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
        public int Ordinal;
    }

    internal static class MenuColors
    {
        public static readonly Color Midnight = Color.FromArgb(5, 18, 46);
        public static readonly Color Blue = Color.FromArgb(15, 78, 222);
        public static readonly Color Line = Color.FromArgb(23, 62, 112);
        public static readonly Color Text = Color.FromArgb(244, 250, 255);
        public static readonly Color Secondary = Color.FromArgb(132, 199, 231);
        public static readonly Color Muted = Color.FromArgb(104, 132, 165);
        public static readonly Color Cyan = Color.FromArgb(67, 222, 255);
        public static readonly Color Caution = Color.FromArgb(255, 167, 176);
        public static readonly Color SelectionText = Color.FromArgb(7, 34, 81);
    }

    internal sealed class ClockMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private readonly MenuFonts fonts;
        public ClockMenuRenderer(MenuFonts fonts) { this.fonts = fonts; RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(MenuColors.Midnight);
            int w = e.ToolStrip.Width, h = e.ToolStrip.Height;
            using (SolidBrush wing = new SolidBrush(Color.FromArgb(8, 28, 65)))
                e.Graphics.FillPolygon(wing, new[] { new Point(w, h / 3), new Point(w, h), new Point(w - 70, h) });
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(MenuColors.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen pen = new Pen(MenuColors.Cyan, 2)) e.Graphics.DrawLine(pen, 1, 1, e.ToolStrip.Width * .48F, 1);
            using (Pen pen = new Pen(MenuColors.Blue, 2)) e.Graphics.DrawLine(pen, e.ToolStrip.Width * .48F, 1, e.ToolStrip.Width - 2, 1);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            bool selected = e.Item.Enabled && e.Item.Selected;
            int w = e.Item.Width, h = e.Item.Height;
            int cut = Logical(e.Item, 9);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (selected)
            {
                using (SolidBrush brush = new SolidBrush(MenuColors.Text))
                    e.Graphics.FillPolygon(brush, new[] { new Point(cut, 1), new Point(w - 1, 1), new Point(w - cut - 1, h - 1), new Point(0, h - 1) });
                using (SolidBrush brush = new SolidBrush(MenuColors.Cyan))
                    e.Graphics.FillPolygon(brush, new[] { new Point(w - Logical(e.Item, 15), 1), new Point(w - 1, 1), new Point(w - cut - 1, h - 1), new Point(w - Logical(e.Item, 24), h - 1) });
            }
            MenuItemInfo info = e.Item.Tag as MenuItemInfo;
            Forms.ToolStripMenuItem item = e.Item as Forms.ToolStripMenuItem;
            if (info != null && info.Ordinal > 0 && e.Item.Image == null && (item == null || !item.Checked))
            {
                using (SolidBrush brush = new SolidBrush(selected ? MenuColors.Blue : MenuColors.Secondary))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    e.Graphics.DrawString(info.Ordinal.ToString("00"), fonts.Sample(4), brush, new Rectangle(0, 0, Logical(e.Item, 24), h), format);
            }
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            Forms.ToolStripMenuItem item = e.Item as Forms.ToolStripMenuItem;
            MenuItemInfo info = e.Item.Tag as MenuItemInfo;
            bool detail = item != null && e.Text == item.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled ? MenuColors.Muted : e.Item.Selected ? MenuColors.SelectionText : detail ? MenuColors.Secondary
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
                    new Rectangle(e.Item.Width - Logical(e.Item, 166), e.TextRectangle.Y, Logical(e.Item, 124), e.TextRectangle.Height),
                    !item.Enabled ? MenuColors.Muted : item.Selected ? MenuColors.SelectionText : MenuColors.Secondary,
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
                using (Pen pen = new Pen(item.Selected ? MenuColors.SelectionText : MenuColors.Cyan, 1.4F))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rect = e.ImageRectangle;
                    e.Graphics.DrawLine(pen, rect.Left, rect.Bottom + 1, rect.Right - 2, rect.Bottom + 1);
                }
            }
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = !e.Item.Enabled ? MenuColors.Muted : e.Item.Selected ? MenuColors.SelectionText : MenuColors.Cyan;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            Rectangle rect = e.ImageRectangle;
            using (Pen pen = new Pen(e.Item.Selected ? MenuColors.SelectionText : MenuColors.Cyan, 1.8F))
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
            using (Pen pen = new Pen(MenuColors.Blue, 2)) e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, 30, e.Item.Height / 2);
        }

        private static int Logical(Forms.ToolStripItem item, int pixels)
        {
            return (int)Math.Round(pixels * (item.Owner == null ? 96 : item.Owner.DeviceDpi) / 96.0);
        }
    }

    internal sealed class ClockMenuHeader : Forms.Control
    {
        private readonly ClockSettings settings;
        private readonly ClockWindow window;
        private readonly Font caption = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font title;
        private readonly Font numbers;
        private readonly Font edition;
        private readonly Font latin;

        public ClockMenuHeader(ClockSettings settings, ClockWindow window, MenuFonts fonts)
        {
            this.settings = settings;
            this.window = window;
            title = fonts.Display(51F);
            numbers = fonts.Display(23F);
            edition = fonts.Display(96F);
            latin = fonts.Display(11F);
            Size = new Size(316, 130);
            BackColor = MenuColors.Midnight;
            SetStyle(Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            AccessibleName = "悬浮时钟状态";
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            GraphicsState saved = g.Save();
            g.ScaleTransform(Width / 316F, Height / 130F);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush blue = new SolidBrush(MenuColors.Blue))
            using (SolidBrush cobalt = new SolidBrush(Color.FromArgb(10, 54, 157)))
            using (SolidBrush cyan = new SolidBrush(MenuColors.Cyan))
            using (SolidBrush white = new SolidBrush(MenuColors.Text))
            using (SolidBrush muted = new SolidBrush(MenuColors.Secondary))
            using (SolidBrush numeral = new SolidBrush(Color.FromArgb(36, 111, 235)))
            {
                g.FillPolygon(cobalt, new[] { new Point(15, 0), new Point(316, 0), new Point(316, 84), new Point(0, 99) });
                g.FillPolygon(blue, new[] { new Point(19, 0), new Point(267, 0), new Point(208, 91), new Point(0, 101), new Point(0, 25) });
                g.DrawString("03", edition, numeral, new PointF(223, -17));
                g.FillPolygon(cyan, new[] { new Point(297, 0), new Point(305, 0), new Point(250, 86), new Point(242, 87) });
                g.DrawString("F L O A T I N G   C L O C K", latin, white, new PointF(16, 9));

                // Lean the display title into the diagonal without distorting UI labels.
                GraphicsState titleState = g.Save();
                g.TranslateTransform(23, 21);
                g.MultiplyTransform(new Matrix(1, 0, -.16F, 1, 0, 0));
                g.DrawString("RELOAD", title, white, new PointF(0, 0));
                g.Restore(titleState);
                g.DrawString("DISPLAY / 显示设置", caption, white, new PointF(16, 78));

                DateTime now = DateTime.Now;
                string time = ClockFormatter.Hour(now, settings.Use24Hour) + ":" + ClockFormatter.Minute(now);
                string state = !window.CanSaveSettings ? "临时设置 · 不保存"
                    : !window.IsVisible ? "已隐藏" : settings.ClickThrough ? "鼠标穿透中" : settings.Locked ? "位置已锁定" : "可拖动";
                g.DrawString(time, numbers, white, new PointF(12, 103));
                string date = now.ToString("MM.dd", System.Globalization.CultureInfo.InvariantCulture);
                if (!settings.Use24Hour) date += "  " + ClockFormatter.Period(now, false);
                g.DrawString(date, latin, muted, new PointF(85, 113));
                using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    g.DrawString(state, caption, cyan, new RectangleF(156, 105, 145, 23), right);
            }
            g.Restore(saved);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { caption.Dispose(); title.Dispose(); numbers.Dispose(); edition.Dispose(); latin.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class ClockMenuFooter : Forms.Control
    {
        private readonly Font caption = new Font("Microsoft YaHei UI", 8F);

        public ClockMenuFooter()
        {
            Size = new Size(316, 24);
            BackColor = MenuColors.Midnight;
            TabStop = false;
            AccessibleName = "方向键选择，右方向键展开，Enter 应用，Esc 关闭";
            SetStyle(Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            Forms.TextRenderer.DrawText(e.Graphics, "↑↓ 选择    → 展开    Enter 应用    Esc 关闭", caption,
                new Rectangle(10, 1, Width - 20, Height - 2), MenuColors.Secondary,
                Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.NoPadding | Forms.TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) caption.Dispose();
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
            bool hasBahnschrift = ClockTypography.InstalledFamily("Bahnschrift") != null;
            string[] names = { "Oxanium", "Orbitron", "Share Tech Mono", hasBahnschrift ? "Bahnschrift" : "Exo 2", "Rajdhani", "Audiowide", "Iceland", "Electrolize" };
            for (int i = 0; i < files.Length; i++)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, files[i]);
                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", files[i]);
                if (!File.Exists(path))
                {
                    string assemblyRoot = Path.GetDirectoryName(typeof(MenuFonts).Assembly.Location);
                    path = Path.Combine(assemblyRoot, files[i]);
                    if (!File.Exists(path)) path = Path.Combine(assemblyRoot, "fonts", files[i]);
                    if (!File.Exists(path)) path = Path.GetFullPath(Path.Combine(assemblyRoot, "..", "fonts", files[i]));
                }
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
        public Font Sample(int index)
        {
            Load();
            return samples[index >= 0 && index < samples.Length ? index : 0];
        }
        public Font Display(float pixels)
        {
            return new Font(Sample(4).FontFamily, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        public void Dispose()
        {
            foreach (Font sample in samples) if (sample != null) sample.Dispose();
            if (families != null) foreach (FontFamily family in families) family.Dispose();
            collection.Dispose();
        }
    }
}
