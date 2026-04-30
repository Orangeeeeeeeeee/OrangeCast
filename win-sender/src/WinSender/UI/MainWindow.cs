using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Networking.NetworkOperators;
using WinSender.Abr;
using WinSender.Audio;
using WinSender.Capture;
using WinSender.Diagnostics;
using WinSender.Discovery;
using WinSender.Settings;
using WinSender.Signaling;
using WinSender.UI.Controls;
using WinSender.WebRTC;

namespace WinSender.UI;

public partial class MainWindow : Form
{
    private AppState _state = AppState.Idle;

    private readonly Panel        _titleBar;
    private readonly StatusIndicator _statusIcon;
    private readonly Label        _lblHero;
    private readonly Label        _lblHeroSub;
    private readonly Label        _lblSectionLabel;
    private readonly Label        _lblSectionCount;
    private readonly RoundedButton _btnRefresh;
    private readonly FlowLayoutPanel _deviceList;
    private readonly IpInputBar   _ipInputBar;
    private readonly Label        _lblFooter;
    private readonly ToggleSwitch _toggleHotspot;
    private readonly Label        _lblHotspot;

    private DeviceCard? _currentCard;

    private readonly MdnsDiscoverer    _discoverer = new();
    private readonly TrustedDeviceStore _trustStore = new();
    private SignalingClient? _client;
    private WebRtcSender?    _sender;
    private ScreenCapture?   _screenCapture;

    private string  _currentTarget = "";
    private string? _currentDeviceId;
    private readonly System.Windows.Forms.Timer _castingTimer;
    private DateTime _castingStartTime;
    private CancellationTokenSource? _reconnectCts;
    private int _reconnecting;
    private bool _userInitiatedDisconnect;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    private NotifyIcon? _trayIcon;
    private bool _reallyExit;

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();

    public MainWindow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(720, 640);
        Size = new Size(880, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        Text = "橙子投屏";
        Font = Theme.Body;
        DoubleBuffered = true;

        _castingTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _castingTimer.Tick += CastingTimer_Tick;

        _titleBar = BuildTitleBar();
        _statusIcon = new StatusIndicator { Size = new Size(96, 96), BackColor = Color.Transparent };
        _lblHero = new Label
        {
            Font = Theme.Hero,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            BackColor = Color.Transparent,
            Text = "未连接"
        };
        _lblHeroSub = new Label
        {
            Font = Theme.Body,
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            BackColor = Color.Transparent,
            Text = "请从下方选择设备或手动输入 IP"
        };

        _lblSectionLabel = new Label
        {
            Font = new Font(Theme.FontFamily, 9f, FontStyle.Bold),
            ForeColor = Theme.PrimarySky,
            AutoSize = true,
            BackColor = Color.Transparent,
            Text = "设备列表"
        };
        _lblSectionCount = new Label
        {
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            BackColor = Color.Transparent,
            Visible = false,
            Text = ""
        };
        _btnRefresh = new RoundedButton
        {
            Text = "刷新",
            Style = ButtonStyle.NeutralOutline,
            Size = new Size(76, 32),
            BorderRadius = 8
        };
        _btnRefresh.Click += async (_, _) => await DiscoverDevicesAsync();

        _deviceList = new FlowLayoutPanel
        {
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Theme.Background,
            Padding = new Padding(0)
        };

        _ipInputBar = new IpInputBar();
        _ipInputBar.ConnectRequested += (_, target) => InitiateConnection(target);

        _lblFooter = new Label
        {
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            BackColor = Color.Transparent,
            Text = $"v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?.?.?"}"
        };

        var hotspotOn = EncoderSettings.Load().StartHotspot;
        _toggleHotspot = new ToggleSwitch { Checked = hotspotOn };
        _toggleHotspot.CheckedChanged += HotspotToggle_Changed;

        _lblHotspot = new Label
        {
            Text      = "热点",
            Font      = Theme.Small,
            ForeColor = Theme.TextMuted,
            AutoSize  = true,
            BackColor = Color.Transparent,
        };

