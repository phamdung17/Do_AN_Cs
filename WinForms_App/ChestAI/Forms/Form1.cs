 using ChestAI.Models;

namespace ChestAI.Forms;

/// <summary>
/// Form đăng ký bệnh án chẩn đoán mới.
/// Wizard 3 bước: (1) Chọn bệnh nhân → (2) Nhập EHR Vitals → (3) Upload ảnh + Clinical Notes
/// Sau khi submit → mở DiagnosisResultForm với kết quả AI đầy đủ.
/// </summary>
public class NewDiagnosisForm : Form
{
    // ── Trạng thái wizard ────────────────────────────────────────────────────
    private int _step = 0;   // 0, 1, 2
    private Patient? _patient;
    private string? _imagePath;

    // ── Step 0: Chọn bệnh nhân ───────────────────────────────────────────────
    private Panel pnlStep0 = null!;
    private TextBox txtSearch = null!;
    private DataGridView dgvPatients = null!;
    private Button btnAddNew = null!;
    private List<Patient> _patientList = [];

    // ── Step 1: EHR Vitals ────────────────────────────────────────────────────
    private Panel pnlStep1 = null!;
    private NumericUpDown numAge = null!;
    private ComboBox cmbGender = null!;
    private CheckBox chkViewPA = null!;
    private CheckBox chkSmoker = null!;
    private NumericUpDown numHR = null!;
    private NumericUpDown numSpO2 = null!;
    private NumericUpDown numTemp = null!;
    private NumericUpDown numSBP = null!;
    private NumericUpDown numRR = null!;
    private NumericUpDown numWBC = null!;
    private NumericUpDown numCRP = null!;
    private NumericUpDown numLactate = null!;

    // ── Step 2: Ảnh + Notes ───────────────────────────────────────────────────
    private Panel pnlStep2 = null!;
    private PictureBox pbPreview = null!;
    private Label lblFileName = null!;
    private TextBox txtNotes = null!;

    // ── Shared controls ───────────────────────────────────────────────────────
    private Label lblStepTitle = null!;
    private Panel pnlStepDot = null!;   // step indicators
    private Button btnPrev = null!;
    private Button btnNext = null!;
    private Label lblPatientBadge = null!;

    // ── Colors (dark theme) ───────────────────────────────────────────────────
    private static readonly Color BgDark = Color.FromArgb(15, 23, 42);
    private static readonly Color BgCard = Color.FromArgb(30, 41, 59);
    private static readonly Color Accent = Color.FromArgb(56, 189, 248);
    private static readonly Color Green = Color.FromArgb(74, 222, 128);
    private static readonly Color Purple = Color.FromArgb(124, 58, 237);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextGray = Color.FromArgb(148, 163, 184);

