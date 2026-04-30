using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WinSender.UI.Controls;

public class PairingCodeDialog : Form
{
    private readonly TextBox[] _boxes = new TextBox[4];
    private readonly Panel[] _slots = new Panel[4];
    private readonly RoundedButton _btnConfirm;
    private readonly RoundedButton _btnCancel;

    public string Code => string.Join("", _boxes.Select(b => b.Text));

    public PairingCodeDialog()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 420);
        BackColor = Theme.Surface;
        ShowInTaskbar = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var lblEyebrow = new Label
        {
            Text = "PAIRING",
            Font = new Font(Theme.FontFamily, 9f, FontStyle.Bold),
            ForeColor = Theme.PrimarySky,
            AutoSize = true,
            Location = new Point(48, 48)
        };
        Controls.Add(lblEyebrow);

        var lblTitle = new Label
        {
            Text = "输入 TV 配对码",
            Font = Theme.H1,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(48, 72)
        };
        Controls.Add(lblTitle);

        var lblHint = new Label
        {
            Text = "屏幕左上角显示的 4 位数字",
            Font = Theme.Body,
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Location = new Point(48, 116)
        };
        Controls.Add(lblHint);

        const int slotSize = 84;
        const int gap = 16;
        int total = slotSize * 4 + gap * 3;
        int startX = (Width - total) / 2;
        int slotY = 180;

        for (int i = 0; i < 4; i++)
        {
            var idx = i;
            var slot = new Panel
            {
                Size = new Size(slotSize, slotSize),
                Location = new Point(startX + i * (slotSize + gap), slotY),
                BackColor = Theme.Surface
            };

            var tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Theme.BigDigit,
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 1,
                Width = slotSize - 12,
                BackColor = Theme.Surface,
                ForeColor = Theme.TextPrimary
            };
            tb.Location = new Point((slotSize - tb.Width) / 2, (slotSize - tb.PreferredHeight) / 2);

            slot.Paint += (_, e) =>
            {
                var g = e.Graphics;
                Theme.EnableHighQuality(g);
                var rect = new Rectangle(0, 0, slot.Width - 1, slot.Height - 1);
                using var path = Theme.RoundedRect(rect, 14);

                bool focused = tb.Focused;
                bool filled = tb.Text.Length > 0;
                using var bg = new SolidBrush(focused ? Theme.MistSky : Theme.Surface);
                g.FillPath(bg, path);
                using var pen = new Pen(focused ? Theme.PrimarySky : (filled ? Theme.BorderStrong : Theme.Border), focused ? 2f : 1.5f);
                g.DrawPath(pen, path);
            };

            tb.GotFocus += (_, _) => slot.Invalidate();
            tb.LostFocus += (_, _) => slot.Invalidate();
            tb.TextChanged += (_, _) =>
            {
                slot.Invalidate();
                if (tb.Text.Length == 1 && idx < 3) _boxes[idx + 1].Focus();
                UpdateState();
                if (idx == 3 && Code.Length == 4)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            tb.KeyDown += (_, ev) =>
            {
                if (ev.KeyCode == Keys.Back && string.IsNullOrEmpty(tb.Text) && idx > 0)
                {
                    _boxes[idx - 1].Focus();
                    _boxes[idx - 1].Text = "";
                }
            };
            tb.KeyPress += (_, ev) =>
            {
                if (!char.IsControl(ev.KeyChar) && !char.IsDigit(ev.KeyChar))
                    ev.Handled = true;
            };

            slot.Controls.Add(tb);
            Controls.Add(slot);
            _boxes[i] = tb;
            _slots[i] = slot;
        }

        _btnCancel = new RoundedButton
        {
            Text = "取消",
            Style = ButtonStyle.Text,
            Size = new Size(96, 40),
            Location = new Point(Width - 240, Height - 80)
        };
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_btnCancel);

        _btnConfirm = new RoundedButton
        {
            Text = "确认连接",
            Style = ButtonStyle.Primary,
            Size = new Size(128, 40),
            Location = new Point(Width - 128 - 48, Height - 80),
            Enabled = false
        };
        _btnConfirm.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(_btnConfirm);
    }

    private void UpdateState()
    {
        _btnConfirm.Enabled = Code.Length == 4;
        _btnConfirm.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableHighQuality(g);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, 18);
        using var pen = new Pen(Theme.BorderStrong, 1.5f);
        g.DrawPath(pen, path);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _boxes[0].Focus();
    }

    public static string ShowDialogAndGetCode(Form parent)
    {
        using var mask = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            Opacity = 0.45,
            StartPosition = FormStartPosition.Manual,
            Location = parent.PointToScreen(Point.Empty),
            Size = parent.ClientSize
        };
        mask.Show(parent);

        using var dlg = new PairingCodeDialog
        {
            StartPosition = FormStartPosition.Manual
        };
        dlg.Location = new Point(
            parent.Location.X + (parent.Width  - dlg.Width)  / 2,
            parent.Location.Y + (parent.Height - dlg.Height) / 2);

        var result = dlg.ShowDialog(mask);
        return result == DialogResult.OK ? dlg.Code : "";
    }
}
