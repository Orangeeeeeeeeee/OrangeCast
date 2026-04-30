using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinSender.Settings;
using WinSender.WebRTC;

namespace WinSender.UI.Controls;

public class SettingsDialog : Form
{
    private readonly CheckBox _chkHwAccel;
    private readonly ComboBox _cmbVendor;
    private readonly Label _lblHwHint;
    private readonly CheckBox _chkStartHotspot;
    private readonly CheckBox _chkShowCursor;
    private readonly RoundedButton _btnCancel;
    private readonly RoundedButton _btnSave;

    public SettingsDialog()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(480, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ShowInTaskbar = false;

        var encSettings = EncoderSettings.Load();

        var title = new Label
        {
            Text = "设置",
            Font = new Font(Theme.FontFamily, 14f, FontStyle.Bold),
            ForeColor = Theme.Primary,
            AutoSize = true,
            Location = new Point(20, 16),
            BackColor = Color.Transparent
        };

        _chkHwAccel = new CheckBox
        {
            Text = "启用硬件加速",
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize = true,
            Checked = encSettings.HwAccel,
            Cursor = Cursors.Hand,
            Location = new Point(32, 70)
        };

        _cmbVendor = new ComboBox
        {
            Font = Theme.Body,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            ItemHeight = 26,
            Size = new Size(280, 28),
            Enabled = encSettings.HwAccel,
            Location = new Point(32, 110)
        };
        _cmbVendor.DrawItem += DrawVendorItem;

        var availableVendors = HardwareEncoderDetector.ProbeAvailableVendors();
        var gpuList = GpuEnumerator.EnumerateGpus();
        var gpuByVendor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in gpuList)
        {
            if (!gpuByVendor.ContainsKey(g.Vendor)) gpuByVendor[g.Vendor] = g.Description;
        }

        foreach (var v in availableVendors)
        {
            string display = v == "auto"
                ? "自动"
                : (gpuByVendor.TryGetValue(v, out var desc) ? desc : v.ToUpperInvariant());
            _cmbVendor.Items.Add(new VendorItem(v, display));
        }

        var savedIdx = availableVendors.IndexOf(encSettings.Vendor);
        _cmbVendor.SelectedIndex = savedIdx >= 0 ? savedIdx : 0;

        _lblHwHint = new Label
        {
            Text = "（下次连接生效）",
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(32, 150)
        };

        _chkHwAccel.CheckedChanged += (_, _) => _cmbVendor.Enabled = _chkHwAccel.Checked;

        _chkStartHotspot = new CheckBox
        {
            Text = "启动时打开系统热点",
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize = true,
            Checked = encSettings.StartHotspot,
            Cursor = Cursors.Hand,
            Location = new Point(32, 185)
        };

        _chkShowCursor = new CheckBox
        {
            Text = "投屏时显示鼠标",
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize = true,
            Checked = encSettings.ShowCursor,
            Cursor = Cursors.Hand,
            Location = new Point(32, 225)
        };

        _btnCancel = new RoundedButton
        {
            Text = "取消",
            Style = ButtonStyle.Outline,
            Size = new Size(100, 40),
            Location = new Point(Width - 240, Height - 64)
        };
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnSave = new RoundedButton
        {
            Text = "保存",
            Style = ButtonStyle.Primary,
            Size = new Size(100, 40),
            Location = new Point(Width - 124, Height - 64)
        };
        _btnSave.Click += (_, _) =>
        {
            var vendor = _cmbVendor.SelectedItem is VendorItem vi ? vi.Token : "auto";
            var s = new EncoderSettings
            {
                HwAccel = _chkHwAccel.Checked,
                Vendor = vendor,
                StartHotspot = _chkStartHotspot.Checked,
                ShowCursor = _chkShowCursor.Checked
            };
            s.Save();
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(title);
        Controls.Add(_chkHwAccel);
        Controls.Add(_cmbVendor);
        Controls.Add(_lblHwHint);
        Controls.Add(_chkStartHotspot);
        Controls.Add(_chkShowCursor);
        Controls.Add(_btnCancel);
        Controls.Add(_btnSave);
    }

    private void DrawVendorItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var cmb = (ComboBox)sender!;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bg = selected ? Theme.PrimarySoft : Theme.Surface;
        var fg = selected ? Theme.PrimaryPressed : Theme.TextPrimary;
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
        var text = cmb.Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, e.Font ?? Theme.Body,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
            fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.DrawFocusRectangle();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);

        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Theme.RadiusWindow);
        Region = new Region(path);

        g.Clear(Theme.Background);

        using var borderPen = new Pen(Theme.BorderNeutral, 1f);
        g.DrawPath(borderPen, path);
    }

    private sealed record VendorItem(string Token, string Display)
    {
        public override string ToString() => Display;
    }
}