    public NewDiagnosisForm()
    {
        Build();
        GotoStep(0);
        LoadPatients();
        CheckServer();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  BUILD FORM
    // ═════════════════════════════════════════════════════════════════════════
    private void Build()
    {
        Text = "Đăng Ký Bệnh Án – ChestAI";
        Size = new Size(820, 680);
        MinimumSize = new Size(820, 680);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BgDark;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        // ── Header strip ─────────────────────────────────────────────────────
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(9, 17, 34)
        };

        var lblTitle = new Label
        {
            Text = "📋  Đăng Ký Bệnh Án Chẩn Đoán Mới",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(20, 14)
        };

        lblPatientBadge = new Label
        {
            Text = "Bệnh nhân: chưa chọn",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = TextGray,
            AutoSize = true,
            Location = new Point(22, 48)
        };

        // Step indicators (3 dots)
        pnlStepDot = new Panel
        {
            Size = new Size(200, 14),
            Location = new Point(580, 33),
            BackColor = Color.Transparent
        };
        RebuildDots(0);

        pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblPatientBadge, pnlStepDot });

        // ── Step title label ─────────────────────────────────────────────────
        lblStepTitle = new Label
        {
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = TextWhite,
            AutoSize = true,
            Location = new Point(20, 95)
        };

        // ── Build 3 step panels ───────────────────────────────────────────────
        pnlStep0 = BuildStep0();
        pnlStep1 = BuildStep1();
        pnlStep2 = BuildStep2();

        foreach (var p in new[] { pnlStep0, pnlStep1, pnlStep2 })
        {
            p.Location = new Point(20, 125);
            p.Size = new Size(770, 460);
            p.Visible = false;
            Controls.Add(p);
        }

        // ── Bottom nav bar ────────────────────────────────────────────────────
        var pnlNav = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Color.FromArgb(9, 17, 34)
        };

        btnPrev = MakeBtn("← Quay lại", BgCard, new Point(20, 13), 140);
        btnNext = MakeBtn("Tiếp theo →", Purple, new Point(640, 13), 150);
        btnPrev.Click += (s, e) => GotoStep(_step - 1);
        btnNext.Click += BtnNext_Click;

        pnlNav.Controls.AddRange(new Control[] { btnPrev, btnNext });

        Controls.AddRange(new Control[] { pnlHeader, lblStepTitle, pnlNav });
    }

    // ─── STEP 0: Chọn bệnh nhân ──────────────────────────────────────────────
    private Panel BuildStep0()
    {
        var pnl = new Panel { BackColor = Color.Transparent };

        // Search bar
        txtSearch = new TextBox
        {
            Location = new Point(0, 0),
            Width = 580,
            Height = 30,
            Font = new Font("Segoe UI", 10),
            PlaceholderText = "🔍 Tìm theo họ tên bệnh nhân...",
            BackColor = BgCard,
            ForeColor = TextWhite
        };
        txtSearch.TextChanged += async (s, e) =>
        {
            await Task.Delay(300);
            _patientList = string.IsNullOrWhiteSpace(txtSearch.Text)
                ? DatabaseService.GetAllPatients()
                : DatabaseService.SearchPatients(txtSearch.Text);
            BindPatientGrid();
        };

        btnAddNew = MakeBtn("➕ Thêm mới", Green, new Point(590, 0), 160);
        btnAddNew.Click += (s, e) =>
        {
            var dlg = new PatientEditDialog(null);
            if (dlg.ShowDialog() == DialogResult.OK) LoadPatients();
        };

        // DataGridView
        dgvPatients = new DataGridView
        {
            Location = new Point(0, 42),
            Size = new Size(750, 360),
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            BackgroundColor = BgCard,
            GridColor = Color.FromArgb(51, 65, 85),
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgDark,
                ForeColor = Accent,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgCard,
                ForeColor = TextWhite,
                SelectionBackColor = Color.FromArgb(56, 189, 248),
                SelectionForeColor = Color.Black
            },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        dgvPatients.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ID", HeaderText = "ID", Width = 55 },
            new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Họ tên", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "Age", HeaderText = "Tuổi", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Gender", HeaderText = "Giới", Width = 70 },
            new DataGridViewTextBoxColumn { Name = "Phone", HeaderText = "SĐT", FillWeight = 20 },
            new DataGridViewTextBoxColumn { Name = "Hist", HeaderText = "Tiền sử", FillWeight = 30 }
        );
        dgvPatients.CellDoubleClick += (s, e) => SelectPatientFromGrid();
        dgvPatients.SelectionChanged += (s, e) =>
        {
            var p = GetPatientFromGrid();
            if (p != null) PreviewPatient(p);
        };

        pnl.Controls.AddRange(new Control[] { txtSearch, btnAddNew, dgvPatients });
        return pnl;
    }

    // ─── STEP 1: EHR Vitals ──────────────────────────────────────────────────
    private Panel BuildStep1()
    {
        var pnl = new Panel { BackColor = Color.Transparent, AutoScroll = true };

        // Left column: demographics
        var pnlLeft = BuildCard(0, 0, 360, 440, "🧑 Thông tin nhân khẩu học");
        var pnlRight = BuildCard(375, 0, 370, 440, "🩺 Dấu hiệu sinh tồn & Xét nghiệm");

        // Demographics
        int y = 45;
        numAge = MakeNumericFull(1, 120, 40, pnlLeft, "Tuổi", ref y, suffix: " tuổi");
        cmbGender = new ComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgCard,
            ForeColor = TextWhite,
            Font = new Font("Segoe UI", 10)
        };
        cmbGender.Items.AddRange(["Nam (M)", "Nữ (F)"]);
        cmbGender.SelectedIndex = 0;
        AddRow(pnlLeft, "Giới tính", cmbGender, ref y);

        chkViewPA = new CheckBox
        {
            Text = "Chụp thẳng (PA view)",
            ForeColor = TextWhite,
            Font = new Font("Segoe UI", 9),
            Checked = true,
            AutoSize = true,
            Location = new Point(15, y)
        };
        pnlLeft.Controls.Add(chkViewPA); y += 30;

        chkSmoker = new CheckBox
        {
            Text = "Tiền sử hút thuốc lá",
            ForeColor = TextWhite,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(15, y)
        };
        pnlLeft.Controls.Add(chkSmoker);

        // Vitals
        y = 45;
        numHR = MakeNumericFull(30, 250, 75, pnlRight, "Nhịp tim – HR (bpm)", ref y);
        numSpO2 = MakeNumericFull(50, 100, 97, pnlRight, "Độ bão hoà O₂ – SpO₂ (%)", ref y);
        numTemp = MakeNumericFull(340, 420, 370, pnlRight, "Nhiệt độ (×10, vd 370→37.0°)", ref y);
        numSBP = MakeNumericFull(60, 260, 120, pnlRight, "Huyết áp tâm thu – SBP (mmHg)", ref y);
        numRR = MakeNumericFull(4, 60, 18, pnlRight, "Nhịp thở – RR (/phút)", ref y);
        numWBC = MakeNumericFull(5, 500, 70, pnlRight, "Bạch cầu – WBC (×10 K/μL)", ref y);
        numCRP = MakeNumericFull(0, 5000, 50, pnlRight, "CRP (×10 mg/L)", ref y);
        numLactate = MakeNumericFull(0, 200, 10, pnlRight, "Lactate (×10 mmol/L)", ref y);

        // Reference ranges hint
        var lblHint = new Label
        {
            Text = "💡 Giá trị bình thường: HR 60-100 | SpO₂ ≥95 | SBP 90-140 | RR 12-20",
            ForeColor = TextGray,
            Font = new Font("Segoe UI", 8, FontStyle.Italic),
            Location = new Point(0, 450),
            AutoSize = true
        };

        pnl.Controls.AddRange(new Control[] { pnlLeft, pnlRight, lblHint });
        return pnl;
    }

    // ─── STEP 2: Ảnh X-quang + Clinical Notes ────────────────────────────────
    private Panel BuildStep2()
    {
        var pnl = new Panel { BackColor = Color.Transparent };

        // Image upload area
        var pnlImg = BuildCard(0, 0, 360, 440, "🩻 Ảnh X-Quang Ngực");

        pbPreview = new PictureBox
        {
            Location = new Point(10, 45),
            Size = new Size(330, 300),
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.None
        };
        pbPreview.AllowDrop = true;
        pbPreview.DragEnter += (s, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        pbPreview.DragDrop += (s, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (files?.Length > 0) LoadImagePreview(files[0]);
        };

        var lblDrop = new Label
        {
            Text = "Kéo thả ảnh vào đây\n(.jpg / .png / .dcm)",
            ForeColor = Color.FromArgb(71, 85, 105),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            Size = new Size(330, 300),
            Location = new Point(10, 45)
        };

        var btnBrowse = MakeBtn("📂  Duyệt file ảnh", Color.FromArgb(59, 130, 246),
            new Point(10, 355), 160);
        btnBrowse.Click += (s, e) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Chọn ảnh X-quang",
                Filter = "Ảnh (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|DICOM (*.dcm)|*.dcm|Tất cả|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                LoadImagePreview(dlg.FileName);
        };

        lblFileName = new Label
        {
            Text = "Chưa chọn file",
            ForeColor = TextGray,
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            Location = new Point(10, 400),
            Width = 340,
            AutoEllipsis = true
        };

        pnlImg.Controls.AddRange(new Control[] { lblDrop, pbPreview, btnBrowse, lblFileName });

        // Clinical Notes area
        var pnlNotes = BuildCard(375, 0, 370, 440, "📝 Ghi Chú Lâm Sàng (ClinicalBERT)");

        var lblNotesHint = new Label
        {
            Text = "Mô tả triệu chứng, tiền sử, kết quả khám.\nVăn bản này được mã hoá bởi Bio_ClinicalBERT\nvà kết hợp với ảnh X-quang + EHR để chẩn đoán.",
            ForeColor = TextGray,
            Font = new Font("Segoe UI", 8, FontStyle.Italic),
            Location = new Point(12, 45),
            Width = 340,
            Height = 60,
            AutoEllipsis = false
        };

        txtNotes = new TextBox
        {
            Location = new Point(12, 115),
            Size = new Size(340, 240),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = BgDark,
            ForeColor = TextWhite,
            Font = new Font("Segoe UI", 9.5f),
            PlaceholderText =
                "Ví dụ:\n" +
                "Patient is a 55-year-old male with progressive dyspnea " +
                "and productive cough for 3 weeks. History of 30 pack-year " +
                "smoking. Fever 38.5°C. Decreased breath sounds on the right base."
        };

        // Templates quick-insert
        var lblTpl = new Label
        {
            Text = "Mẫu nhanh:",
            ForeColor = TextGray,
            Font = new Font("Segoe UI", 8),
            Location = new Point(12, 365),
            AutoSize = true
        };

        string[] templates =
        [
            "Ho, khó thở, sốt",
            "Đau ngực, ho ra máu",
            "Khó thở khi gắng sức"
        ];

        int tx = 12;
        foreach (var tpl in templates)
        {
            var btn = new Button
            {
                Text = tpl,
                Location = new Point(tx, 385),
                AutoSize = true,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = BgDark,
                ForeColor = Accent,
                Font = new Font("Segoe UI", 8),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Accent;
            string cap = tpl;
            btn.Click += (s, e) => txtNotes.AppendText((txtNotes.Text.Length > 0 ? " " : "") + cap + ".");
            pnlNotes.Controls.Add(btn);
            tx += btn.PreferredSize.Width + 6;
        }

        pnlNotes.Controls.AddRange(new Control[] { lblNotesHint, txtNotes, lblTpl });
        pnl.Controls.AddRange(new Control[] { pnlImg, pnlNotes });
        return pnl;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  WIZARD NAVIGATION
    // ═════════════════════════════════════════════════════════════════════════
    private static readonly string[] StepTitles =
    [
        "Bước 1/3 — Chọn bệnh nhân",
        "Bước 2/3 — Nhập thông số lâm sàng (EHR Vitals)",
        "Bước 3/3 — Upload ảnh X-quang & ghi chú lâm sàng"
    ];

    private void GotoStep(int step)
    {
        if (step < 0 || step > 2) return;
        _step = step;

        pnlStep0.Visible = step == 0;
        pnlStep1.Visible = step == 1;
        pnlStep2.Visible = step == 2;

        lblStepTitle.Text = StepTitles[step];
        btnPrev.Enabled = step > 0;
        btnNext.Text = step < 2 ? "Tiếp theo →" : "🔬 Bắt đầu chẩn đoán";
        btnNext.BackColor = step < 2 ? Purple : Color.FromArgb(5, 150, 105);

        RebuildDots(step);
    }

    private void RebuildDots(int current)
    {
        pnlStepDot.Controls.Clear();
        for (int i = 0; i < 3; i++)
        {
            bool active = i == current;
            bool done = i < current;
            var dot = new Panel
            {
                Size = new Size(active ? 24 : 12, 12),
                Location = new Point(i * 36, 1),
                BackColor = done ? Green
                           : active ? Accent
                           : Color.FromArgb(51, 65, 85)
            };
            pnlStepDot.Controls.Add(dot);
            var lbl = new Label
            {
                Text = (i + 1).ToString(),
                ForeColor = active || done ? Color.Black : TextGray,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                Size = dot.Size,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = dot.Location
            };
            pnlStepDot.Controls.Add(lbl);
        }
    }

    private async void BtnNext_Click(object? sender, EventArgs e)
    {
        if (_step == 0)
        {
            if (_patient is null)
            {
                SelectPatientFromGrid();
                if (_patient is null)
                {
                    ShowWarn("Vui lòng chọn một bệnh nhân từ danh sách.");
                    return;
                }
            }
            GotoStep(1);
        }
        else if (_step == 1)
        {
            GotoStep(2);
        }
        else   // Step 2 → Run diagnosis
        {
            if (_imagePath is null)
            {
                ShowWarn("Vui lòng chọn hoặc kéo thả ảnh X-quang.");
                return;
            }
            await RunDiagnosisAsync();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LOGIC
    // ═════════════════════════════════════════════════════════════════════════
    private void LoadPatients()
    {
        try
        {
            _patientList = DatabaseService.GetAllPatients();
            BindPatientGrid();
        }
        catch { /* DB chưa sẵn sàng */ }
    }

    private void BindPatientGrid()
    {
        dgvPatients.Rows.Clear();
        foreach (var p in _patientList)
            dgvPatients.Rows.Add(
                p.PatientID, p.FullName, p.Age,
                p.Gender == 'M' ? "Nam" : "Nữ",
                p.Phone ?? "—",
                string.IsNullOrEmpty(p.MedicalHistory) ? "—" : p.MedicalHistory[..Math.Min(30, p.MedicalHistory.Length)]
            );
    }

    private Patient? GetPatientFromGrid()
    {
        if (dgvPatients.SelectedRows.Count == 0) return null;
        int id = Convert.ToInt32(dgvPatients.SelectedRows[0].Cells["ID"].Value);
        return _patientList.Find(p => p.PatientID == id);
    }

    private void SelectPatientFromGrid()
    {
        var p = GetPatientFromGrid();
        if (p is null) return;
        _patient = p;
        PreviewPatient(p);
    }

    private void PreviewPatient(Patient p)
    {
        lblPatientBadge.Text = $"Bệnh nhân: {p.FullName}  |  {(p.Gender == 'M' ? "Nam" : "Nữ")}, {p.Age} tuổi  |  ID: {p.PatientID}";
        lblPatientBadge.ForeColor = Accent;
        // Điền sẵn Age/Gender vào step 1
        if (numAge != null) numAge.Value = Math.Min(Math.Max(p.Age, 1), 120);
        if (cmbGender != null) cmbGender.SelectedIndex = p.Gender == 'M' ? 0 : 1;
        if (chkSmoker != null) chkSmoker.Checked = p.IsSmoker;
    }

    private void LoadImagePreview(string path)
    {
        _imagePath = path;
        try
        {
            pbPreview.Image = Image.FromFile(path);
        }
        catch
        {
            pbPreview.Image = null;
        }
        lblFileName.Text = Path.GetFileName(path);
        lblFileName.ForeColor = Green;
    }

    private async Task RunDiagnosisAsync()
    {
        btnNext.Enabled = false;
        btnNext.Text = "⏳ Đang chẩn đoán...";

        try
        {
            var vitals = new EhrVitals
            {
                HeartRate = (float)numHR.Value,
                SpO2 = (float)numSpO2.Value,
                Temperature = (float)numTemp.Value / 10f,
                SBP = (float)numSBP.Value,
                RespiratoryRate = (float)numRR.Value,
                WBC = (float)numWBC.Value / 10f,
                CRP = (float)numCRP.Value / 10f,
                Lactate = (float)numLactate.Value / 10f,
            };
            string notes = txtNotes.Text.Trim();

            // Gọi đồng thời 3 APIs
            var tPredict = ApiService.PredictAsync(_imagePath!, _patient!, vitals, notes);
            var tSegment = ApiService.SegmentAsync(_imagePath!);

            await Task.WhenAll(tPredict, tSegment);

            var predict = tPredict.Result;
            var segment = tSegment.Result;

            // Lấy class index cho explain & SHAP
            int classIdx = predict.diagnoses
                .Select((d, i) => (d, i))
                .OrderByDescending(x => x.d.Probability)
                .FirstOrDefault().i;

            var tExplain = ApiService.ExplainAsync(_imagePath!, _patient!, classIdx, vitals);
            var tShap = ApiService.ShapAsync(_imagePath!, _patient!, classIdx, vitals, notes);
            await Task.WhenAll(tExplain, tShap);

            // Mở form kết quả
            var resultForm = new DiagnosisResultForm(
                _patient!, _imagePath!, vitals, predict, segment,
                tExplain.Result, tShap.Result);
            resultForm.Show(this);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi chẩn đoán:\n{ex.Message}", "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnNext.Enabled = true;
            btnNext.Text = "🔬 Bắt đầu chẩn đoán";
        }
    }

    private async void CheckServer()
    {
        bool ok = await ApiService.IsServerAliveAsync();
        if (!ok)
            MessageBox.Show(
                "⚠️ Không kết nối được AI Server (localhost:8000).\n" +
                "Hãy khởi động AI_Server/main.py trước khi chẩn đoán.",
                "Cảnh báo server", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════════
    private static Panel BuildCard(int x, int y, int w, int h, string title)
    {
        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = Color.FromArgb(30, 41, 59)
        };
        var bar = new Panel
        { Size = new Size(w, 4), Location = new Point(0, 0), BackColor = Color.FromArgb(56, 189, 248) };
        var lbl = new Label
        {
            Text = title,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(12, 10)
        };
        card.Controls.AddRange(new Control[] { bar, lbl });
        return card;
    }

    private static NumericUpDown MakeNumericFull(
        int min, int max, int val, Panel parent, string label, ref int y, string suffix = "")
    {
        var lbl = new Label
        {
            Text = label,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(12, y + 2)
        };
        var num = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = val,
            Location = new Point(200, y),
            Width = 100,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        parent.Controls.AddRange(new Control[] { lbl, num });
        y += 40;
        return num;
    }

    private static void AddRow(Panel parent, string label, Control ctrl, ref int y)
    {
        parent.Controls.Add(new Label
        {
            Text = label,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(12, y + 2)
        });
        ctrl.Location = new Point(200, y);
        parent.Controls.Add(ctrl);
        y += 42;
    }

    private static Button MakeBtn(string text, Color bg, Point loc, int w) => new()
    {
        Text = text,
        Location = loc,
        Size = new Size(w, 36),
        FlatStyle = FlatStyle.Flat,
        BackColor = bg,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Cursor = Cursors.Hand
    };

    private static void ShowWarn(string msg) =>
        MessageBox.Show(msg, "Cần thêm thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void InitializeComponent()
    {

    }
}