        Controls.Add(_titleBar);
        Controls.Add(_statusIcon);
        Controls.Add(_lblHero);
        Controls.Add(_lblHeroSub);
        Controls.Add(_ipInputBar);
        Controls.Add(_lblSectionLabel);
        Controls.Add(_lblSectionCount);
        Controls.Add(_btnRefresh);
        Controls.Add(_deviceList);
        Controls.Add(_lblFooter);
        Controls.Add(_lblHotspot);
        Controls.Add(_toggleHotspot);
        _toggleHotspot.BringToFront();
        _lblHotspot.BringToFront();

        Resize += (_, _) => LayoutColumn();
        LayoutColumn();
        SetState(AppState.Idle);
    }

    private Panel BuildTitleBar()
    {
        var bar = new Panel { Height = 48, Dock = DockStyle.Top, BackColor = Theme.Background };
        bar.Paint += (_, e) =>
        {
            Theme.EnableHighQuality(e.Graphics);
            using var dot = new SolidBrush(Theme.PrimarySky);
            e.Graphics.FillEllipse(dot, 24, 18, 12, 12);
            using var ring = new Pen(Theme.LightSky, 1.5f);
            e.Graphics.DrawEllipse(ring, 22, 16, 16, 16);
            using var brandFont = new Font(Theme.FontFamily, 14f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "橙子投屏", brandFont,
                new Rectangle(46, 0, 200, bar.Height), Theme.Primary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        bar.MouseDown += TitleBar_MouseDown;

        var btnSettings = new IconBox
        {
            IconName = "cog",
            IconSize = 28,
            Tint = Theme.TextSecondary,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Width - 132, 10)
        };
        btnSettings.MouseEnter += (_, _) => btnSettings.Tint = Theme.PrimaryHover;
        btnSettings.MouseLeave += (_, _) => btnSettings.Tint = Theme.TextSecondary;
        btnSettings.Click += (_, _) =>
        {
            using var dlg = new SettingsDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && _screenCapture != null)
                _screenCapture.ShowCursor = EncoderSettings.Load().ShowCursor;
        };

        var btnMin = new IconBox
        {
            IconName = "minus",
            IconSize = 20,
            Tint = Theme.TextSecondary,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Width - 78, 14)
        };
        btnMin.MouseEnter += (_, _) => btnMin.Tint = Theme.PrimaryHover;
        btnMin.MouseLeave += (_, _) => btnMin.Tint = Theme.TextSecondary;
        btnMin.Click += (_, _) => WindowState = FormWindowState.Minimized;

        var btnClose = new IconBox
        {
            IconName = "x",
            IconSize = 24,
            Tint = Theme.TextSecondary,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Width - 38, 12)
        };
        btnClose.MouseEnter += (_, _) => btnClose.Tint = Theme.Error;
        btnClose.MouseLeave += (_, _) => btnClose.Tint = Theme.TextSecondary;
        btnClose.Click += (_, _) => Close();
        btnClose.Location = new Point(Width - 44, 4);

        bar.Controls.Add(btnSettings);
        bar.Controls.Add(btnMin);
        bar.Controls.Add(btnClose);
        bar.Resize += (_, _) =>
        {
            btnSettings.Location = new Point(bar.Width - 132, 10);
            btnMin.Location   = new Point(bar.Width - 78, 14);
            btnClose.Location = new Point(bar.Width - 38, 12);
        };
        return bar;
    }

    private void LayoutColumn()
    {
        const int columnMax = 640;
        int colW = Math.Min(columnMax, ClientSize.Width - 64);
        int colX = (ClientSize.Width - colW) / 2;

        int y = _titleBar.Height + 36;

        _statusIcon.Location = new Point(ClientSize.Width / 2 - _statusIcon.Width / 2, y);
        y += _statusIcon.Height + 20;

        _lblHero.Location = new Point(ClientSize.Width / 2 - _lblHero.PreferredWidth / 2, y);
        y += _lblHero.PreferredHeight + 6;

        _lblHeroSub.Location = new Point(ClientSize.Width / 2 - _lblHeroSub.PreferredWidth / 2, y);
        y += _lblHeroSub.PreferredHeight + 32;

        _ipInputBar.Location = new Point(colX, y);
        _ipInputBar.Width = colW;
        y += _ipInputBar.Height + 40;

        _lblSectionLabel.Location = new Point(colX, y);
        _lblSectionCount.Location = new Point(colX + _lblSectionLabel.PreferredWidth + 12, y + 2);
        _btnRefresh.Location      = new Point(colX + colW - _btnRefresh.Width, y - 4);
        y += 32;

        _deviceList.Location = new Point(colX, y);
        int bottomReserved = 70;
        _deviceList.Size = new Size(colW, ClientSize.Height - y - bottomReserved);

        foreach (Control c in _deviceList.Controls)
        {
            if (c is DeviceCard dc) dc.Width = colW - 4;
            else c.Width = colW - 4;
        }

        int by = ClientSize.Height - 60;
        _lblFooter.Location = new Point(ClientSize.Width / 2 - _lblFooter.PreferredWidth / 2, ClientSize.Height - 22);

        int toggleY = ClientSize.Height - _toggleHotspot.Height - 14;
        int toggleX = ClientSize.Width  - _toggleHotspot.Width  - 20;
        _toggleHotspot.Location = new Point(toggleX, toggleY);
        _lblHotspot.Location    = new Point(toggleX - _lblHotspot.PreferredWidth - 8,
                                             toggleY + (_toggleHotspot.Height - _lblHotspot.PreferredHeight) / 2);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        InitializeTrayIcon();
        _ = DiscoverDevicesAsync();
        if (EncoderSettings.Load().StartHotspot)
            _ = SetHotspotAsync(true);
    }

    private async void HotspotToggle_Changed(object? sender, EventArgs e)
    {
        bool next = _toggleHotspot.Checked;
        var settings = EncoderSettings.Load();
        settings.StartHotspot = next;
        settings.Save();
        await SetHotspotAsync(next);
    }

    private static async Task SetHotspotAsync(bool enable)
    {
        try
        {
            var profiles = Windows.Networking.Connectivity.NetworkInformation.GetConnectionProfiles();
            Windows.Networking.Connectivity.ConnectionProfile? wanProfile = null;
            foreach (var p in profiles)
            {
                if (p.IsWlanConnectionProfile || p.GetNetworkConnectivityLevel() != Windows.Networking.Connectivity.NetworkConnectivityLevel.None)
                {
                    wanProfile = p;
                    break;
                }
            }
            if (wanProfile == null)
            {
                AppLog.Signaling("Hotspot", "No suitable network profile found for tethering");
                return;
            }
            var mgr = NetworkOperatorTetheringManager.CreateFromConnectionProfile(wanProfile);
            if (enable)
                await mgr.StartTetheringAsync();
            else
                await mgr.StopTetheringAsync();
            AppLog.Signaling("Hotspot", enable ? "Tethering started" : "Tethering stopped");
        }
        catch (Exception ex)
        {
            AppLog.Signaling("Hotspot", $"Tethering failed: {ex.Message}");
        }
    }

    private void InitializeTrayIcon()
    {
        var menu = new ContextMenuStrip();
        var miShow = new ToolStripMenuItem("显示窗口");
        miShow.Click += (_, _) => RestoreFromTray();
        var miExit = new ToolStripMenuItem("退出");
        miExit.Click += (_, _) =>
        {
            _reallyExit = true;
            Close();
        };
        menu.Items.Add(miShow);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miExit);

        Icon trayIconImage;
        try
        {
            var exePath = Environment.ProcessPath;
            trayIconImage = !string.IsNullOrEmpty(exePath)
                ? Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application
                : SystemIcons.Application;
        }
        catch { trayIconImage = SystemIcons.Application; }

        _trayIcon = new NotifyIcon
        {
            Icon = trayIconImage,
            Text = "橙子投屏",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.EnableHighQuality(e.Graphics);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Theme.RadiusWindow);
        Region = new Region(path);
        
        using var borderPen = new Pen(Theme.BorderNeutral, 1f);
        e.Graphics.DrawPath(borderPen, path);
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTBOTTOMRIGHT = 17;
        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST)
        {
            var pos = PointToClient(new Point(m.LParam.ToInt32() & 0xffff, m.LParam.ToInt32() >> 16));
            if (pos.X >= ClientSize.Width - 16 && pos.Y >= ClientSize.Height - 16)
                m.Result = (IntPtr)HTBOTTOMRIGHT;
        }
    }

    private void SetState(AppState state, string? subText = null)
    {
        // BeginInvoke (not Invoke): worker threads (SIPSorcery state callbacks) must NOT block on
        // the UI thread, otherwise close() teardown deadlocks UI thread waiting on this call.
        if (InvokeRequired) { try { BeginInvoke(() => SetState(state, subText)); } catch { } return; }

        _state = state;
        _statusIcon.State = state;

        WinSender.UI.Controls.ButtonState bs = WinSender.UI.Controls.ButtonState.Normal;
        if (state == AppState.Connecting || state == AppState.Reconnecting) bs = WinSender.UI.Controls.ButtonState.Connecting;
        else if (state == AppState.Casting) bs = WinSender.UI.Controls.ButtonState.Connected;

        _ipInputBar.SetState(bs);

        foreach (Control c in _deviceList.Controls)
        {
            if (c is DeviceCard dc)
            {
                string t = $"{dc.Device.Host}:{dc.Device.Port}";
                if (t == _currentTarget && (bs == WinSender.UI.Controls.ButtonState.Connecting || bs == WinSender.UI.Controls.ButtonState.Connected))
                {
                    dc.SetConnectionState(bs);
                    _currentCard = dc;
                }
                else
                {
                    dc.SetConnectionState(WinSender.UI.Controls.ButtonState.Normal);
                }
            }
        }

        switch (state)
        {
            case AppState.Idle:
                _lblHero.Text       = "未连接";
                _lblHeroSub.Text    = subText ?? "请从下方选择设备或手动输入 IP";
                break;
            case AppState.Searching:
                _lblHero.Text       = "搜索设备";
                _lblHeroSub.Text    = subText ?? "请确保 TV 已开启接收端并处于同一局域网";
                break;
            case AppState.Connecting:
                _lblHero.Text       = "连接中";
                _lblHeroSub.Text    = subText ?? $"正在连接 {_currentTarget}";
                break;
            case AppState.Casting:
                _lblHero.Text       = "投屏中";
                _lblHeroSub.Text    = $"→ {_currentTarget}   00:00:00";
                _castingStartTime   = DateTime.Now;
                _castingTimer.Start();
                break;
            case AppState.Error:
                _lblHero.Text       = "出错了";
                _lblHeroSub.Text    = subText ?? "发生未知错误";
                break;
            case AppState.Reconnecting:
                _lblHero.Text       = "重连中";
                _lblHeroSub.Text    = subText ?? $"目标 {_currentTarget}";
                _castingTimer.Stop();
                break;
        }
        LayoutColumn();
    }

    private async Task DiscoverDevicesAsync()
    {
        SetState(AppState.Searching);
        _deviceList.Controls.Clear();
        _btnRefresh.Enabled = false;
        _lblSectionCount.Text = "scanning…";

        try
        {
            var devices = await _discoverer.DiscoverAsync(TimeSpan.FromSeconds(3));
            _deviceList.SuspendLayout();
            _deviceList.Controls.Clear();

            if (devices.Count == 0)
            {
                _lblSectionCount.Text = "0 found";
                var lblEmpty = new Label
                {
                    Text = "未发现设备\n请确认 TV 已开启接收端,或手动输入 IP",
                    Font = Theme.Body,
                    ForeColor = Theme.TextSecondary,
                    AutoSize = true,
                    Margin = new Padding(8, 32, 0, 0)
                };
                _deviceList.Controls.Add(lblEmpty);
            }
            else
            {
                _lblSectionCount.Text = $"{devices.Count} found";
                foreach (var dev in devices)
                {
                    var card = new DeviceCard(dev) { Width = _deviceList.ClientSize.Width - 4 };
                    card.ConnectClicked += (_, d) => InitiateConnection($"{d.Host}:{d.Port}");
                    _deviceList.Controls.Add(card);
                }
            }
            _deviceList.ResumeLayout();
            SetState(AppState.Idle);
        }
        catch (Exception ex)
        {
            SetState(AppState.Error, "设备发现失败: " + ex.Message);
            ToastNotification.ShowError("设备发现失败");
        }
        finally
        {
            _btnRefresh.Enabled = true;
            LayoutColumn();
        }
    }



    private async void InitiateConnection(string target)
    {
        try
        {
            if (_state == AppState.Connecting || _state == AppState.Casting || _state == AppState.Reconnecting)
            {
                if (_currentTarget == target)
                {
                    await DisconnectAsync();
                    return;
                }
                await DisconnectAsync();
            }

            _userInitiatedDisconnect = false;
            _currentTarget = target;

        var (host, port) = ParseTarget(target);
        var trusted = _trustStore.FindByHost(host, port);

        AuthInfo auth;
        if (trusted != null)
        {
            auth = new AuthInfo("token", trusted.Token);
            _currentDeviceId = trusted.DeviceId;
            Console.WriteLine($"[Connect] Using saved token for {trusted.DeviceName}");
        }
        else
        {
            var code = PairingCodeDialog.ShowDialogAndGetCode(this);
            if (string.IsNullOrEmpty(code)) return;
            auth = new AuthInfo("pin", code);
            _currentDeviceId = null;
        }

        EnsureManualDeviceCard(host, port);
        SetState(AppState.Connecting);
        await _connectionGate.WaitAsync();
        try
        {
            await ConnectOnceAsync(target, auth);
            SetState(AppState.Casting);
            ToastNotification.ShowSuccess("投屏已开始");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Connect] Failed: {ex.Message}");
            WinSender.Diagnostics.AppLog.For("Connect").Warning(ex, "InitiateConnection failed target={Target}", target);
            SetState(AppState.Error, ex.Message);
            ToastNotification.ShowError("连接失败: " + ex.Message);
            try { if (_client != null) await _client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _client = null;
            _sender = null;
        }
        finally
        {
            _connectionGate.Release();
        }
        }
        catch (Exception ex)
        {
            WinSender.Diagnostics.AppLog.For("Connect").Error(ex, "InitiateConnection top-level crash");
        }
    }

    private async Task ConnectOnceAsync(string target, AuthInfo auth)
    {
        var ident = _trustStore.Identity;

        _client = new SignalingClient(target);
        _client.Disconnected += OnSignalingDisconnected;
        await _client.ConnectAsync();

        var reqPayload = JsonSerializer.Serialize(new ConnectRequestPayload(ident.DeviceId, ident.DeviceName, auth));
        await _client.SendAsync(new SignalingMessage("CONNECT_REQUEST", reqPayload));

        var pairingTcs = new TaskCompletionSource<(bool ok, string? payload, string? err)>();
        void OnMsg(SignalingMessage msg)
        {
            if (msg.Type == "CONNECT_ACCEPT") pairingTcs.TrySetResult((true, msg.Payload, null));
            else if (msg.Type == "CONNECT_REJECT") pairingTcs.TrySetResult((false, null, msg.Payload));
        }
        _client.MessageReceived += OnMsg;

        var timeout = Task.Delay(30000);
        var done = await Task.WhenAny(pairingTcs.Task, timeout);
        _client.MessageReceived -= OnMsg;

        if (done == timeout) throw new Exception("配对超时");

        var (ok, acceptJson, errPayload) = await pairingTcs.Task;
        if (!ok)
        {
            string reason = "配对被拒";
            if (!string.IsNullOrEmpty(errPayload))
            {
                try
                {
                    var rej = JsonSerializer.Deserialize<ConnectRejectPayload>(errPayload);
                    if (rej != null) reason = rej.Reason;
                }
                catch { reason = errPayload; }
            }
            if (auth.Type == "token" && _currentDeviceId != null)
            {
                _trustStore.Remove(_currentDeviceId);
                _currentDeviceId = null;
            }
            throw new Exception(reason);
        }

        if (!string.IsNullOrEmpty(acceptJson))
        {
            try
            {
                var accept = JsonSerializer.Deserialize<ConnectAcceptPayload>(acceptJson);
                if (accept != null && !string.IsNullOrEmpty(accept.Token))
                {
                    var (host, port) = ParseTarget(target);
                    _currentDeviceId = accept.DeviceId;
                    _trustStore.Upsert(new TrustedDevice(
                        accept.DeviceId, accept.DeviceName, accept.Token,
                        host, port, DateTime.UtcNow));
                    Console.WriteLine($"[Connect] Saved token for {accept.DeviceName} ({accept.DeviceId})");
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Connect] Parse ACCEPT failed: {ex.Message}"); }
        }

        var screenCapture = new ScreenCapture();
        _screenCapture = screenCapture;
        screenCapture.ShowCursor = EncoderSettings.Load().ShowCursor;
        var audioCapture  = new SystemAudioCapture();
        var abrController = new AbrController();
        _sender = new WebRtcSender(_client, screenCapture, audioCapture, abrController);
        _sender.ConnectionLost += OnPeerConnectionLost;
        await _sender.StartAsync(auth.Value);
    }

    private void EnsureManualDeviceCard(string host, int port)
    {
        foreach (Control c in _deviceList.Controls)
        {
            if (c is DeviceCard dc && dc.Device.Host == host && dc.Device.Port == port)
                return;
        }
        foreach (Control c in _deviceList.Controls.Cast<Control>().ToList())
        {
            if (c is Label) _deviceList.Controls.Remove(c);
        }
        var dev = new TvDevice($"{host}:{port}", host, port);
        var card = new DeviceCard(dev) { Width = _deviceList.ClientSize.Width - 4 };
        card.ConnectClicked += (_, d) => InitiateConnection($"{d.Host}:{d.Port}");
        _deviceList.Controls.Add(card);
        _lblSectionCount.Text = $"{_deviceList.Controls.OfType<DeviceCard>().Count()} found";
    }

    private static (string host, int port) ParseTarget(string target)
    {
        var s = target.StartsWith("ws://") ? target.Substring(5) : target;
        var idx = s.IndexOf(':');
        if (idx < 0) return (s, 8765);
        return (s.Substring(0, idx), int.TryParse(s.Substring(idx + 1), out var p) ? p : 8765);
    }

    private void OnSignalingDisconnected(DisconnectReason reason, string detail)
    {
        try
        {
            if (_userInitiatedDisconnect) return;
            if (_state != AppState.Casting && _state != AppState.Connecting) return;
            Console.WriteLine($"[Reconnect] Signaling disconnected: {reason} - {detail}");
            WinSender.Diagnostics.AppLog.For("Reconnect").Information("signaling disconnected reason={Reason} detail={Detail}", reason, detail);
            _ = Task.Run(StartReconnectLoopAsync);
        }
        catch (Exception ex)
        {
            WinSender.Diagnostics.AppLog.For("Reconnect").Error(ex, "OnSignalingDisconnected handler failed");
        }
    }

    private void OnPeerConnectionLost(string state)
    {
        try
        {
            if (_userInitiatedDisconnect) return;
            Console.WriteLine($"[Reconnect] Peer connection lost: {state}");
            WinSender.Diagnostics.AppLog.For("Reconnect").Information("peer connection lost state={State}", state);
            _ = Task.Run(StartReconnectLoopAsync);
        }
        catch (Exception ex)
        {
            WinSender.Diagnostics.AppLog.For("Reconnect").Error(ex, "OnPeerConnectionLost handler failed");
        }
    }

    private async Task StartReconnectLoopAsync()
    {
        // Atomic reentry guard: previous code used non-atomic `if (_reconnectCts != null)` check
        // which let two simultaneous disconnect callbacks both pass the guard and double-loop.
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) == 1) return;

        await _connectionGate.WaitAsync();
        try
        {
            _reconnectCts = new CancellationTokenSource();
            var ct = _reconnectCts.Token;

            var target = _currentTarget;
            if (string.IsNullOrEmpty(target)) return;

            try { _sender?.Stop(); } catch (Exception ex) { WinSender.Diagnostics.AppLog.For("Reconnect").Warning(ex, "sender.Stop failed"); }
            try { if (_client != null) await _client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { WinSender.Diagnostics.AppLog.For("Reconnect").Warning(ex, "client.DisconnectAsync failed/timeout"); }
            _sender = null;
            _client = null;

            SetState(AppState.Reconnecting, "正在尝试重新连接…");

            var (host, port) = ParseTarget(target);
            var trusted = _trustStore.FindByHost(host, port);
            if (trusted == null)
            {
                SetState(AppState.Error, "无保存的配对凭证,无法自动重连");
                ToastNotification.ShowError("自动重连失败:需要重新配对");
                return;
            }
            var auth = new AuthInfo("token", trusted.Token);

            int[] backoffSec = { 1, 2, 4, 8, 16, 30 };
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                int wait = attempt < backoffSec.Length ? backoffSec[attempt] : 60;
                attempt++;
                SetState(AppState.Reconnecting, $"第 {attempt} 次  ({wait}s 后重试)  → {target}");

                try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); }
                catch (OperationCanceledException) { break; }
                if (ct.IsCancellationRequested) break;

                try
                {
                    Console.WriteLine($"[Reconnect] Attempt #{attempt} → {target}");
                    WinSender.Diagnostics.AppLog.For("Reconnect").Information("attempt {Attempt} target={Target}", attempt, target);
                    await ConnectOnceAsync(target, auth);
                    SetState(AppState.Casting);
                    ToastNotification.ShowSuccess("已重新连接");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Reconnect] Attempt #{attempt} failed: {ex.Message}");
                    WinSender.Diagnostics.AppLog.For("Reconnect").Warning(ex, "attempt {Attempt} failed", attempt);
                    try { if (_client != null) await _client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                    _client = null;
                    _sender = null;
                    if (attempt > 30)
                    {
                        SetState(AppState.Error, $"重连失败 {attempt} 次,已停止");
                        ToastNotification.ShowError("自动重连已停止");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            WinSender.Diagnostics.AppLog.For("Reconnect").Error(ex, "reconnect loop crashed");
        }
        finally
        {
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            Interlocked.Exchange(ref _reconnecting, 0);
            _connectionGate.Release();
        }
    }

    private async Task DisconnectAsync()
    {
        _userInitiatedDisconnect = true;
        _reconnectCts?.Cancel();
        _castingTimer.Stop();

        await _connectionGate.WaitAsync();
        try
        {
            try { _sender?.Stop(); } catch (Exception ex) { WinSender.Diagnostics.AppLog.For("Disconnect").Warning(ex, "sender.Stop failed"); }
            try { if (_client != null) await _client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { WinSender.Diagnostics.AppLog.For("Disconnect").Warning(ex, "client.DisconnectAsync failed/timeout"); }

            _sender = null;
            _client = null;
            _currentDeviceId = null;
            SetState(AppState.Idle);
            ToastNotification.ShowInfo("已断开连接");
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private void CastingTimer_Tick(object? sender, EventArgs e)
    {
        if (_state == AppState.Casting)
        {
            var span = DateTime.Now - _castingStartTime;
            _lblHeroSub.Text = $"→ {_currentTarget}   {span:hh\\:mm\\:ss}";
            LayoutColumn();
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(1500, "橙子投屏", "已最小化到托盘，双击图标可恢复窗口", ToolTipIcon.Info);
            return;
        }

        if (_state == AppState.Casting)
        {
            var res = MessageBox.Show("正在投屏，确定退出吗？", "橙子投屏", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res == DialogResult.No) { e.Cancel = true; _reallyExit = false; return; }
        }
        if (_state == AppState.Casting || _state == AppState.Connecting)
            await DisconnectAsync();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        base.OnFormClosing(e);
    }
}
