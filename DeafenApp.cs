using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Mutex = System.Threading.Mutex;
using AbandonedMutexException = System.Threading.AbandonedMutexException;

namespace Deafen
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\Deafen.SingleInstance";
        private const string ShowMainWindowMessageName = "Deafen.ShowMainWindow";
        internal static readonly uint ShowMainWindowMessage =
            RegisterWindowMessage(ShowMainWindowMessageName);

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                using (Mutex appMutex = new Mutex(false, SingleInstanceMutexName))
                {
                    bool mutexOwned;
                    try
                    {
                        mutexOwned = appMutex.WaitOne(0, false);
                    }
                    catch (AbandonedMutexException)
                    {
                        mutexOwned = true;
                    }

                    if (!mutexOwned)
                    {
                        if (!ActivateExistingInstance())
                        {
                            MessageBox.Show(
                                "Deafen is already running, but its window could not be activated.",
                                "Deafen",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        return;
                    }

                    try
                    {
                        bool startHidden = args.Any(delegate(string argument)
                        {
                            return String.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase);
                        });
                        Application.Run(new MainForm(startHidden));
                    }
                    finally
                    {
                        appMutex.ReleaseMutex();
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "The application could not start:\r\n" + exception.Message,
                    "Deafen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool ActivateExistingInstance()
        {
            if (ShowMainWindowMessage == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            for (int attempt = 0; attempt < 30; attempt++)
            {
                IntPtr windowHandle = FindWindow(null, "Deafen");
                if (windowHandle != IntPtr.Zero)
                {
                    IntPtr result;
                    return SendMessageTimeout(
                        windowHandle,
                        ShowMainWindowMessage,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0x0002,
                        1000,
                        out result) != IntPtr.Zero;
                }

                System.Threading.Thread.Sleep(100);
            }

            return false;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);
    }

    internal sealed class FlatRoundedButton : Button
    {
        private bool _isHovered;
        private bool _isPressed;

        public FlatRoundedButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = Color.FromArgb(245, 247, 255);
            BackColor = Color.FromArgb(27, 38, 60);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
            TabStop = true;
        }

        public bool Accent { get; set; }

        protected override void OnMouseEnter(EventArgs args)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(args);
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            _isHovered = false;
            _isPressed = false;
            Invalidate();
            base.OnMouseLeave(args);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left)
                _isPressed = true;
            Invalidate();
            base.OnMouseDown(args);
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            _isPressed = false;
            Invalidate();
            base.OnMouseUp(args);
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            args.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;

            Color fill = Accent
                ? Color.FromArgb(112, 91, 244)
                : Color.FromArgb(27, 38, 60);
            if (_isHovered)
                fill = Accent ? Color.FromArgb(133, 113, 255) : Color.FromArgb(39, 53, 80);
            if (_isPressed)
                fill = Accent ? Color.FromArgb(91, 71, 218) : Color.FromArgb(21, 31, 51);

            using (GraphicsPath path = CreateRoundedPath(bounds, 8))
            using (SolidBrush background = new SolidBrush(fill))
            {
                args.Graphics.FillPath(background, path);
                if (!Accent)
                {
                    using (Pen border = new Pen(Color.FromArgb(48, 62, 88)))
                        args.Graphics.DrawPath(border, path);
                }
            }

            TextRenderer.DrawText(
                args.Graphics,
                Text,
                Font,
                bounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = bounds;
                focusBounds.Inflate(-5, -5);
                ControlPaint.DrawFocusRectangle(args.Graphics, focusBounds, ForeColor, Color.Transparent);
            }
        }

        internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ToggleOption : Control
    {
        private bool _checked;
        private bool _isHovered;

        public ToggleOption()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            ForeColor = Color.FromArgb(232, 238, 248);
            BackColor = Color.FromArgb(11, 16, 29);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            Size = new Size(250, 22);
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(args);
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(args);
        }

        protected override void OnClick(EventArgs args)
        {
            Checked = !Checked;
            base.OnClick(args);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                args.Handled = true;
            }
            base.OnKeyDown(args);
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            args.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(1, (Height - 16) / 2, 16, 16);
            Color borderColor = _checked
                ? Color.FromArgb(129, 108, 255)
                : (_isHovered ? Color.FromArgb(112, 126, 158) : Color.FromArgb(73, 87, 116));
            Color fillColor = _checked
                ? Color.FromArgb(112, 91, 244)
                : Color.FromArgb(18, 27, 45);

            using (GraphicsPath path = FlatRoundedButton.CreateRoundedPath(box, 4))
            using (SolidBrush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(borderColor))
            {
                args.Graphics.FillPath(fill, path);
                args.Graphics.DrawPath(border, path);
            }

            if (_checked)
            {
                using (Pen check = new Pen(Color.White, 1.8F))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    args.Graphics.DrawLines(check, new[]
                    {
                        new Point(4, box.Top + 8),
                        new Point(7, box.Top + 11),
                        new Point(13, box.Top + 5)
                    });
                }
            }

            TextRenderer.DrawText(
                args.Graphics,
                Text,
                Font,
                new Rectangle(26, 0, Math.Max(0, Width - 26), Height),
                ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = ClientRectangle;
                focusBounds.Inflate(-1, -2);
                ControlPaint.DrawFocusRectangle(args.Graphics, focusBounds, ForeColor, BackColor);
            }
        }
    }

    internal sealed class ProcessNameDialog : Form
    {
        private readonly TextBox _processNameInput = new TextBox();

        public ProcessNameDialog()
        {
            Text = "Add process by name";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 174);
            BackColor = Color.FromArgb(11, 16, 29);
            ForeColor = Color.FromArgb(232, 238, 248);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Label prompt = new Label
            {
                Text = "PROCESS NAME",
                Location = new Point(20, 17),
                Size = new Size(300, 20),
                ForeColor = Color.FromArgb(142, 155, 177),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point)
            };
            Controls.Add(prompt);

            _processNameInput.Location = new Point(20, 43);
            _processNameInput.Size = new Size(320, 28);
            _processNameInput.BackColor = Color.FromArgb(13, 21, 37);
            _processNameInput.ForeColor = Color.FromArgb(232, 238, 248);
            _processNameInput.BorderStyle = BorderStyle.FixedSingle;
            _processNameInput.Font = Font;
            _processNameInput.AccessibleName = "Process executable name";
            _processNameInput.Text = String.Empty;
            Controls.Add(_processNameInput);

            Label hint = new Label
            {
                Text = "Example: notepad.exe",
                Location = new Point(21, 76),
                Size = new Size(230, 18),
                ForeColor = Color.FromArgb(142, 155, 177)
            };
            Controls.Add(hint);

            FlatRoundedButton cancelButton = new FlatRoundedButton
            {
                Text = "Cancel",
                Location = new Point(148, 117),
                Size = new Size(88, 36),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(cancelButton);

            FlatRoundedButton addButton = new FlatRoundedButton
            {
                Text = "Add process",
                Location = new Point(246, 117),
                Size = new Size(94, 36),
                Accent = true,
                DialogResult = DialogResult.OK
            };
            Controls.Add(addButton);

            AcceptButton = addButton;
            CancelButton = cancelButton;
        }

        public string ProcessName
        {
            get { return _processNameInput.Text.Trim(); }
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(18, 27, 45); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(18, 27, 45); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(18, 27, 45); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(18, 27, 45); } }
        public override Color MenuBorder { get { return Color.FromArgb(48, 62, 88); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(112, 91, 244); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(39, 53, 80); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(39, 53, 80); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(39, 53, 80); } }
    }

    internal sealed class FlickerFreeListView : ListView
    {
        private const int GwlStyle = -16;
        private const int HdsNoSizing = 0x0800;
        private const int SbHorz = 0;
        private const int WmMouseWheel = 0x020A;
        private const int WmVScroll = 0x0115;
        private const int WmSize = 0x0005;
        private const int WmWindowPosChanged = 0x0047;
        private const int SbLineUp = 0;
        private const int SbLineDown = 1;
        private const int SbPageUp = 2;
        private const int SbPageDown = 3;
        private const int WheelDelta = 120;
        private const int WmPaint = 0x000F;
        private int _wheelDeltaRemainder;

        public event EventHandler NativeLayoutChanged;

        public FlickerFreeListView()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            SetWindowTheme(Handle, "DarkMode_Explorer", null);
            DisableColumnResizing();
            HideHorizontalScrollBar();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseWheel)
            {
                int delta = unchecked((short)((long)message.WParam >> 16));
                _wheelDeltaRemainder += delta;
                int wheelSteps = _wheelDeltaRemainder / WheelDelta;
                _wheelDeltaRemainder -= wheelSteps * WheelDelta;

                int lines = SystemInformation.MouseWheelScrollLines;
                if (wheelSteps != 0 && lines != 0)
                {
                    int scrollCommand = lines < 0
                        ? (wheelSteps > 0 ? SbPageUp : SbPageDown)
                        : (wheelSteps > 0 ? SbLineUp : SbLineDown);
                    int scrollCount = lines < 0 ? Math.Abs(wheelSteps) : Math.Abs(wheelSteps) * lines;
                    for (int index = 0; index < scrollCount; index++)
                    {
                        Message scrollMessage = Message.Create(
                            Handle,
                            WmVScroll,
                            new IntPtr(scrollCommand),
                            IntPtr.Zero);
                        base.WndProc(ref scrollMessage);
                    }
                }

                return;
            }

            if (message.Msg == WmPaint)
                HideHorizontalScrollBar();

            base.WndProc(ref message);
            HideHorizontalScrollBar();
            if (message.Msg == WmPaint)
                PaintEmptyArea();
            if (message.Msg == WmSize ||
                message.Msg == WmWindowPosChanged ||
                message.Msg == WmVScroll ||
                message.Msg == WmPaint)
            {
                EventHandler handler = NativeLayoutChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        public void HideHorizontalScrollBar()
        {
            if (IsHandleCreated)
                ShowScrollBar(Handle, SbHorz, false);
        }

        private void PaintEmptyArea()
        {
            if (!IsHandleCreated || Items.Count == 0)
                return;

            Rectangle lastItemBounds = GetItemRect(Items.Count - 1, ItemBoundsPortion.Entire);
            int emptyAreaTop = lastItemBounds.Bottom;
            if (emptyAreaTop < 0 || emptyAreaTop >= ClientSize.Height)
                return;

            using (Graphics graphics = Graphics.FromHwnd(Handle))
            using (SolidBrush background = new SolidBrush(BackColor))
            {
                graphics.FillRectangle(
                    background,
                    0,
                    emptyAreaTop,
                    ClientSize.Width,
                    ClientSize.Height - emptyAreaTop);
            }
        }

        public void DisableColumnResizing()
        {
            IntPtr header = GetHeaderHandle();
            if (header == IntPtr.Zero)
                return;

            int style = GetWindowLong(header, GwlStyle);
            SetWindowLong(header, GwlStyle, style | HdsNoSizing);
            SetWindowPos(header, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        }

        private IntPtr GetHeaderHandle()
        {
            if (!IsHandleCreated)
                return IntPtr.Zero;

            IntPtr child = GetWindow(Handle, 5);
            while (child != IntPtr.Zero)
            {
                StringBuilder className = new StringBuilder(32);
                GetClassName(child, className, className.Capacity);
                if (String.Equals(className.ToString(), "SysHeader32", StringComparison.Ordinal))
                    return child;

                child = GetWindow(child, 2);
            }

            return IntPtr.Zero;
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr windowHandle, string applicationName, string idList);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr windowHandle, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowScrollBar(IntPtr windowHandle, int bar, bool show);

        [DllImport("user32.dll", EntryPoint = "GetWindow", SetLastError = true)]
        private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }

    internal sealed class ProcessPicker : Control
    {
        private readonly List<object> _items = new List<object>();
        private readonly ToolStripDropDown _popup = new ToolStripDropDown();
        private readonly ListBox _listBox = new ListBox();
        private readonly ToolStripControlHost _host;
        private int _selectedIndex = -1;
        private int _hoveredIndex = -1;
        private int _updateCount;
        private bool _syncingPopup;
        private bool _isHovered;

        public ProcessPicker()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.FromArgb(13, 21, 37);
            ForeColor = Color.FromArgb(232, 238, 248);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.ComboBox;
            AccessibleName = "Currently running processes";
            ItemHeight = 24;
            MaxDropDownItems = 18;

            _listBox.BorderStyle = BorderStyle.None;
            _listBox.DrawMode = DrawMode.OwnerDrawFixed;
            _listBox.IntegralHeight = false;
            _listBox.ItemHeight = ItemHeight;
            _listBox.BackColor = BackColor;
            _listBox.ForeColor = ForeColor;
            _listBox.Font = Font;
            _listBox.DrawItem += DrawPopupItem;
            _listBox.SelectedIndexChanged += SelectPopupItem;
            _listBox.MouseMove += UpdateHoveredItem;
            _listBox.MouseLeave += ClearHoveredItem;

            _popup.AutoClose = true;
            _popup.Padding = Padding.Empty;
            _popup.Margin = Padding.Empty;
            _popup.BackColor = BackColor;
            _popup.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            _popup.Closed += delegate
            {
                if (CanFocus)
                    Focus();
                Invalidate();
            };

            _host = new ToolStripControlHost(_listBox)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _popup.Items.Add(_host);
        }

        public event EventHandler DropDown;
        public event EventHandler SelectedIndexChanged;
        public event DrawItemEventHandler DrawItem;

        public IList<object> Items
        {
            get { return _items; }
        }

        public int ItemHeight { get; set; }

        public int MaxDropDownItems { get; set; }

        public bool IsItemHovered(int index)
        {
            return _popup.Visible && _hoveredIndex == index;
        }

        public object SelectedItem
        {
            get
            {
                return _selectedIndex >= 0 && _selectedIndex < _items.Count
                    ? _items[_selectedIndex]
                    : null;
            }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int nextIndex = value < 0 || value >= _items.Count ? -1 : value;
                if (_selectedIndex == nextIndex)
                    return;

                _selectedIndex = nextIndex;
                if (_popup.Visible && _selectedIndex < _listBox.Items.Count)
                {
                    _syncingPopup = true;
                    _listBox.SelectedIndex = _selectedIndex;
                    _syncingPopup = false;
                }

                Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        public void BeginUpdate()
        {
            if (_updateCount == 0 && _selectedIndex != -1)
            {
                _selectedIndex = -1;
                Invalidate();
                EventHandler selectedIndexChanged = SelectedIndexChanged;
                if (selectedIndexChanged != null)
                    selectedIndexChanged(this, EventArgs.Empty);
            }

            _updateCount++;
        }

        public void EndUpdate()
        {
            if (_updateCount == 0)
                throw new InvalidOperationException("EndUpdate was called without a matching BeginUpdate.");

            _updateCount--;
            if (_updateCount == 0)
                RefreshPopupItems();
        }

        public void ShowDropDown()
        {
            if (_popup.Visible || !Enabled)
                return;

            EventHandler handler = DropDown;
            if (handler != null)
                handler(this, EventArgs.Empty);

            RefreshPopupItems();
            int visibleItems = Math.Min(_items.Count, Math.Max(1, MaxDropDownItems));
            int popupHeight = Math.Max(ItemHeight, visibleItems * ItemHeight + 4);
            _listBox.Size = new Size(Math.Max(1, Width - 2), popupHeight - 4);
            _host.Size = new Size(Width, popupHeight);
            _popup.Show(this, new Point(0, Height));
        }

        public void CloseDropDown()
        {
            if (_popup.Visible)
                _popup.Close();
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(args);
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(args);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left)
                Focus();
            base.OnMouseDown(args);
        }

        protected override void OnClick(EventArgs args)
        {
            if (_popup.Visible)
                CloseDropDown();
            else
                ShowDropDown();
            base.OnClick(args);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Down || args.KeyCode == Keys.Enter || args.KeyCode == Keys.Space)
            {
                ShowDropDown();
                args.Handled = true;
            }
            else if (args.KeyCode == Keys.Up && _items.Count > 0)
            {
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                args.Handled = true;
            }
            else if (args.KeyCode == Keys.Escape && _popup.Visible)
            {
                CloseDropDown();
                args.Handled = true;
            }

            base.OnKeyDown(args);
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            args.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;

            using (GraphicsPath path = FlatRoundedButton.CreateRoundedPath(bounds, 7))
            using (SolidBrush background = new SolidBrush(BackColor))
            using (Pen border = new Pen(
                Focused || _popup.Visible
                    ? Color.FromArgb(112, 91, 244)
                    : (_isHovered ? Color.FromArgb(73, 87, 116) : Color.FromArgb(48, 62, 88))))
            {
                args.Graphics.FillPath(background, path);
                args.Graphics.DrawPath(border, path);
            }

            int arrowWidth = Math.Min(38, Width / 3);
            Rectangle arrowBounds = new Rectangle(Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
            using (SolidBrush arrowBackground = new SolidBrush(Color.FromArgb(22, 33, 54)))
                args.Graphics.FillRectangle(arrowBackground, arrowBounds);

            int arrowCenterX = arrowBounds.Left + arrowBounds.Width / 2;
            int arrowCenterY = arrowBounds.Top + arrowBounds.Height / 2;
            using (Pen arrow = new Pen(Color.FromArgb(184, 197, 219), 1.6F))
            {
                arrow.StartCap = LineCap.Round;
                arrow.EndCap = LineCap.Round;
                args.Graphics.DrawLines(arrow, new[]
                {
                    new Point(arrowCenterX - 4, arrowCenterY - 2),
                    new Point(arrowCenterX, arrowCenterY + 2),
                    new Point(arrowCenterX + 4, arrowCenterY - 2)
                });
            }

            int selectedWidth = Math.Max(0, arrowBounds.Left - 8);
            if (SelectedItem != null)
            {
                DrawItemEventHandler handler = DrawItem;
                if (handler != null)
                {
                    handler(
                        this,
                        new DrawItemEventArgs(
                            args.Graphics,
                            Font,
                            new Rectangle(1, 1, selectedWidth, Height - 2),
                            -1,
                            DrawItemState.None,
                            ForeColor,
                            BackColor));
                }
            }
            else
            {
                TextRenderer.DrawText(
                    args.Graphics,
                    "Select a running app",
                    Font,
                    new Rectangle(8, 1, selectedWidth, Height - 2),
                    Color.FromArgb(142, 155, 177),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = ClientRectangle;
                focusBounds.Inflate(-4, -4);
                ControlPaint.DrawFocusRectangle(args.Graphics, focusBounds, ForeColor, BackColor);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _popup.Dispose();
            base.Dispose(disposing);
        }

        private void RefreshPopupItems()
        {
            if (_updateCount > 0)
                return;

            _syncingPopup = true;
            _listBox.BeginUpdate();
            try
            {
                _listBox.Items.Clear();
                foreach (object item in _items)
                    _listBox.Items.Add(item);

                if (_selectedIndex >= 0 && _selectedIndex < _listBox.Items.Count)
                    _listBox.SelectedIndex = _selectedIndex;
            }
            finally
            {
                _listBox.EndUpdate();
                _syncingPopup = false;
            }

            Invalidate();
        }

        private void DrawPopupItem(object sender, DrawItemEventArgs args)
        {
            DrawItemEventHandler handler = DrawItem;
            if (handler != null && args.Index >= 0)
                handler(this, args);
        }

        private void UpdateHoveredItem(object sender, MouseEventArgs args)
        {
            int hoveredIndex = _listBox.IndexFromPoint(args.Location);
            if (hoveredIndex == _hoveredIndex)
                return;

            int previousIndex = _hoveredIndex;
            _hoveredIndex = hoveredIndex;
            if (previousIndex >= 0 && previousIndex < _listBox.Items.Count)
                _listBox.Invalidate(_listBox.GetItemRectangle(previousIndex));
            if (_hoveredIndex >= 0 && _hoveredIndex < _listBox.Items.Count)
                _listBox.Invalidate(_listBox.GetItemRectangle(_hoveredIndex));
        }

        private void ClearHoveredItem(object sender, EventArgs args)
        {
            if (_hoveredIndex < 0)
                return;

            int previousIndex = _hoveredIndex;
            _hoveredIndex = -1;
            if (previousIndex < _listBox.Items.Count)
                _listBox.Invalidate(_listBox.GetItemRectangle(previousIndex));
        }

        private void SelectPopupItem(object sender, EventArgs args)
        {
            if (_syncingPopup || _listBox.SelectedIndex < 0)
                return;

            SelectedIndex = _listBox.SelectedIndex;
            _popup.Close();
        }
    }

    internal sealed class MainForm : Form
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "Deafen";
        private const string LegacyStartupValueName = "MuteWhenMinimized";
        private const uint EventSystemForeground = 0x0003;
        private const uint EventSystemMinimizeStart = 0x0016;
        private const uint EventSystemMinimizeEnd = 0x0017;
        private const uint WinEventOutOfContext = 0x0000;
        private const int MonitoringFallbackInterval = 2000;
        private static readonly Color WindowColor = Color.FromArgb(11, 16, 29);
        private static readonly Color SurfaceColor = Color.FromArgb(18, 27, 45);
        private static readonly Color InputColor = Color.FromArgb(13, 21, 37);
        private static readonly Color TextColor = Color.FromArgb(232, 238, 248);
        private static readonly Color MutedTextColor = Color.FromArgb(142, 155, 177);
        private readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Deafen",
            "Deafen.json");
        private readonly string _legacyConfigPath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "processes.json");
        private readonly string _legacyConfigPathBesideExecutable = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "Deafen.json");
        private readonly List<string> _processNames = new List<string>();
        private readonly Timer _timer = new Timer();
        private readonly Panel _processCard = new Panel();
        private readonly FlickerFreeListView _processList = new FlickerFreeListView();
        private readonly ImageList _processRowHeightImages = new ImageList();
        private readonly Bitmap _processRowHeightImage = new Bitmap(1, 26);
        private readonly ProcessPicker _runningProcessPicker = new ProcessPicker();
        private readonly Label _runningProcessLabel = new Label();
        private readonly FlatRoundedButton _addSelectedButton = new FlatRoundedButton();
        private readonly FlatRoundedButton _addByNameButton = new FlatRoundedButton();
        private readonly ToggleOption _startupCheckBox = new ToggleOption();
        private readonly ToggleOption _minimizeToTrayCheckBox = new ToggleOption();
        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();
        private readonly Icon _appIcon;
        private readonly WinEventDelegate _windowEventCallback;
        private readonly bool _startHidden;
        private bool _exitRequested;
        private bool _trayNotificationShown;
        private bool _updatingOptions;
        private bool _isRefreshing;
        private bool _audioRefreshPending;
        private bool _monitoringErrorShown;
        private bool _processPickerErrorShown;
        private IntPtr _foregroundEventHook;
        private IntPtr _minimizeEventHook;

        public MainForm(bool startHidden)
        {
            _startHidden = startHidden;
            _windowEventCallback = HandleWindowEvent;
            Text = "Deafen";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ClientSize = new Size(680, 700);
            int nonClientHeight = Height - ClientSize.Height;
            MinimumSize = new Size(Width, 450 + nonClientHeight);
            MaximumSize = new Size(Width, 1200 + nonClientHeight);
            BackColor = WindowColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;

            _appIcon = CreateAppIcon();
            Icon = _appIcon;

            MinimizeToTrayOnClose = true;
            LoadSettings();
            BuildInterface();
            InitializeTray();
            InitializeStartupOption();
            InitializeCloseOption();

            _timer.Interval = MonitoringFallbackInterval;
            _timer.Tick += delegate { RefreshAudioState(); };
            HandleCreated += delegate { SetDarkTitleBar(); };
            SizeChanged += delegate { LayoutInterface(); };
            Shown += delegate
            {
                InstallWindowEventHooks();
                AudioSessionController.SessionCreated += HandleAudioSessionCreated;
                try
                {
                    AudioSessionController.StartSessionNotifications();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        "Deafen could not enable immediate audio-session detection. It will continue checking every 2 seconds.\r\n\r\n" +
                        exception.Message,
                        "Deafen",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                TryRefreshRunningProcesses();
                RefreshAudioState();
                _timer.Start();
                if (_startHidden && MinimizeToTrayOnClose)
                    Hide();
            };
            FormClosing += HandleFormClosing;
            FormClosed += HandleFormClosed;
        }

        private void InstallWindowEventHooks()
        {
            try
            {
                uint flags = WinEventOutOfContext;
                _foregroundEventHook = SetWinEventHook(
                    EventSystemForeground,
                    EventSystemForeground,
                    IntPtr.Zero,
                    _windowEventCallback,
                    0,
                    0,
                    flags);
                if (_foregroundEventHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                _minimizeEventHook = SetWinEventHook(
                    EventSystemMinimizeStart,
                    EventSystemMinimizeEnd,
                    IntPtr.Zero,
                    _windowEventCallback,
                    0,
                    0,
                    flags);
                if (_minimizeEventHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

            }
            catch (Exception exception)
            {
                StopWindowEventHooks();
                MessageBox.Show(
                    "Deafen could not enable immediate focus-change monitoring. It will continue checking every 2 seconds.\r\n\r\n" +
                    exception.Message,
                    "Deafen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void HandleWindowEvent(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime)
        {
            if (eventType != EventSystemForeground &&
                eventType != EventSystemMinimizeStart &&
                eventType != EventSystemMinimizeEnd)
            {
                return;
            }

            QueueAudioRefresh();
        }

        private void HandleAudioSessionCreated()
        {
            QueueAudioRefresh();
        }

        private void QueueAudioRefresh()
        {
            if (_audioRefreshPending || IsDisposed || !IsHandleCreated)
                return;

            _audioRefreshPending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _audioRefreshPending = false;
                    if (!IsDisposed)
                        RefreshAudioState();
                });
            }
            catch (InvalidOperationException)
            {
                _audioRefreshPending = false;
            }
        }

        private void StopWindowEventHooks()
        {
            if (_foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundEventHook);
                _foregroundEventHook = IntPtr.Zero;
            }

            if (_minimizeEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_minimizeEventHook);
                _minimizeEventHook = IntPtr.Zero;
            }
        }

        private void LoadSettings()
        {
            if (File.Exists(_configPath))
            {
                AppSettings settings = DeserializeSettings(
                    File.ReadAllText(_configPath),
                    Path.GetFileName(_configPath));
                LoadProcessNames(settings, false);
                MinimizeToTrayOnClose = settings.MinimizeToTray ?? true;
                return;
            }

            string migrationSource = File.Exists(_legacyConfigPathBesideExecutable)
                ? _legacyConfigPathBesideExecutable
                : File.Exists(_legacyConfigPath) ? _legacyConfigPath : null;

            if (migrationSource == null)
            {
                SaveSettings();
                return;
            }

            AppSettings legacySettings = DeserializeSettings(
                File.ReadAllText(migrationSource),
                Path.GetFileName(migrationSource));
            LoadProcessNames(legacySettings, String.Equals(
                migrationSource,
                _legacyConfigPath,
                StringComparison.OrdinalIgnoreCase));
            MinimizeToTrayOnClose = legacySettings.MinimizeToTray ?? true;
            SaveSettings();
        }

        private static AppSettings DeserializeSettings(string json, string fileName)
        {
            AppSettings settings = new JavaScriptSerializer().Deserialize<AppSettings>(json);
            if (settings == null || settings.Processes == null)
                throw new InvalidDataException(fileName + " must contain a 'Processes' array.");

            return settings;
        }

        private void LoadProcessNames(AppSettings settings, bool importingLegacySettings)
        {
            foreach (string configuredName in settings.Processes)
            {
                string processName = NormalizeProcessName(configuredName);
                if (importingLegacySettings &&
                    String.Equals(processName, "PAYDAY2", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_processNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
                    _processNames.Add(processName);
            }
        }

        private bool MinimizeToTrayOnClose { get; set; }

        private void SaveSettings()
        {
            AppSettings settings = new AppSettings
            {
                Processes = _processNames.Select(delegate(string name) { return name + ".exe"; }).ToArray(),
                MinimizeToTray = MinimizeToTrayOnClose
            };

            string temporaryPath = _configPath + ".tmp";
            string backupPath = temporaryPath + ".bak";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                string json = new JavaScriptSerializer().Serialize(settings);
                File.WriteAllText(temporaryPath, json + Environment.NewLine, new System.Text.UTF8Encoding(false));

                if (File.Exists(_configPath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    File.Replace(temporaryPath, _configPath, backupPath);
                }
                else
                {
                    File.Move(temporaryPath, _configPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
        }

        private void BuildInterface()
        {
            Label title = new Label
            {
                Text = "Deafen",
                Location = new Point(24, 19),
                Size = new Size(620, 36),
                Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = TextColor
            };
            Controls.Add(title);

            Label description = new Label
            {
                Text = "Keep your games quiet in the background. Audio returns as soon as a tracked window is active.",
                Location = new Point(27, 61),
                Size = new Size(625, 23),
                ForeColor = MutedTextColor
            };
            Controls.Add(description);

            Label sectionLabel = new Label
            {
                Text = "TRACKED PROCESSES",
                Location = new Point(25, 96),
                Size = new Size(220, 18),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = MutedTextColor
            };
            Controls.Add(sectionLabel);

            _processCard.Location = new Point(20, 118);
            _processCard.Size = new Size(640, 280);
            _processCard.BackColor = SurfaceColor;
            _processCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _processCard.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (Pen border = new Pen(Color.FromArgb(36, 49, 72)))
                    args.Graphics.DrawRectangle(border, 0, 0, _processCard.Width - 1, _processCard.Height - 1);
            };
            _processCard.Resize += delegate
            {
                _processCard.Invalidate();
                _processList.Invalidate();
            };

            _processList.View = View.Details;
            _processList.HeaderStyle = ColumnHeaderStyle.None;
            _processList.FullRowSelect = true;
            _processList.GridLines = false;
            _processList.MultiSelect = false;
            _processList.HideSelection = false;
            _processList.BorderStyle = BorderStyle.None;
            _processList.BackColor = SurfaceColor;
            _processList.ForeColor = TextColor;
            _processList.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            _processList.OwnerDraw = true;
            _processRowHeightImages.ColorDepth = ColorDepth.Depth32Bit;
            _processRowHeightImages.ImageSize = new Size(1, 26);
            using (Graphics graphics = Graphics.FromImage(_processRowHeightImage))
                graphics.Clear(Color.Transparent);
            _processRowHeightImages.Images.Add(_processRowHeightImage);
            _processList.SmallImageList = _processRowHeightImages;
            _processList.DrawItem += DrawProcessItem;
            _processList.DrawSubItem += DrawProcessSubItem;
            _processList.Columns.Add("PROCESS", 220);
            _processList.Columns.Add("STATUS", 330);
            _processList.Columns.Add("REMOVE", 70);
            _processList.SizeChanged += delegate { UpdateProcessColumns(); };
            _processList.NativeLayoutChanged += delegate { UpdateProcessColumns(); };
            _processList.MouseMove += UpdateDeleteButtonCursor;
            _processList.MouseClick += RemoveProcessFromRow;
            _processCard.Controls.Add(_processList);
            Controls.Add(_processCard);

            _startupCheckBox.Text = "Start with Windows";
            _startupCheckBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _startupCheckBox.CheckedChanged += ChangeStartupOption;
            Controls.Add(_startupCheckBox);

            _minimizeToTrayCheckBox.Text = "Minimize to tray on close";
            _minimizeToTrayCheckBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _minimizeToTrayCheckBox.CheckedChanged += ChangeCloseOption;
            Controls.Add(_minimizeToTrayCheckBox);

            _runningProcessPicker.ItemHeight = 24;
            _runningProcessPicker.MaxDropDownItems = 18;
            _runningProcessPicker.BackColor = InputColor;
            _runningProcessPicker.ForeColor = TextColor;
            _runningProcessPicker.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _runningProcessPicker.DrawItem += DrawRunningProcess;
            _runningProcessPicker.SelectedIndexChanged += delegate
            {
                RunningProcessChoice choice = _runningProcessPicker.SelectedItem as RunningProcessChoice;
                _addSelectedButton.Enabled = choice != null &&
                    !_processNames.Contains(choice.ProcessName, StringComparer.OrdinalIgnoreCase);
                _addSelectedButton.Invalidate();
            };
            _runningProcessPicker.DropDown += delegate { TryRefreshRunningProcesses(); };
            _runningProcessPicker.AccessibleName = "Currently running processes";
            Controls.Add(_runningProcessPicker);

            _runningProcessLabel.Text = "RUNNING APPS";
            _runningProcessLabel.Location = new Point(25, 0);
            _runningProcessLabel.Size = new Size(420, 18);
            _runningProcessLabel.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            _runningProcessLabel.ForeColor = MutedTextColor;
            Controls.Add(_runningProcessLabel);

            _addSelectedButton.Text = "Add selected";
            _addSelectedButton.Accent = true;
            _addByNameButton.Accent = true;
            _addSelectedButton.Size = new Size(112, 36);
            _addSelectedButton.Enabled = false;
            _addSelectedButton.Click += delegate
            {
                RunningProcessChoice choice = _runningProcessPicker.SelectedItem as RunningProcessChoice;
                if (choice == null)
                    return;

                AddTrackedProcess(choice.ProcessName);
            };
            Controls.Add(_addSelectedButton);

            _addByNameButton.Text = "Add by name";
            _addByNameButton.Size = new Size(112, 36);
            _addByNameButton.Click += delegate
            {
                using (ProcessNameDialog dialog = new ProcessNameDialog())
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        AddTrackedProcess(dialog.ProcessName);
                }
            };
            Controls.Add(_addByNameButton);

            _trayMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            _trayMenu.BackColor = SurfaceColor;
            _trayMenu.ForeColor = TextColor;
            LayoutInterface();
        }

        private void LayoutInterface()
        {
            if (_processCard == null || _processList == null)
                return;

            int actionTop = ClientSize.Height - 83;
            _processCard.SetBounds(20, 118, Math.Max(0, ClientSize.Width - 40), Math.Max(120, actionTop - 155));
            int cardInset = 12;
            int listWidth = Math.Max(0, _processCard.ClientSize.Width - cardInset * 2);
            int listHeight = Math.Max(0, _processCard.ClientSize.Height - cardInset * 2);
            _processList.SetBounds(cardInset, cardInset, listWidth, listHeight);

            int addByNameLeft = ClientSize.Width - 24 - _addByNameButton.Width;
            int addSelectedLeft = addByNameLeft - 8 - _addSelectedButton.Width;

            _runningProcessLabel.Location = new Point(25, actionTop - 23);

            _runningProcessPicker.SetBounds(24, actionTop, Math.Max(120, addSelectedLeft - 10 - 24), 36);
            _addSelectedButton.Location = new Point(addSelectedLeft, actionTop);
            _addByNameButton.Location = new Point(addByNameLeft, actionTop);

            int optionsTop = ClientSize.Height - 35;
            _startupCheckBox.SetBounds(25, optionsTop, 250, 22);
            _minimizeToTrayCheckBox.SetBounds(342, optionsTop, 300, 22);
            UpdateProcessColumns();
        }

        private void DrawRunningProcess(object sender, DrawItemEventArgs args)
        {
            int itemIndex = args.Index < 0 ? _runningProcessPicker.SelectedIndex : args.Index;
            if (itemIndex < 0 || itemIndex >= _runningProcessPicker.Items.Count)
                return;

            bool isHighlighted =
                (args.State & DrawItemState.Selected) == DrawItemState.Selected ||
                _runningProcessPicker.IsItemHovered(args.Index);
            using (SolidBrush background = new SolidBrush(
                isHighlighted
                    ? Color.FromArgb(38, 45, 75)
                    : InputColor))
            {
                args.Graphics.FillRectangle(background, args.Bounds);
            }

            RunningProcessChoice choice = _runningProcessPicker.Items[itemIndex] as RunningProcessChoice;
            if (choice == null)
            {
                using (SolidBrush placeholderBrush = new SolidBrush(MutedTextColor))
                    TextRenderer.DrawText(
                        args.Graphics,
                        Convert.ToString(_runningProcessPicker.Items[itemIndex]),
                        Font,
                        args.Bounds,
                        placeholderBrush.Color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }

            using (SolidBrush processBrush = new SolidBrush(TextColor))
            using (SolidBrush detailBrush = new SolidBrush(MutedTextColor))
            {
                float textX = args.Bounds.Left + 8;
                string trackedSuffix = _processNames.Contains(choice.ProcessName, StringComparer.OrdinalIgnoreCase)
                    ? "  (tracked)"
                    : String.Empty;
                string processLabel = choice.ProcessName + ".exe" + trackedSuffix;
                TextRenderer.DrawText(
                    args.Graphics,
                    processLabel,
                    Font,
                    new Rectangle((int)textX, args.Bounds.Top, Math.Max(0, args.Bounds.Right - (int)textX - 12), args.Bounds.Height),
                    processBrush.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                if (!String.IsNullOrWhiteSpace(choice.WindowTitle))
                {
                    string title = " — " + choice.WindowTitle;
                    SizeF titleSize = args.Graphics.MeasureString(title, Font);
                    float processLabelWidth = args.Graphics.MeasureString(processLabel, Font).Width;
                    float remainingWidth = args.Bounds.Right - textX - processLabelWidth - 12;
                    if (titleSize.Width > remainingWidth && remainingWidth > 18)
                        title = " — " + choice.WindowTitle.Substring(0, Math.Min(choice.WindowTitle.Length, Math.Max(1, (int)(choice.WindowTitle.Length * remainingWidth / titleSize.Width)))) + "…";

                    TextRenderer.DrawText(
                        args.Graphics,
                        title,
                        Font,
                        new Rectangle(
                            (int)(textX + processLabelWidth),
                            args.Bounds.Top,
                            Math.Max(0, args.Bounds.Right - (int)(textX + processLabelWidth) - 12),
                            args.Bounds.Height),
                        detailBrush.Color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }

                if (choice.InstanceCount > 1)
                {
                    string instanceCount = " (" + choice.InstanceCount + ")";
                    SizeF instanceSize = args.Graphics.MeasureString(instanceCount, Font);
                    TextRenderer.DrawText(
                        args.Graphics,
                        instanceCount,
                        Font,
                        new Rectangle(
                            args.Bounds.Right - (int)Math.Ceiling(instanceSize.Width) - 12,
                            args.Bounds.Top,
                            (int)Math.Ceiling(instanceSize.Width),
                            args.Bounds.Height),
                        detailBrush.Color,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }

            if ((args.State & DrawItemState.Focus) == DrawItemState.Focus)
                args.DrawFocusRectangle();
        }

        private void TryRefreshRunningProcesses()
        {
            try
            {
                RefreshRunningProcesses();
            }
            catch (Exception exception)
            {
                if (!_processPickerErrorShown)
                {
                    MessageBox.Show(
                        "Could not refresh running apps:\r\n" + exception.Message,
                        "Process list error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    _processPickerErrorShown = true;
                }
            }
        }

        private void RefreshRunningProcesses()
        {
            string selectedName = (_runningProcessPicker.SelectedItem as RunningProcessChoice) == null
                ? null
                : ((RunningProcessChoice)_runningProcessPicker.SelectedItem).ProcessName;
            Dictionary<string, RunningProcessChoice> choices =
                new Dictionary<string, RunningProcessChoice>(StringComparer.OrdinalIgnoreCase);
            int currentProcessId;
            using (Process currentProcess = Process.GetCurrentProcess())
                currentProcessId = currentProcess.Id;

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == currentProcessId)
                            continue;

                        string processName = process.ProcessName;
                        if (String.IsNullOrWhiteSpace(processName))
                            continue;

                        IntPtr windowHandle = process.MainWindowHandle;
                        if (windowHandle == IntPtr.Zero)
                            continue;

                        string windowTitle = process.MainWindowTitle.Trim();
                        RunningProcessChoice choice;

                        if (!choices.TryGetValue(processName, out choice))
                        {
                            choice = new RunningProcessChoice(processName);
                            choices.Add(processName, choice);
                        }
                        choice.InstanceCount++;
                        if (!String.IsNullOrWhiteSpace(windowTitle) &&
                            (String.IsNullOrWhiteSpace(choice.WindowTitle) ||
                             StringComparer.OrdinalIgnoreCase.Compare(windowTitle, choice.WindowTitle) < 0))
                        {
                            choice.WindowTitle = windowTitle;
                        }
                    }
                    catch (Win32Exception)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            List<RunningProcessChoice> sortedChoices = choices.Values
                .OrderBy(delegate(RunningProcessChoice choice) { return choice.ProcessName; }, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            bool hasSameItems = _runningProcessPicker.Items.Count == sortedChoices.Count &&
                _runningProcessPicker.Items.Cast<object>().Select(delegate(object item) { return item.ToString(); })
                    .SequenceEqual(sortedChoices.Select(delegate(RunningProcessChoice choice) { return choice.ToString(); }));
            if (hasSameItems)
            {
                _runningProcessPicker.Invalidate();
                _addSelectedButton.Enabled = _runningProcessPicker.SelectedItem is RunningProcessChoice &&
                    !_processNames.Contains(((RunningProcessChoice)_runningProcessPicker.SelectedItem).ProcessName, StringComparer.OrdinalIgnoreCase);
                _addSelectedButton.Invalidate();
                _processPickerErrorShown = false;
                return;
            }

            _runningProcessPicker.BeginUpdate();
            try
            {
                _runningProcessPicker.Items.Clear();
                if (sortedChoices.Count == 0)
                {
                    _runningProcessPicker.Items.Add("No running apps found");
                    _runningProcessPicker.SelectedIndex = 0;
                }
                else
                {
                    foreach (RunningProcessChoice choice in sortedChoices)
                        _runningProcessPicker.Items.Add(choice);

                    int selectedIndex = selectedName == null
                        ? -1
                        : sortedChoices.FindIndex(delegate(RunningProcessChoice choice)
                        {
                            return String.Equals(choice.ProcessName, selectedName, StringComparison.OrdinalIgnoreCase);
                        });
                    _runningProcessPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                }
            }
            finally
            {
                _runningProcessPicker.EndUpdate();
            }

            _processPickerErrorShown = false;
        }

        private void UpdateProcessColumns()
        {
            if (_processList.Columns.Count != 3)
                return;

            int removeColumnWidth = 64;
            int availableWidth = Math.Max(0, _processList.ClientSize.Width - removeColumnWidth);
            int processColumnWidth = availableWidth * 2 / 5;
            int statusColumnWidth = availableWidth - processColumnWidth;
            if (_processList.Columns[0].Width != processColumnWidth)
                _processList.Columns[0].Width = processColumnWidth;
            if (_processList.Columns[1].Width != statusColumnWidth)
                _processList.Columns[1].Width = statusColumnWidth;
            if (_processList.Columns[2].Width != removeColumnWidth)
                _processList.Columns[2].Width = removeColumnWidth;
            _processList.HideHorizontalScrollBar();
        }

        private void UpdateDeleteButtonCursor(object sender, MouseEventArgs args)
        {
            ListViewHitTestInfo hit = _processList.HitTest(args.Location);
            _processList.Cursor = hit.Item != null &&
                hit.SubItem != null &&
                hit.Item.SubItems.IndexOf(hit.SubItem) == 2 &&
                GetDeleteButtonBounds(GetRowContentBounds(hit.SubItem.Bounds)).Contains(args.Location)
                    ? Cursors.Hand
                    : Cursors.Default;
        }

        private static Rectangle GetRowContentBounds(Rectangle cellBounds)
        {
            return new Rectangle(
                cellBounds.Left,
                cellBounds.Top + 2,
                cellBounds.Width,
                Math.Max(0, cellBounds.Height - 4));
        }

        private static Rectangle GetDeleteButtonBounds(Rectangle cellBounds)
        {
            return new Rectangle(
                cellBounds.Left + (cellBounds.Width - 48) / 2,
                cellBounds.Top + (cellBounds.Height - 18) / 2,
                48,
                18);
        }

        private void RemoveProcessFromRow(object sender, MouseEventArgs args)
        {
            ListViewHitTestInfo hit = _processList.HitTest(args.Location);
            if (hit.Item == null || hit.SubItem == null ||
                hit.Item.SubItems.IndexOf(hit.SubItem) != 2)
            {
                return;
            }

            if (!GetDeleteButtonBounds(GetRowContentBounds(hit.SubItem.Bounds)).Contains(args.Location))
                return;

            RemoveTrackedProcess(Convert.ToString(hit.Item.Tag));
        }

        private void DrawProcessItem(object sender, DrawListViewItemEventArgs args)
        {
            if (_processList.View != View.Details)
            {
                args.DrawDefault = true;
                return;
            }

            Color rowColor = args.Item.Selected
                ? Color.FromArgb(38, 45, 75)
                : (args.ItemIndex % 2 == 0 ? Color.FromArgb(16, 24, 40) : SurfaceColor);
            Rectangle rowBounds = GetRowContentBounds(
                _processList.GetItemRect(args.ItemIndex, ItemBoundsPortion.Entire));
            using (SolidBrush background = new SolidBrush(rowColor))
                args.Graphics.FillRectangle(background, rowBounds);
        }

        private void DrawProcessSubItem(object sender, DrawListViewSubItemEventArgs args)
        {
            Color rowColor = args.Item.Selected
                ? Color.FromArgb(38, 45, 75)
                : (args.ItemIndex % 2 == 0 ? Color.FromArgb(16, 24, 40) : SurfaceColor);
            using (SolidBrush background = new SolidBrush(rowColor))
                args.Graphics.FillRectangle(background, args.Bounds);

            Rectangle rowBounds = GetRowContentBounds(args.Bounds);

            if (args.ColumnIndex == 2)
            {
                Rectangle deleteBounds = GetDeleteButtonBounds(rowBounds);
                using (GraphicsPath path = RoundedRectangle(deleteBounds, 5))
                using (SolidBrush deleteBackground = new SolidBrush(
                    args.Item.Selected ? Color.FromArgb(95, 48, 62) : Color.FromArgb(47, 31, 48)))
                using (SolidBrush deleteText = new SolidBrush(Color.FromArgb(255, 139, 158)))
                using (Font deleteFont = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point))
                {
                    args.Graphics.FillPath(deleteBackground, path);
                    TextRenderer.DrawText(
                        args.Graphics,
                        "Delete",
                        deleteFont,
                        deleteBounds,
                        deleteText.Color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                return;
            }

            if (args.ColumnIndex == 0)
            {
                TextRenderer.DrawText(
                    args.Graphics,
                    args.SubItem.Text,
                    Font,
                    new Rectangle(rowBounds.Left + 12, rowBounds.Top, Math.Max(0, rowBounds.Width - 20), rowBounds.Height),
                    TextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }

            Color statusColor = GetStatusColor(args.SubItem.Text);
            using (Font statusFont = new Font(Font.FontFamily, 8.5F, FontStyle.Regular, GraphicsUnit.Point))
            {
                Size textSize = TextRenderer.MeasureText(args.SubItem.Text, statusFont);
                int badgeHeight = Math.Min(20, rowBounds.Height);
                Rectangle badge = new Rectangle(
                    rowBounds.Left + 8,
                    rowBounds.Top + (rowBounds.Height - badgeHeight) / 2,
                    Math.Min(rowBounds.Width - 12, textSize.Width + 14),
                    badgeHeight);

                using (GraphicsPath path = RoundedRectangle(badge, 7))
                using (SolidBrush badgeBackground = new SolidBrush(Color.FromArgb(30, statusColor)))
                {
                    args.Graphics.FillPath(badgeBackground, path);
                    TextRenderer.DrawText(
                        args.Graphics,
                        args.SubItem.Text,
                        statusFont,
                        Rectangle.Inflate(badge, -7, 0),
                        statusColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private static Color GetStatusColor(string status)
        {
            if (status.Equals("Not running", StringComparison.OrdinalIgnoreCase))
                return MutedTextColor;
            if (status.IndexOf("out of focus", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromArgb(255, 190, 110);
            if (status.IndexOf("muted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf("minimized", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromArgb(255, 171, 106);
            if (status.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromArgb(100, 213, 170);
            if (status.IndexOf("running", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromArgb(116, 169, 255);
            return MutedTextColor;
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void SetDarkTitleBar()
        {
            int enabled = 1;
            int result = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            if (result != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr eventHookModule,
            WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr eventHook);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        private delegate void WinEventDelegate(
            IntPtr eventHook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime);

        private void InitializeTray()
        {
            ToolStripMenuItem openItem = new ToolStripMenuItem("Open");
            openItem.Click += delegate { ShowMainWindow(); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { _exitRequested = true; Close(); };

            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add(exitItem);

            _trayIcon.Text = "Deafen";
            _trayIcon.Icon = _appIcon;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.MouseDoubleClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left)
                    ShowMainWindow();
            };
        }

        private void InitializeStartupOption()
        {
            try
            {
                _updatingOptions = true;
                _startupCheckBox.Checked = IsStartupEnabled();
                if (_startupCheckBox.Checked)
                {
                    UpdateStartupCommand();
                    RemoveLegacyStartupCommand();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Could not read the Windows startup setting:\r\n" + exception.Message,
                    "Deafen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                _updatingOptions = false;
            }
        }

        private void InitializeCloseOption()
        {
            _updatingOptions = true;
            _minimizeToTrayCheckBox.Checked = MinimizeToTrayOnClose;
            _updatingOptions = false;
        }

        private bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                return key != null &&
                    (key.GetValue(StartupValueName, null) != null ||
                     key.GetValue(LegacyStartupValueName, null) != null);
        }

        private static void UpdateStartupCommand()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
            {
                if (key == null)
                    throw new InvalidOperationException("Could not open the current user's startup registry key.");

                key.SetValue(
                    StartupValueName,
                    "\"" + Application.ExecutablePath + "\" --minimized",
                    RegistryValueKind.String);
            }
        }

        private static void RemoveLegacyStartupCommand()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
            {
                if (key != null)
                    key.DeleteValue(LegacyStartupValueName, false);
            }
        }

        private void ChangeStartupOption(object sender, EventArgs args)
        {
            if (_updatingOptions)
                return;

            bool requestedState = _startupCheckBox.Checked;
            try
            {
                using (RegistryKey key = requestedState
                    ? Registry.CurrentUser.CreateSubKey(StartupRegistryPath)
                    : Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
                {
                    if (key == null && requestedState)
                        throw new InvalidOperationException("Could not open the current user's startup registry key.");

                    if (requestedState)
                        key.SetValue(
                            StartupValueName,
                            "\"" + Application.ExecutablePath + "\" --minimized",
                            RegistryValueKind.String);
                    else if (key != null)
                    {
                        key.DeleteValue(StartupValueName, false);
                        key.DeleteValue(LegacyStartupValueName, false);
                    }
                }
                if (requestedState)
                    RemoveLegacyStartupCommand();
            }
            catch (Exception exception)
            {
                _updatingOptions = true;
                _startupCheckBox.Checked = !requestedState;
                _updatingOptions = false;
                MessageBox.Show(
                    "Could not change the Windows startup setting:\r\n" + exception.Message,
                    "Deafen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ChangeCloseOption(object sender, EventArgs args)
        {
            if (_updatingOptions)
                return;

            bool previousState = MinimizeToTrayOnClose;
            MinimizeToTrayOnClose = _minimizeToTrayCheckBox.Checked;
            try
            {
                SaveSettings();
            }
            catch (Exception exception)
            {
                MinimizeToTrayOnClose = previousState;
                _updatingOptions = true;
                _minimizeToTrayCheckBox.Checked = previousState;
                _updatingOptions = false;
                MessageBox.Show(
                    "Could not save the close behavior setting:\r\n" + exception.Message,
                    "Deafen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string NormalizeProcessName(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Enter a process name, for example Game.exe.");

            string name = value.Trim();
            if (!String.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
                throw new ArgumentException("Enter a file name, not a path.");

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("The process name contains invalid characters.");

            return name;
        }

        private void AddTrackedProcess(string processName)
        {
            if (String.IsNullOrWhiteSpace(processName))
                return;

            try
            {
                string name = NormalizeProcessName(processName);
                if (_processNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("This process is already in the list.");

                _processNames.Add(name);
                try
                {
                    SaveSettings();
                }
                catch
                {
                    _processNames.Remove(name);
                    throw;
                }

                TryRefreshRunningProcesses();
                RefreshAudioState();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Could not add process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void RemoveTrackedProcess(string processName)
        {
            if (String.IsNullOrWhiteSpace(processName))
                return;

            try
            {
                string name = NormalizeProcessName(processName);
                int index = _processNames.FindIndex(delegate(string item)
                {
                    return String.Equals(item, name, StringComparison.OrdinalIgnoreCase);
                });
                if (index < 0)
                    return;

                HashSet<int> removedProcessIds = GetProcessIds(name);
                AudioSessionController.Apply(new HashSet<int>(), removedProcessIds);
                _processNames.RemoveAt(index);
                try
                {
                    SaveSettings();
                }
                catch
                {
                    _processNames.Insert(index, name);
                    throw;
                }

                TryRefreshRunningProcesses();
                RefreshAudioState();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Could not remove process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RefreshAudioState()
        {
            if (_isRefreshing)
                return;

            _isRefreshing = true;
            HashSet<int> mutedProcessIds = new HashSet<int>();
            HashSet<int> trackedProcessIds = new HashSet<int>();
            try
            {
                int foregroundProcessId = AudioSessionController.GetForegroundProcessId();
                Dictionary<string, string> desiredStatuses =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in _processNames)
                {
                    Process[] running = Process.GetProcessesByName(name);
                    int windowCount = 0;
                    int minimizedCount = 0;
                    int backgroundCount = 0;

                    foreach (Process process in running)
                    {
                        using (process)
                        {
                            try
                            {
                                trackedProcessIds.Add(process.Id);
                                IntPtr windowHandle = process.MainWindowHandle;
                                if (windowHandle == IntPtr.Zero)
                                    continue;

                                windowCount++;
                                bool isMinimized = AudioSessionController.IsIconic(windowHandle);
                                bool isInBackground = process.Id != foregroundProcessId;
                                if (isMinimized)
                                    minimizedCount++;
                                else if (isInBackground)
                                    backgroundCount++;

                                if (isMinimized || isInBackground)
                                    mutedProcessIds.Add(process.Id);
                            }
                            catch (InvalidOperationException)
                            {
                            }
                            catch (Win32Exception)
                            {
                            }
                        }
                    }

                    string status;
                    if (running.Length == 0)
                        status = "Not running";
                    else if (windowCount == 0)
                        status = "Running; no window found";
                    else if (minimizedCount == windowCount)
                        status = "Minimized";
                    else if (backgroundCount == windowCount)
                        status = "Out of focus — muted";
                    else if (backgroundCount > 0 || minimizedCount > 0)
                        status = "Partially out of focus";
                    else
                        status = "In focus";

                    desiredStatuses[name] = status;
                }

                AudioSessionController.Apply(mutedProcessIds, trackedProcessIds);
                UpdateProcessList(desiredStatuses);
                _monitoringErrorShown = false;
            }
            catch (Exception exception)
            {
                string errorMessage = "Audio monitoring failed:\r\n" + exception.Message;

                try
                {
                    AudioSessionController.Apply(new HashSet<int>(), trackedProcessIds);
                }
                catch (Exception restoreException)
                {
                    errorMessage += "\r\nCould not enable tracked application audio: " + restoreException.Message;
                }

                if (!_monitoringErrorShown)
                {
                    MessageBox.Show(
                        errorMessage,
                        "Deafen monitoring error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    _monitoringErrorShown = true;
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void UpdateProcessList(Dictionary<string, string> desiredStatuses)
        {
            bool rebuild = _processList.Items.Count != _processNames.Count;
            bool needsUpdate = rebuild;
            if (!rebuild)
            {
                for (int index = 0; index < _processNames.Count; index++)
                {
                    ListViewItem item = _processList.Items[index];
                    string name = _processNames[index];
                    string status;
                    if (!String.Equals(Convert.ToString(item.Tag), name, StringComparison.OrdinalIgnoreCase))
                    {
                        rebuild = true;
                        needsUpdate = true;
                        break;
                    }

                    if (!desiredStatuses.TryGetValue(name, out status) ||
                        !String.Equals(item.SubItems[1].Text, status, StringComparison.Ordinal))
                        needsUpdate = true;
                }
            }

            if (!needsUpdate)
                return;

            int previousTopIndex = 0;
            if (_processList.Items.Count > 0)
            {
                ListViewItem topItem = _processList.TopItem;
                if (topItem != null)
                    previousTopIndex = topItem.Index;
            }

            _processList.BeginUpdate();
            try
            {
                if (rebuild)
                {
                    _processList.Items.Clear();
                    foreach (string name in _processNames)
                    {
                        ListViewItem item = new ListViewItem(name + ".exe");
                        item.Tag = name;
                        item.ImageIndex = 0;
                        item.SubItems.Add(desiredStatuses[name]);
                        item.SubItems.Add(String.Empty);
                        _processList.Items.Add(item);
                    }
                }
                else
                {
                    for (int index = 0; index < _processNames.Count; index++)
                        _processList.Items[index].SubItems[1].Text = desiredStatuses[_processNames[index]];
                }
            }
            finally
            {
                _processList.EndUpdate();
            }

            if (rebuild && _processList.IsHandleCreated)
            {
                UpdateProcessColumns();
                _processList.BeginInvoke(new MethodInvoker(UpdateProcessColumns));
            }

            if (_processList.Items.Count > 0)
            {
                int topIndex = Math.Min(previousTopIndex, _processList.Items.Count - 1);
                _processList.Items[topIndex].EnsureVisible();
            }
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            SetForegroundWindow(Handle);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Program.ShowMainWindowMessage)
            {
                ShowMainWindow();
                return;
            }

            base.WndProc(ref message);
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs args)
        {
            if (MinimizeToTrayOnClose && !_exitRequested)
            {
                args.Cancel = true;
                Hide();

                if (!_trayNotificationShown)
                {
                    _trayIcon.ShowBalloonTip(
                        2500,
                        "Deafen",
                        "The application is still running in the system tray.",
                        ToolTipIcon.Info);
                    _trayNotificationShown = true;
                }

                return;
            }

            _timer.Stop();
            StopWindowEventHooks();
            AudioSessionController.SessionCreated -= HandleAudioSessionCreated;
            string shutdownError = null;
            try
            {
                AudioSessionController.StopSessionNotifications();
            }
            catch (Exception exception)
            {
                shutdownError = "Could not stop audio-session notifications:\r\n" + exception.Message;
            }

            try
            {
                AudioSessionController.Apply(new HashSet<int>(), GetTrackedProcessIds());
            }
            catch (Exception exception)
            {
                string audioError = "Could not enable audio for tracked applications:\r\n" + exception.Message;
                shutdownError = shutdownError == null
                    ? audioError
                    : shutdownError + "\r\n\r\n" + audioError;
            }

            if (shutdownError != null)
                MessageBox.Show(shutdownError, "Deafen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private HashSet<int> GetTrackedProcessIds()
        {
            HashSet<int> processIds = new HashSet<int>();
            foreach (string name in _processNames)
                processIds.UnionWith(GetProcessIds(name));
            return processIds;
        }

        private static HashSet<int> GetProcessIds(string processName)
        {
            HashSet<int> processIds = new HashSet<int>();
            Process[] processes = Process.GetProcessesByName(processName);
            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        processIds.Add(process.Id);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (Win32Exception)
                    {
                    }
                }
            }

            return processIds;
        }

        private void HandleFormClosed(object sender, FormClosedEventArgs args)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _timer.Dispose();
            _processRowHeightImages.Dispose();
            _processRowHeightImage.Dispose();
            _appIcon.Dispose();
        }

        private static Icon CreateAppIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush background = new SolidBrush(Color.FromArgb(30, 45, 80)))
            using (Brush speaker = new SolidBrush(Color.White))
            using (Pen mute = new Pen(Color.FromArgb(255, 95, 105), 3.2f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(background, 1, 1, 30, 30);
                graphics.FillPolygon(speaker, new[]
                {
                    new Point(5, 12),
                    new Point(10, 12),
                    new Point(17, 7),
                    new Point(17, 25),
                    new Point(10, 20),
                    new Point(5, 20)
                });
                graphics.DrawLine(mute, 19, 11, 26, 21);
                graphics.DrawLine(mute, 26, 11, 19, 21);

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(iconHandle).Clone();
                }
                finally
                {
                    AudioSessionController.DestroyIconHandle(iconHandle);
                }
            }
        }

        private sealed class AppSettings
        {
            public AppSettings()
            {
                MinimizeToTray = true;
            }

            public string[] Processes { get; set; }
            public bool? MinimizeToTray { get; set; }
        }

        private sealed class RunningProcessChoice
        {
            public RunningProcessChoice(string processName)
            {
                ProcessName = processName;
            }

            public string ProcessName { get; private set; }
            public string WindowTitle { get; set; }
            public int InstanceCount { get; set; }

            public override string ToString()
            {
                string title = String.IsNullOrWhiteSpace(WindowTitle) ? String.Empty : " — " + WindowTitle;
                string count = InstanceCount > 1 ? " (" + InstanceCount + ")" : String.Empty;
                return ProcessName + ".exe" + title + count;
            }
        }
    }
}
