using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MicNeverDown
{
    public sealed class SettingsForm : Form
    {
        private static readonly Color Background = Color.FromArgb(16, 20, 29);
        private static readonly Color Surface = Color.FromArgb(25, 32, 45);
        private static readonly Color TextColor = Color.FromArgb(237, 242, 249);
        private static readonly Color MutedColor = Color.FromArgb(154, 168, 188);
        private static readonly Color Accent = Color.FromArgb(139, 231, 204);
        private readonly ComboBox devices = new ComboBox();
        private readonly LocalizedNumericUpDown level = new LocalizedNumericUpDown();
        private readonly VolumeSlider slider = new VolumeSlider();
        private readonly CheckBox startup = new CheckBox();
        private readonly Label note = new Label();
        private readonly Button apply;
        private readonly Action<Settings, bool> save;
        private readonly Settings existing;
        private readonly Bitmap catPicture;
        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly List<Action> languageBindings = new List<Action>();
        private CultureInfo previousCulture;
        private bool noDevices;
        private string deviceError;
        private bool syncing;

        public SettingsForm(Settings settings, Action<Settings, bool> onSave)
        {
            existing = settings;
            save = onSave;
            previousCulture = Ui.Culture;
            Text = "MicNeverDown";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 598);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            ForeColor = TextColor;
            Font = MakeFont(10F, FontStyle.Regular);
            DoubleBuffered = true;

            Controls.Add(MakeLabel("MicNeverDown", 28, 27, 450, 42, 25F, FontStyle.Bold, TextColor));
            Controls.Add(MakeLabel("Your microphone volume, under control", 30, 77, 460, 24, 10F, FontStyle.Regular, MutedColor, "Громкость микрофона под контролем"));
            catPicture = LoadCatPicture();
            Controls.Add(new PictureBox { Image = catPicture, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(506, 30, 64, 64), TabStop = false });

            RoundedCard deviceCard = new RoundedCard { Bounds = new Rectangle(28, 126, 544, 138), BackColor = Surface };
            deviceCard.Controls.Add(MakeLabel("Microphone", 20, 17, 340, 27, 12F, FontStyle.Bold, TextColor, "Микрофон"));
            Button refresh = MakeButton("Refresh", new Rectangle(405, 14, 121, 32), false, "Обновить");
            languageBindings.Add(delegate { refresh.AccessibleName = Ui.Text("Refresh microphone list", "Обновить список микрофонов"); });
            refresh.Click += delegate { RefreshDevices(); };
            deviceCard.Controls.Add(refresh);
            deviceCard.Controls.Add(MakeLabel("Choose your headset microphone", 20, 49, 490, 23, 9F, FontStyle.Regular, MutedColor, "Выберите устройство гарнитуры"));
            devices.SetBounds(20, 82, 504, 36);
            devices.DropDownStyle = ComboBoxStyle.DropDownList;
            devices.DisplayMember = "Name";
            devices.FlatStyle = FlatStyle.Flat;
            devices.BackColor = Color.FromArgb(36, 46, 63);
            devices.ForeColor = TextColor;
            devices.DrawMode = DrawMode.OwnerDrawFixed;
            devices.ItemHeight = 29;
            devices.DropDownWidth = 504;
            languageBindings.Add(delegate { devices.AccessibleName = Ui.Text("Headset microphone", "Микрофон гарнитуры"); });
            devices.DrawItem += DrawDevice;
            devices.SelectedIndexChanged += delegate
            {
                AudioDeviceInfo selected = devices.SelectedItem as AudioDeviceInfo;
                if (selected != null) level.Value = (decimal)(Math.Round(selected.Level * 1000) / 10.0);
            };
            deviceCard.Controls.Add(devices);
            Controls.Add(deviceCard);

            RoundedCard volumeCard = new RoundedCard { Bounds = new Rectangle(28, 278, 544, 158), BackColor = Surface };
            volumeCard.Controls.Add(MakeLabel("Volume level", 20, 18, 345, 28, 12F, FontStyle.Bold, TextColor, "Уровень громкости"));
            volumeCard.Controls.Add(MakeLabel("We will keep it at this level", 20, 52, 345, 23, 9F, FontStyle.Regular, MutedColor, "Будем удерживать это значение"));
            level.SetBounds(397, 23, 91, 32);
            level.Minimum = 0;
            level.Maximum = 100;
            level.DecimalPlaces = 1;
            level.Increment = 0.1M;
            level.Font = MakeFont(17F, FontStyle.Bold);
            level.BackColor = Surface;
            level.ForeColor = Accent;
            level.BorderStyle = BorderStyle.None;
            languageBindings.Add(delegate { level.AccessibleName = Ui.Text("Volume to keep, percent", "Удерживать уровень, процентов"); });
            level.ValueChanged += delegate
            {
                if (syncing) return;
                syncing = true;
                slider.Value = (int)Math.Round(level.Value * 10M);
                syncing = false;
            };
            volumeCard.Controls.Add(level);
            volumeCard.Controls.Add(MakeLabel("%", 493, 28, 28, 29, 12F, FontStyle.Bold, MutedColor));
            slider.SetBounds(20, 87, 504, 34);
            languageBindings.Add(delegate { slider.AccessibleName = Ui.Text("Microphone volume", "Уровень микрофона"); });
            slider.ValueChanged += delegate
            {
                if (syncing) return;
                syncing = true;
                level.Value = slider.Value / 10M;
                syncing = false;
            };
            volumeCard.Controls.Add(slider);
            volumeCard.Controls.Add(MakeLabel("0%", 20, 124, 60, 21, 8F, FontStyle.Regular, MutedColor));
            Label maximum = MakeLabel("100%", 455, 124, 69, 21, 8F, FontStyle.Regular, MutedColor);
            maximum.TextAlign = ContentAlignment.TopRight;
            volumeCard.Controls.Add(maximum);
            Controls.Add(volumeCard);

            BindText(startup, "Start with Windows", "Запускать вместе с Windows");
            startup.SetBounds(31, 452, 530, 29);
            startup.FlatStyle = FlatStyle.Flat;
            startup.ForeColor = TextColor;
            startup.Checked = Startup.Enabled;
            Controls.Add(startup);
            note.SetBounds(31, 489, 538, 43);
            note.Font = MakeFont(9F, FontStyle.Regular);
            note.ForeColor = MutedColor;
            Controls.Add(note);
            Controls.Add(MakeLabel("arahizok", 31, 553, 200, 26, 9F, FontStyle.Regular, MutedColor));
            apply = MakeButton("Save", new Rectangle(394, 545, 178, 40), true, "Сохранить");
            apply.Click += SaveClicked;
            Controls.Add(apply);
            AcceptButton = apply;
            RefreshDevices();
            ApplyLanguage();
            Ui.LanguageChanged += LanguageChanged;
            deviceCard.TabIndex = 0; devices.TabIndex = 0; refresh.TabIndex = 1;
            volumeCard.TabIndex = 1; level.TabIndex = 0; slider.TabIndex = 1;
            startup.TabIndex = 2; apply.TabIndex = 3;
        }

        private Font MakeFont(float size, FontStyle style)
        {
            Font value = new Font("Segoe UI", size, style);
            ownedFonts.Add(value);
            return value;
        }
        private static Bitmap LoadCatPicture()
        {
            // Read the original 256px PNG frame directly, retaining its colour data.
            using (Stream resource = typeof(Program).Assembly.GetManifestResourceStream("MicNeverDown.Cat.ico"))
            using (BinaryReader reader = new BinaryReader(resource))
            {
                reader.ReadUInt16(); reader.ReadUInt16();
                int count = reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    int width = reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
                    reader.ReadUInt16(); reader.ReadUInt16();
                    int length = reader.ReadInt32(); int offset = reader.ReadInt32();
                    if (width != 0) continue;
                    resource.Position = offset;
                    using (MemoryStream png = new MemoryStream(reader.ReadBytes(length)))
                    using (Image image = Image.FromStream(png)) return new Bitmap(image);
                }
                throw new InvalidDataException(Ui.Text("Cat image not found.", "Не найдено изображение кота."));
            }
        }
        private Label MakeLabel(string text, int x, int y, int width, int height, float size, FontStyle style, Color color, string russian = null)
        {
            Label label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), Font = MakeFont(size, style), ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false };
            if (russian != null) BindText(label, text, russian);
            return label;
        }
        private Button MakeButton(string text, Rectangle bounds, bool primary, string russian = null)
        {
            Button button = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : MutedColor, Font = MakeFont(primary ? 10F : 9F, primary ? FontStyle.Bold : FontStyle.Regular), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            if (russian != null) BindText(button, text, russian);
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(57, 72, 93);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(167, 243, 221) : Color.FromArgb(37, 48, 65);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(111, 205, 177) : Color.FromArgb(45, 58, 77);
            return button;
        }
        private void DrawDevice(object sender, DrawItemEventArgs e)
        {
            if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brush = new SolidBrush(selected ? Color.FromArgb(44, 66, 78) : devices.BackColor)) e.Graphics.FillRectangle(brush, e.Bounds);
            AudioDeviceInfo item = e.Index >= 0 && e.Index < devices.Items.Count ? devices.Items[e.Index] as AudioDeviceInfo : null;
            string text = item != null ? item.Name : Ui.Text("No microphones connected", "Нет подключённых микрофонов");
            Rectangle textBounds = new Rectangle(e.Bounds.X + 9, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, devices.Font, textBounds, TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
        private void SaveClicked(object sender, EventArgs args)
        {
            try
            {
                AudioDeviceInfo selected = devices.SelectedItem as AudioDeviceInfo;
                if (selected == null) throw new InvalidOperationException(Ui.Text("Select a connected microphone.", "Выберите подключённый микрофон."));
                Settings value = new Settings { DeviceId = selected.Id, DeviceName = selected.Name, Level = (float)level.Value / 100f };
                Storage.Validate(value);
                save(value, startup.Checked);
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Ui.Text("Could not save", "Не удалось сохранить"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void RefreshDevices()
        {
            try
            {
                List<AudioDeviceInfo> list = AudioDevices.ListCapture();
                devices.DataSource = list;
                if (existing != null)
                {
                    AudioDeviceInfo selected = list.Find(delegate(AudioDeviceInfo item) { return item.Id == existing.DeviceId; });
                    if (selected != null) { devices.SelectedItem = selected; level.Value = (decimal)(Math.Round(existing.Level * 1000) / 10.0); }
                }
                else
                {
                    AudioDeviceInfo selected = list.Find(delegate(AudioDeviceInfo item) { return item.IsCommunications; });
                    if (selected != null) devices.SelectedItem = selected;
                }
                apply.Enabled = list.Count > 0;
                noDevices = list.Count == 0;
                deviceError = null;
                UpdateNote();
            }
            catch (Exception ex) { apply.Enabled = false; deviceError = ex.Message; UpdateNote(); }
        }
        private void BindText(Control control, string english, string russian)
        {
            Action update = delegate { control.Text = Ui.Text(english, russian); };
            languageBindings.Add(update);
            update();
        }
        private void LanguageChanged(object sender, EventArgs args) { ApplyLanguage(); }
        private void ApplyLanguage()
        {
            // Commit valid in-progress numeric input using its previous language.
            level.CommitPendingInput(previousCulture);
            foreach (Action update in languageBindings) update();
            previousCulture = Ui.Culture;
            level.RefreshDisplay();
            UpdateNote();
            devices.Invalidate();
        }
        private void UpdateNote()
        {
            note.Text = deviceError != null ? Ui.Text("Could not load microphones: ", "Не удалось загрузить микрофоны: ") + deviceError : noDevices ?
                Ui.Text("Connect your headset and click Refresh.", "Подключите гарнитуру и нажмите «Обновить».") :
                Ui.Text("No audio is recorded. Mute works as usual.\nClose this window to keep the app in the tray.", "Звук не записывается. Mute работает как обычно.\nПосле закрытия окна программа остаётся в трее.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Ui.LanguageChanged -= LanguageChanged;
            base.Dispose(disposing);
            if (disposing)
            {
                if (catPicture != null) catPicture.Dispose();
                foreach (Font font in ownedFonts) font.Dispose();
                ownedFonts.Clear();
            }
        }
    }

    internal sealed class LocalizedNumericUpDown : NumericUpDown
    {
        public void CommitPendingInput(CultureInfo previousCulture)
        {
            decimal pending;
            bool valid = Decimal.TryParse(Text, NumberStyles.Number, previousCulture, out pending) && pending >= Minimum && pending <= Maximum;
            // NumericUpDown would otherwise reparse UserEdit text in the new
            // thread culture inside its Value setter (e.g. 41,2 becomes 412).
            UserEdit = false;
            if (valid) Value = pending;
        }
        public void RefreshDisplay() { UpdateEditText(); }
    }

    internal sealed class RoundedCard : Panel
    {
        public RoundedCard() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float r = 16F * DeviceDpi / 96F;
            RectangleF bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen border = new Pen(Color.FromArgb(40, 51, 69)))
            {
                path.AddArc(bounds.Left, bounds.Top, r * 2, r * 2, 180, 90);
                path.AddArc(bounds.Right - r * 2, bounds.Top, r * 2, r * 2, 270, 90);
                path.AddArc(bounds.Right - r * 2, bounds.Bottom - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class VolumeSlider : Control
    {
        private int value;
        public event EventHandler ValueChanged;
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(1000, value));
                if (this.value == next) return;
                this.value = next;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
        public VolumeSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Slider;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibleObject(this); }
        private sealed class SliderAccessibleObject : ControlAccessibleObject
        {
            private readonly VolumeSlider owner;
            public SliderAccessibleObject(VolumeSlider owner) : base(owner) { this.owner = owner; }
            public override string Value
            {
                get { return (owner.Value / 10M).ToString("0.0") + "%"; }
                set { decimal number; if (Decimal.TryParse(value.TrimEnd('%'), out number)) owner.Value = (int)(Math.Max(0M, Math.Min(100M, number)) * 10M); }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96F;
            float inset = 11 * scale;
            float y = Height / 2F;
            float x = inset + (Width - inset * 2) * value / 1000F;
            using (Pen track = new Pen(Color.FromArgb(55, 70, 91), 5 * scale))
            using (Pen fill = new Pen(Color.FromArgb(139, 231, 204), 5 * scale))
            using (SolidBrush knob = new SolidBrush(Color.FromArgb(190, 255, 234)))
            {
                track.StartCap = track.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, inset, y, Width - inset, y);
                if (value > 0) e.Graphics.DrawLine(fill, inset, y, x, y);
                e.Graphics.FillEllipse(knob, x - 8 * scale, y - 8 * scale, 16 * scale, 16 * scale);
                if (Focused) using (Pen focus = new Pen(Color.FromArgb(139, 231, 204), scale)) e.Graphics.DrawEllipse(focus, x - 11 * scale, y - 11 * scale, 22 * scale, 22 * scale);
            }
        }
        private void SetFromMouse(int x)
        {
            float inset = 11 * DeviceDpi / 96F;
            Value = (int)Math.Round((x - inset) * 1000F / Math.Max(1F, Width - inset * 2));
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromMouse(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture && e.Button == MouseButtons.Left) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
        protected override bool IsInputKey(Keys keyData) { Keys key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= 1;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += 1;
            else if (e.KeyCode == Keys.PageDown) Value -= 100;
            else if (e.KeyCode == Keys.PageUp) Value += 100;
            else if (e.KeyCode == Keys.Home) Value = 0;
            else if (e.KeyCode == Keys.End) Value = 1000;
            else return;
            e.Handled = e.SuppressKeyPress = true;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
