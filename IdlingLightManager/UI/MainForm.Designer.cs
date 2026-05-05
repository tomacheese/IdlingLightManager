#nullable enable
using System.Reflection;

namespace IdlingLightManager.UI;

partial class MainForm
{
    /// <summary>
    /// 使用中のデザイナー変数。
    /// </summary>
    private System.ComponentModel.IContainer? components = null;

    /// <summary>アプリケーションアイコン。フォームと NotifyIcon で共用する。</summary>
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
    /// ログを表示する ListView。
    /// </summary>
    public ListView LogView => logView;

    /// <summary>
    /// 使用中のリソースをすべてクリーンアップする。
    /// </summary>
    /// <param name="disposing">マネージリソースを破棄する場合は <c>true</c>、それ以外は <c>false</c>。</param>
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
    /// デザイナーで生成されたコードでサポートされているメソッド。
    /// このメソッドの内容をコードエディターで変更しないでください。
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // アセンブリに埋め込まれたアイコンをロードする
        using var iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IdlingLightManager.Resources.app.ico")!;
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

        // --- トレイアイコンの右クリックメニュー ---
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

        // --- フォーム本体の設定 ---
        ClientSize = new Size(800, 450);
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Text = "IdlingLightManager";
        Icon = _appIcon;

        Controls.Add(logView);
        Controls.Add(button1);
        Controls.Add(button2);
        Controls.Add(button3);

        // --- イベント接続 ---
        this.Load += OnMainFormLoad;
        this.Resize += OnMainFormResize;
    }
}
