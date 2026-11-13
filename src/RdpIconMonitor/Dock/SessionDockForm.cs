using RdpIconMonitor.Rendering;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Dock;

internal sealed class SessionDockForm : Form
{
    private const int HeaderHeight = 22;
    private const int RowHeight = 34;
    private const int RowGap = 2;
    private const int MinActionWidth = 150;
    private const int MinimizeWidth = 28;
    private const int OuterMargin = 4;
    private readonly Func<WindowIdentity, bool> activate;
    private readonly Func<WindowIdentity, bool> minimize;
    private readonly List<(Button Action, Button Minimize)> rows = [];
    private readonly ToolTip toolTip = new();
    private readonly Panel headerPanel = new()
    {
        BackColor = Color.FromArgb(24, 24, 24),
        Dock = DockStyle.Top
    };
    private readonly Label header = new()
    {
        AutoEllipsis = true,
        Font = new("Segoe UI", 9.5f, FontStyle.Bold),
        ForeColor = Color.FromArgb(205, 211, 216),
        Padding = new(6, 0, 4, 0),
        Text = "RDP SESSIONS",
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button toggle = new()
    {
        FlatStyle = FlatStyle.Flat,
        Font = new("Segoe UI", 10f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        UseVisualStyleBackColor = false
    };
    private Rectangle? previousWorkingArea;
    private SessionDockScreenState? currentState;
    private bool collapsed;
    private bool dragging;
    private bool dragMoved;
    private bool userPositioned;
    private Point dragCursorOffset;

    public SessionDockForm(
        Func<WindowIdentity, bool> activate,
        Func<WindowIdentity, bool> minimize)
    {
        this.activate = activate;
        this.minimize = minimize;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(24, 24, 24);
        Font = new("Segoe UI", 10f, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        header.Cursor = Cursors.SizeAll;
        toolTip.SetToolTip(header, "Drag to move the session dock");
        header.MouseDown += StartDrag;
        header.MouseMove += MoveDrag;
        header.MouseUp += StopDrag;
        header.MouseCaptureChanged += (_, _) =>
        {
            if (dragging && !header.Capture) FinishDrag();
        };
        toggle.BackColor = Color.FromArgb(43, 47, 51);
        toggle.FlatAppearance.BorderColor = Color.FromArgb(75, 81, 87);
        toggle.FlatAppearance.BorderSize = 1;
        toggle.ForeColor = Color.White;
        toggle.Click += (_, _) => ToggleCollapsed();
        headerPanel.Controls.Add(header);
        headerPanel.Controls.Add(toggle);
        Controls.Add(headerPanel);
        UpdateToggle();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080;
            parameters.ExStyle |= 0x08000000;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (currentState is not null)
        {
            LayoutRows(currentState);
            Position(currentState);
        }
    }

    public void Update(SessionDockScreenState state)
    {
        currentState = state;
        SuspendLayout();
        try
        {
            RebuildRows(state);
            LayoutRows(state);
            Position(state);
            if (state.Sessions.Count == 0)
            {
                Visible = false;
                return;
            }
            if (!Visible) Show();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (currentState is not null)
        {
            LayoutRows(currentState);
            Position(currentState);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) toolTip.Dispose();
        base.Dispose(disposing);
    }

    private void RebuildRows(SessionDockScreenState state)
    {
        if (rows.Count == state.Sessions.Count) return;
        foreach (var row in rows)
        {
            row.Action.Dispose();
            row.Minimize.Dispose();
        }
        rows.Clear();
        for (int index = 0; index < state.Sessions.Count; index++)
        {
            var action = new Button
            {
                AutoEllipsis = true,
                FlatStyle = FlatStyle.Flat,
                Font = Font,
                Height = RowHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false
            };
            action.FlatAppearance.BorderSize = 1;
            action.Click += (_, _) =>
            {
                if (action.Tag is WindowIdentity identity) activate(identity);
            };
            var minimizeButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new("Segoe UI", 10f, FontStyle.Bold),
                Height = RowHeight,
                Text = "_",
                TextAlign = ContentAlignment.TopCenter,
                UseVisualStyleBackColor = false,
                Width = MinimizeWidth
            };
            minimizeButton.FlatAppearance.BorderColor = Color.FromArgb(75, 81, 87);
            minimizeButton.FlatAppearance.BorderSize = 1;
            minimizeButton.Click += (_, _) =>
            {
                if (minimizeButton.Tag is WindowIdentity identity) minimize(identity);
            };
            toolTip.SetToolTip(minimizeButton, "Minimize this session");
            minimizeButton.AccessibleName = "Minimize session";
            rows.Add((action, minimizeButton));
            Controls.Add(action);
            Controls.Add(minimizeButton);
        }
    }

    private void LayoutRows(SessionDockScreenState state)
    {
        int desiredActionWidth = Math.Max(
            Scale(MinActionWidth),
            state.Sessions.Count == 0
                ? Scale(MinActionWidth)
                : state.Sessions.Max(session =>
                    TextRenderer.MeasureText((session.Current ? "\u25cf " : "") + session.Label, Font).Width + Scale(20)));
        int margin = Scale(OuterMargin);
        int availableActionWidth = Math.Max(
            1,
            state.WorkingArea.Width - Scale(MinimizeWidth) - margin * 3);
        int actionWidth = Math.Min(desiredActionWidth, availableActionWidth);
        int rowHeight = Scale(RowHeight);
        int headerHeight = Scale(HeaderHeight);
        int width = actionWidth + Scale(MinimizeWidth) + margin * 3;
        int height = collapsed
            ? headerHeight
            : headerHeight + margin * 2 + state.Sessions.Count * rowHeight +
                Math.Max(0, state.Sessions.Count - 1) * Scale(RowGap);
        headerPanel.Height = headerHeight;
        header.Height = headerHeight;
        toggle.Width = Scale(MinimizeWidth);
        toggle.Height = headerHeight;
        toggle.Location = new(width - toggle.Width, 0);
        header.Location = Point.Empty;
        header.Width = width - toggle.Width;
        ClientSize = new(width, Math.Max(headerHeight, height));

        for (int index = 0; index < state.Sessions.Count; index++)
        {
            var session = state.Sessions[index];
            var (action, minimizeButton) = rows[index];
            var baseColor = session.Hue is double hue
                ? ColorTranslator.FromHtml(OklchColor.Rotate("#0078D4", hue))
                : Color.FromArgb(70, 86, 100);
            int top = headerHeight + margin + index * (rowHeight + Scale(RowGap));
            action.Tag = session.Identity;
            action.Text = session.Current ? $"\u25cf {session.Label}" : session.Label;
            action.BackColor = baseColor;
            action.ForeColor = Color.White;
            action.FlatAppearance.BorderColor = session.Current
                ? Color.White
                : Color.FromArgb(75, 81, 87);
            action.FlatAppearance.BorderSize = session.Current ? 2 : 1;
            action.Width = actionWidth;
            action.Height = rowHeight;
            action.Location = new(margin, top);
            action.Visible = !collapsed;
            action.AccessibleName = $"Activate {session.Label}";
            toolTip.SetToolTip(action, session.Label);

            minimizeButton.Tag = session.Identity;
            minimizeButton.BackColor = Color.FromArgb(43, 47, 51);
            minimizeButton.ForeColor = Color.White;
            minimizeButton.Width = Scale(MinimizeWidth);
            minimizeButton.Height = rowHeight;
            minimizeButton.Location = new(action.Right + margin, top);
            minimizeButton.Visible = !collapsed;
            minimizeButton.AccessibleName = $"Minimize {session.Label}";
        }
    }

    private int Scale(int logicalPixels) =>
        (logicalPixels * DeviceDpi + 48) / 96;

    private void Position(SessionDockScreenState state)
    {
        int margin = Scale(OuterMargin);
        if (dragging)
        {
            Location = Clamp(Location, state.WorkingArea, Size);
        }
        else if (!userPositioned)
        {
            Location = DefaultLocation(state.WorkingArea, Size, margin);
        }
        else if (previousWorkingArea is Rectangle oldWorkingArea &&
            oldWorkingArea != state.WorkingArea)
        {
            Location = Clamp(new(
                state.WorkingArea.X + Left - oldWorkingArea.X,
                state.WorkingArea.Y + Top - oldWorkingArea.Y), state.WorkingArea, Size);
        }
        else
        {
            Location = Clamp(Location, state.WorkingArea, Size);
        }
        previousWorkingArea = state.WorkingArea;
    }

    internal static Point DefaultLocation(Rectangle workingArea, Size size, int margin) =>
        Clamp(new(
            workingArea.Right - size.Width - margin,
            workingArea.Bottom - size.Height - margin), workingArea, size);

    internal static Point Clamp(Point desired, Rectangle workingArea, Size size) => new(
        Math.Clamp(desired.X, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - size.Width)),
        Math.Clamp(desired.Y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - size.Height)));

    internal static Point BottomAlignedLocation(
        Rectangle previousBounds,
        Size newSize,
        Rectangle workingArea) =>
        Clamp(new(previousBounds.Left, previousBounds.Bottom - newSize.Height), workingArea, newSize);

    private void StartDrag(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        Point cursor = header.PointToScreen(args.Location);
        dragCursorOffset = new(cursor.X - Left, cursor.Y - Top);
        dragging = true;
        dragMoved = false;
        header.Capture = true;
    }

    private void MoveDrag(object? sender, MouseEventArgs args)
    {
        if (!dragging || currentState is null || args.Button != MouseButtons.Left) return;
        Point cursor = Control.MousePosition;
        Point desired = new(cursor.X - dragCursorOffset.X, cursor.Y - dragCursorOffset.Y);
        Point clamped = Clamp(desired, currentState.WorkingArea, Size);
        if (clamped != Location)
        {
            Location = clamped;
            dragMoved = true;
        }
    }

    private void StopDrag(object? sender, MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Left) FinishDrag();
    }

    private void FinishDrag()
    {
        if (!dragging) return;
        dragging = false;
        if (dragMoved) userPositioned = true;
        if (header.Capture) header.Capture = false;
    }

    private void ToggleCollapsed()
    {
        Rectangle previousBounds = Bounds;
        collapsed = !collapsed;
        UpdateToggle();
        if (currentState is null) return;
        LayoutRows(currentState);
        Location = BottomAlignedLocation(previousBounds, Size, currentState.WorkingArea);
    }

    private void UpdateToggle()
    {
        toggle.Text = collapsed ? "+" : "-";
        toggle.AccessibleName = collapsed ? "Expand session dock" : "Collapse session dock";
        toolTip.SetToolTip(toggle, collapsed ? "Expand session dock" : "Collapse session dock");
    }
}
