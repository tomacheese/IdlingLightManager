#nullable enable
using System.Reflection;

namespace IdlingLightManager.UI;

partial class MainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer? components = null;

    /// <summary>Application icon, shared by the form and the NotifyIcon.</summary>
    private Icon? _appIcon;

    private ListView logView = null!;
    private Button button1 = null!;
    private Button button2 = null!;
    private Button button3 = null!;
    private NotifyIcon notifyIcon = null!;
    private ContextMenuStrip trayContextMenu = null!;
    private ToolStripMenuItem menuItemOpen = null!;
    private ToolStripMenuItem menuItemPcStart = null!;
    private ToolStripMenuItem menuItemPcStop = null!;
    private ToolStripSeparator menuItemSeparator = null!;
    private ToolStripMenuItem menuItemExit = null!;

    /// <summary>
    /// Gets the ListView that displays the log.
    /// </summary>
    public ListView LogView => logView;

    /// <summary>
    /// Cleans up any resources being used.
    /// </summary>
    /// <param name="disposing"><c>true</c> to dispose managed resources; otherwise <c>false</c>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        if (disposing)
        {
            _appIcon?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Method required for designer support.
    /// Do not modify the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // Load the icon embedded in the assembly
        using var iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IdlingLightManager.Resources.app.ico")
            ?? throw new InvalidOperationException(
                "Embedded resource 'IdlingLightManager.Resources.app.ico' was not found.");
        _appIcon = new Icon(iconStream);

        // --- LogView (ListView) ---
        logView = new ListView();
        logView.Size = new Size(797, 407);
        logView.Location = new Point(1, -3);
        logView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logView.View = View.Details;
        logView.HideSelection = false;
        logView.FullRowSelect = true;
        logView.Columns.Add("Time", 154);
        logView.Columns.Add("Message", 636);

        // --- button1 (PCStart) ---
        button1 = new Button();
        button1.Size = new Size(266, 47);
        button1.Location = new Point(1, 403);
        button1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        button1.Text = "PCStart";
        button1.Click += OnPcStartClick;

        // --- button2 (PCStop) ---
        button2 = new Button();
        button2.Size = new Size(252, 47);
        button2.Location = new Point(273, 403);
        button2.Anchor = AnchorStyles.Bottom;
        button2.Text = "PCStop";
        button2.Click += OnPcStopClick;

        // --- button3 (Exit) ---
        button3 = new Button();
        button3.Size = new Size(267, 47);
        button3.Location = new Point(531, 403);
        button3.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        button3.Text = "Exit";
        button3.Click += OnExitClick;

        // --- Tray icon context menu ---
        menuItemOpen = new ToolStripMenuItem("開く");
        menuItemOpen.Click += OnMenuItemOpenClick;
        menuItemPcStart = new ToolStripMenuItem("照明 ON (PCStart)");
        menuItemPcStart.Click += OnPcStartClick;
        menuItemPcStop = new ToolStripMenuItem("照明 OFF (PCStop)");
        menuItemPcStop.Click += OnPcStopClick;
        menuItemSeparator = new ToolStripSeparator();
        menuItemExit = new ToolStripMenuItem("終了");
        menuItemExit.Click += OnExitClick;

        trayContextMenu = new ContextMenuStrip(components);
        trayContextMenu.Items.AddRange([menuItemOpen, menuItemPcStart, menuItemPcStop, menuItemSeparator, menuItemExit]);

        // --- notifyIcon ---
        notifyIcon = new NotifyIcon(components);
        notifyIcon.Text = "IdlingLightManager";
        notifyIcon.Visible = true;
        notifyIcon.Icon = _appIcon;
        notifyIcon.ContextMenuStrip = trayContextMenu;
        notifyIcon.MouseDoubleClick += OnNotifyIconMouseDoubleClick;

        // --- Form settings ---
        ClientSize = new Size(800, 450);
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Text = "IdlingLightManager";
        Icon = _appIcon;

        Controls.Add(logView);
        Controls.Add(button1);
        Controls.Add(button2);
        Controls.Add(button3);

        // --- Event wiring ---
        this.Load += OnMainFormLoad;
        this.Resize += OnMainFormResize;
    }
}
