using ChestAI.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;

namespace ChestAI.Forms;

/// <summary>
/// Panel chẩn đoán AI cho một bệnh nhân cụ thể.
/// Bố cục: Trái = ảnh X-quang + kết quả AI | Phải = Tab (Detection / Segmentation / Heatmap / EHR / SHAP / So sánh / Lịch sử)
/// </summary>
public class DiagnosisPanel : Panel
{
    private readonly Patient _patient;

    // Cột trái
    private PictureBox  pbOriginal    = null!;
    private Label       lblStatus     = null!;
    private Button      btnLoad       = null!;
    private Button      btnDiagnose   = null!;
    private Label       lblResult     = null!;
    private Label       lblProb       = null!;
    private ProgressBar pbarProb      = null!;
    private Label       lblRationale  = null!;   // Textual rationale
    private Label       lblIcd        = null!;   // ICD code

    // Cột phải – Tabs
    private TabControl  tabRight       = null!;
    private PictureBox  pbDetection    = null!;
    private PictureBox  pbSegment      = null!;
    private PictureBox  pbHeatmap      = null!;
    private PictureBox  pbCompareOrig  = null!;   // So sánh gốc
    private PictureBox  pbCompareSeg   = null!;   // So sánh segmentation
    private DataGridView dgvResults    = null!;
    private DataGridView dgvHistory    = null!;
    private TextBox      txtConclusion  = null!;
    private Button       btnConfirm    = null!;

    // EHR Vitals Input controls
    private NumericUpDown numHR    = null!;
    private NumericUpDown numSpO2  = null!;
    private NumericUpDown numTemp  = null!;
    private NumericUpDown numSBP   = null!;
    private NumericUpDown numRR    = null!;
    private NumericUpDown numWBC   = null!;
    private NumericUpDown numCRP   = null!;
    private NumericUpDown numLactate = null!;
    private TextBox      txtClinicalNotes = null!;

    // SHAP chart
    private CartesianChart? _shapChart;

    private string?              _currentImagePath;
    private PredictApiResponse?  _lastPredict;
    private ShapApiResponse?     _lastShap;
    private int                  _lastDiagnosisId;

    public DiagnosisPanel(Patient patient)
    {
        _patient = patient;
        InitializeComponent();
        LoadHistory();
        CheckServerStatus();
    }

    private void InitializeComponent()
    {
        BackColor = Color.FromArgb(15, 23, 42);

        // ── Header ────────────────────────────────────────────────────────────
        var lblHeader = new Label
        {
            Text      = $"Chẩn đoán AI  —  {_patient.FullName}  " +
                        $"({(_patient.Gender == 'M' ? "Nam" : "Nữ")}, {_patient.Age} tuổi)",
            Font      = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(0, 8)
        };

        lblStatus = new Label
        {
            Text      = "⚙ Đang kiểm tra kết nối server AI...",
            Font      = new Font("Segoe UI", 8, FontStyle.Italic),
            ForeColor = Color.Silver,
            AutoSize  = true,
            Location  = new Point(0, 38)
        };

        // ── Cột trái (420px) ──────────────────────────────────────────────────
        var pnlLeft = new Panel
        {
            Location  = new Point(0, 60),
            Width     = 420,
            BackColor = Color.FromArgb(30, 41, 59)
        };

        pbOriginal = new PictureBox
        {
            Location  = new Point(10, 10),
            Size      = new Size(400, 400),
            BackColor = Color.Black,
            SizeMode  = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.None
        };
        // Kéo thả file ảnh
        pbOriginal.AllowDrop = true;
        pbOriginal.DragEnter += (s, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        pbOriginal.DragDrop += (s, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (files?.Length > 0) LoadImage(files[0]);
        };

        var lblDrop = new Label
        {
            Text      = "Kéo thả ảnh X-quang\nvào đây hoặc nhấn nút bên dưới",
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 9, FontStyle.Italic),
            Size      = new Size(400, 400),
            Location  = new Point(10, 10)
        };

        btnLoad = MakeBtn("📂 Chọn ảnh X-quang", Color.FromArgb(59,130,246), new Point(10, 420), 190);
        btnDiagnose = MakeBtn("🔬 Bắt đầu chẩn đoán", Color.FromArgb(5,150,105), new Point(210, 420), 200);
        btnDiagnose.Enabled = false;
        btnLoad.Click    += BtnLoad_Click;
        btnDiagnose.Click += BtnDiagnose_Click;

        // Kết quả nhanh
        lblResult = new Label
        {
            Text      = "—",
            Font      = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(10, 465)
        };
        lblProb = new Label
        {
            Text      = "Xác suất: —",
            Font      = new Font("Segoe UI", 9),
            ForeColor = Color.Silver,
            AutoSize  = true,
            Location  = new Point(10, 500)
        };
        pbarProb = new ProgressBar
        {
            Location  = new Point(10, 520),
            Size      = new Size(400, 18),
            Style     = ProgressBarStyle.Continuous,
            Value     = 0
        };

        pnlLeft.Controls.AddRange(new Control[]
        { lblDrop, pbOriginal, btnLoad, btnDiagnose, lblResult, lblProb, pbarProb });
        pnlLeft.Height = 560;

        // ICD code label
        lblIcd = new Label
        {
            Text      = "ICD: —",
            Font      = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(56,189,248),
            AutoSize  = true,
            Location  = new Point(10, 545)
        };
        // Textual rationale (AI explanation short text)
        lblRationale = new Label
        {
            Text      = string.Empty,
            Font      = new Font("Segoe UI", 8, FontStyle.Italic),
            ForeColor = Color.Silver,
            Location  = new Point(10, 565),
            Size      = new Size(400, 60),
            AutoEllipsis = true
        };
        pnlLeft.Controls.AddRange(new Control[] { lblIcd, lblRationale });
        pnlLeft.Height = 640;

        // ── Tab phải ──────────────────────────────────────────────────────────
        tabRight = new TabControl
        {
            Location   = new Point(435, 60),
            Font       = new Font("Segoe UI", 9),
        };
        tabRight.Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                        | AnchorStyles.Left | AnchorStyles.Right;

        tabRight.TabPages.Add(BuildTabDetection());
        tabRight.TabPages.Add(BuildTabSegment());
        tabRight.TabPages.Add(BuildTabHeatmap());
        tabRight.TabPages.Add(BuildTabDiseases());
        tabRight.TabPages.Add(BuildTabVitals());     // EHR vitals input
        tabRight.TabPages.Add(BuildTabShap());       // SHAP XAI chart
        tabRight.TabPages.Add(BuildTabCompare());    // Split-view so sánh
        tabRight.TabPages.Add(BuildTabHistory());

        Controls.AddRange(new Control[] { lblHeader, lblStatus, pnlLeft, tabRight });

        SizeChanged += (s, e) =>
        {
            pnlLeft.Height    = Height - 65;
            tabRight.Size     = new Size(Width - 440, Height - 65);
        };
    }

    // ─── TABS ─────────────────────────────────────────────────────────────────
    private TabPage BuildTabDetection()
    {
        var tab = new TabPage("🟡 Detection (YOLO)") { BackColor = Color.FromArgb(15,23,42) };
        pbDetection = new PictureBox
        { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
        tab.Controls.Add(pbDetection);
        return tab;
    }

    private TabPage BuildTabSegment()
    {
        var tab = new TabPage("🔴 Segmentation") { BackColor = Color.FromArgb(15,23,42) };
        pbSegment = new PictureBox
        { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
        tab.Controls.Add(pbSegment);
        return tab;
    }

    private TabPage BuildTabHeatmap()
    {
        var tab = new TabPage("🌡️ Grad-CAM (XAI)") { BackColor = Color.FromArgb(15,23,42) };
        pbHeatmap = new PictureBox
        { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
        tab.Controls.Add(pbHeatmap);
        return tab;
    }

    private TabPage BuildTabDiseases()
    {
        var tab = new TabPage("📊 Xác suất bệnh") { BackColor = Color.FromArgb(15,23,42) };

        dgvResults = new DataGridView
        {
            Dock              = DockStyle.Top,
            Height            = 340,
            ReadOnly          = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            BackgroundColor   = Color.FromArgb(30,41,59),
            DefaultCellStyle  = new DataGridViewCellStyle
            { BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        dgvResults.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Rank",      Name = "rank",   Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "Bệnh",      Name = "disease", FillWeight = 25 },
            new DataGridViewTextBoxColumn { HeaderText = "ICD-10",    Name = "icd",    Width = 75 },
            new DataGridViewTextBoxColumn { HeaderText = "Xác suất %",Name = "prob",   Width = 100 }
        );

        // Nhận xét của bác sĩ
        var lblNote = new Label
        {
            Text = "Kết luận của bác sĩ:", ForeColor = Color.Silver,
            Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(5, 350)
        };
        txtConclusion = new TextBox
        {
            Location  = new Point(5, 372),
            Width     = 400, Height = 70, Multiline = true,
            BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9)
        };
        btnConfirm = new Button
        {
            Text = "✅ Xác nhận kết luận", Location = new Point(5, 450),
            Width = 200, Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(5,150,105), ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        btnConfirm.Click += BtnConfirm_Click;

        tab.Controls.AddRange(new Control[]
        { dgvResults, lblNote, txtConclusion, btnConfirm });
        return tab;
    }

    private TabPage BuildTabHistory()
    {
        var tab = new TabPage("📋 Lịch sử khám") { BackColor = Color.FromArgb(15,23,42) };
        dgvHistory = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            RowHeadersVisible = false,
            BackgroundColor   = Color.FromArgb(30,41,59),
            DefaultCellStyle  = new DataGridViewCellStyle
            { BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        dgvHistory.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Ngày",     Name = "date",     Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "Kết quả",  Name = "disease",  FillWeight = 30 },
            new DataGridViewTextBoxColumn { HeaderText = "ICD",      Name = "icd",      Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Xác suất", Name = "prob",     Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Bác sĩ",   Name = "doctor",   FillWeight = 20 },
            new DataGridViewTextBoxColumn { HeaderText = "Xác nhận", Name = "confirmed",Width = 90 }
        );
        tab.Controls.Add(dgvHistory);
        return tab;
    }

    // ─── TAB MỚI: EHR VITALS INPUT ────────────────────────────────────────────
    private TabPage BuildTabVitals()
    {
        var tab = new TabPage("🩺 EHR Vitals & Labs") { BackColor = Color.FromArgb(15,23,42) };
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true,
                              BackColor = Color.FromArgb(15,23,42) };

        var lblTitle = new Label
        {
            Text = "Thông số sinh tồn & Xét nghiệm (nhập trước khi chẩn đoán)",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = true, Location = new Point(10, 10)
        };
        pnl.Controls.Add(lblTitle);

        int y = 40;

        numHR      = MakeNumeric(10, 200, 75,  y);    pnl.Controls.AddRange(MakeVitalsRow("Nhịp tim (HR, bpm)", numHR, y)); y += 40;
        numSpO2    = MakeNumeric(70, 100, 97,  y);    pnl.Controls.AddRange(MakeVitalsRow("SpO₂ (%)", numSpO2, y)); y += 40;
        numTemp    = MakeNumeric(350, 420, 370, y);   pnl.Controls.AddRange(MakeVitalsRow("Nhiệt độ (°C ×10, 370=37.0°)", numTemp, y)); y += 40;
        numSBP     = MakeNumeric(70, 250, 120, y);    pnl.Controls.AddRange(MakeVitalsRow("Huyết áp tâm thu (mmHg)", numSBP, y)); y += 40;
        numRR      = MakeNumeric(5,  60,  18,  y);    pnl.Controls.AddRange(MakeVitalsRow("Nhịp thở (/phút)", numRR, y)); y += 40;
        numWBC     = MakeNumeric(10, 300, 70,  y);    pnl.Controls.AddRange(MakeVitalsRow("Bạch cầu WBC (K/μL ×10)", numWBC, y)); y += 40;
        numCRP     = MakeNumeric(0, 5000, 50,  y);    pnl.Controls.AddRange(MakeVitalsRow("CRP (mg/L ×10)", numCRP, y)); y += 40;
        numLactate = MakeNumeric(0, 200,  10,  y);    pnl.Controls.AddRange(MakeVitalsRow("Lactate (mmol/L ×10)", numLactate, y)); y += 50;

        // Clinical Notes
        var lblNotes = new Label
        {
            Text = "Ghi chú lâm sàng (Clinical Notes) – dùng bởi ClinicalBERT:",
            ForeColor = Color.Silver, Font = new Font("Segoe UI", 9),
            AutoSize = true, Location = new Point(10, y)
        };
        txtClinicalNotes = new TextBox
        {
            Location  = new Point(10, y + 22),
            Size      = new Size(440, 80),
            Multiline = true,
            BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9),
            PlaceholderText = "Ví dụ: Patient presents with cough, dyspnea. History of smoking for 20 years..."
        };
        pnl.Controls.AddRange(new Control[] { lblNotes, txtClinicalNotes });

        tab.Controls.Add(pnl);
        return tab;
    }

    private static Control[] MakeVitalsRow(string label, NumericUpDown num, int y)
    {
        var lbl = new Label
        {
            Text = label, ForeColor = Color.Silver, Font = new Font("Segoe UI", 9),
            AutoSize = true, Location = new Point(10, y + 5)
        };
        num.Location = new Point(280, y);
        num.Width = 80;
        num.BackColor = Color.FromArgb(30,41,59);
        num.ForeColor = Color.White;
        num.Font = new Font("Segoe UI", 10);
        return [lbl, num];
    }

    private static NumericUpDown MakeNumeric(int min, int max, int val, int y) => new()
    {
        Minimum = min, Maximum = max, Value = val,
        Location = new Point(280, y), Width = 80,
        BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White,
        Font = new Font("Segoe UI", 10)
    };

    private EhrVitals GetVitalsFromInputs() => new()
    {
        HeartRate       = (float)numHR.Value,
        SpO2            = (float)numSpO2.Value,
        Temperature     = (float)numTemp.Value / 10f,   // ×10 stored
        SBP             = (float)numSBP.Value,
        RespiratoryRate = (float)numRR.Value,
        WBC             = (float)numWBC.Value / 10f,
        CRP             = (float)numCRP.Value / 10f,
        Lactate         = (float)numLactate.Value / 10f,
    };

    // ─── TAB MỚI: SHAP CHART ──────────────────────────────────────────────────
    private TabPage BuildTabShap()
    {
        var tab = new TabPage("📊 SHAP (Giải thích EHR)") { BackColor = Color.FromArgb(15,23,42) };

        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15,23,42) };

        var lblTitle = new Label
        {
            Text      = "SHAP Feature Importance – Đóng góp của từng yếu tố lâm sàng",
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White, Dock = DockStyle.Top, Height = 30,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5,0,0,0)
        };

        // LiveChartsCore CartesianChart (Horizontal Bar Chart)
        _shapChart = new CartesianChart
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15,23,42),
        };

        var lblEmpty = new Label
        {
            Text      = "Chạy chẩn đoán để xem SHAP values...",
            ForeColor = Color.Gray,
            Font      = new Font("Segoe UI", 11, FontStyle.Italic),
            Dock      = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Name      = "lblShapEmpty"
        };

        pnl.Controls.AddRange(new Control[] { lblEmpty, _shapChart, lblTitle });
        tab.Controls.Add(pnl);
        return tab;
    }

    private void UpdateShapChart(ShapApiResponse shap)
    {
        if (_shapChart == null) return;

        // Lấy top 10 features theo |SHAP|
        var topFeatures = shap.feature_contributions
            .OrderByDescending(f => Math.Abs(f.shap_value))
            .Take(10)
            .ToList();

        var values  = topFeatures.Select(f => f.shap_value).ToArray();
        var labels  = topFeatures.Select(f => f.name).ToArray();
        var colors  = topFeatures.Select(f =>
            f.shap_value >= 0
                ? new SKColor(248, 113, 113)   // Đỏ = tăng nguy cơ
                : new SKColor(74,  222, 128)   // Xanh = giảm nguy cơ
        ).ToArray();

        // Tạo series (mỗi feature 1 column)
        var rowSeries = new List<ISeries>();
        for (int i = 0; i < topFeatures.Count; i++)
        {
            int idx = i;
            rowSeries.Add(new RowSeries<double>
            {
                Name   = labels[idx],
                Values = new[] { values[idx] },
                Fill   = new SolidColorPaint(colors[idx]),
                TooltipLabelFormatter = p =>
                    $"{labels[idx]}: {p.Model:F4}",
                MaxBarWidth = 18,
            });
        }

        _shapChart.Series = rowSeries.ToArray();
        _shapChart.YAxes  = new[]
        {
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(SKColors.White),
                TextSize   = 10,
            }
        };
        _shapChart.XAxes = new[]
        {
            new Axis
            {
                Name = "SHAP Value",
                NamePaint   = new SolidColorPaint(SKColors.Silver),
                LabelsPaint = new SolidColorPaint(SKColors.Silver),
                TextSize    = 9,
            }
        };

        // Ẩn placeholder label
        var tabShap = tabRight.TabPages.Cast<TabPage>()
            .FirstOrDefault(t => t.Text.Contains("SHAP"));
        if (tabShap != null)
        {
            var lbl = tabShap.Controls.Find("lblShapEmpty", true).FirstOrDefault();
            if (lbl != null) lbl.Visible = false;
        }
    }

    // ─── TAB MỚI: SO SÁNH SPLIT-VIEW ──────────────────────────────────────────
    private TabPage BuildTabCompare()
    {
        var tab = new TabPage("🔍 So sánh Ảnh gốc / Mask") { BackColor = Color.FromArgb(15,23,42) };

        var split = new SplitContainer
        {
            Dock        = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor   = Color.FromArgb(15,23,42),
            SplitterDistance = 50  // %
        };

        // Bên trái: ảnh gốc
        var lblOrig = new Label
        {
            Text = "📷 Ảnh X-quang gốc", ForeColor = Color.White,
            Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            BackColor = Color.FromArgb(30,41,59)
        };
        pbCompareOrig = new PictureBox
        {
            Dock = DockStyle.Fill, BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        split.Panel1.Controls.AddRange(new Control[] { pbCompareOrig, lblOrig });

        // Bên phải: segmentation overlay
        var lblSeg = new Label
        {
            Text = "🔴 Segmentation Overlay", ForeColor = Color.White,
            Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            BackColor = Color.FromArgb(30,41,59)
        };
        pbCompareSeg = new PictureBox
        {
            Dock = DockStyle.Fill, BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        split.Panel2.Controls.AddRange(new Control[] { pbCompareSeg, lblSeg });

        tab.Controls.Add(split);
        return tab;
    }

    // ─── LOGIC ───────────────────────────────────────────────────────────────
    private async void CheckServerStatus()
    {
        bool alive = await ApiService.IsServerAliveAsync();
        lblStatus.ForeColor = alive ? Color.FromArgb(74,222,128) : Color.FromArgb(248,113,113);
        lblStatus.Text      = alive
            ? "✅ Server AI đang chạy (localhost:8000)"
            : "❌ Không kết nối được server AI – hãy khởi động AI_Server/main.py";
    }

    private void LoadImage(string path)
    {
        _currentImagePath  = path;
        pbOriginal.Image   = Image.FromFile(path);
        btnDiagnose.Enabled = true;
        lblResult.Text      = "—";
        lblProb.Text        = "Xác suất: —";
        pbarProb.Value      = 0;
    }

    private void BtnLoad_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Chọn ảnh X-quang",
            Filter = "Ảnh (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Tất cả|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            LoadImage(dlg.FileName);
    }

    private async void BtnDiagnose_Click(object? sender, EventArgs e)
    {
        if (_currentImagePath is null) return;

        btnDiagnose.Enabled = false;
        btnDiagnose.Text    = "⏳ Đang chẩn đoán...";
        lblResult.Text      = "Đang phân tích...";

        try
        {
            var vitals       = GetVitalsFromInputs();
            var clinicNotes  = txtClinicalNotes?.Text.Trim() ?? string.Empty;
            int topClassIdx  = 6;   // Mặc định: Pneumonia; sẽ cập nhật sau predict

            // ── Gọi đồng thời 3 API chính ───────────────────────────────────
            var tPredict = ApiService.PredictAsync(_currentImagePath, _patient, vitals, clinicNotes);
            var tSegment = ApiService.SegmentAsync(_currentImagePath);
            var tExplain = ApiService.ExplainAsync(_currentImagePath, _patient, topClassIdx, vitals);

            await Task.WhenAll(tPredict, tSegment, tExplain);

            _lastPredict   = tPredict.Result;
            var segResult  = tSegment.Result;
            var explResult = tExplain.Result;

            // ── Cập nhật class index từ kết quả dự đoán ─────────────────────
            topClassIdx = _lastPredict.diagnoses.Count > 0
                ? _lastPredict.diagnoses.IndexOf(
                    _lastPredict.diagnoses.OrderByDescending(d => d.Probability).First())
                : 6;
            // Lấy index trong DISEASES list
            int diseaseIdx = 0;
            for (int i = 0; i < _lastPredict.diagnoses.Count; i++)
            {
                if (_lastPredict.diagnoses[i].Rank == 1) { diseaseIdx = i; break; }
            }

            // ── Hiển thị kết quả chính ──────────────────────────────────────
            string topDisease = _lastPredict.top_disease;
            string topIcd     = _lastPredict.top_icd_code;
            int    probPct    = (int)(_lastPredict.top_probability * 100);

            lblResult.Text  = _lastPredict.normal ? "✅ Phổi bình thường" : $"⚠️ {topDisease}";
            lblResult.ForeColor = _lastPredict.normal
                ? Color.FromArgb(74, 222, 128)
                : Color.FromArgb(248, 113, 113);
            lblProb.Text    = $"Xác suất: {probPct}%";
            pbarProb.Value  = Math.Min(probPct, 100);
            lblIcd.Text     = $"ICD: {topIcd}";
            lblRationale.Text = _lastPredict.textual_rationale;

            // ── Hiển thị ảnh Detection ──────────────────────────────────────
            pbDetection.Image = _lastPredict.detections.Count > 0
                ? DrawBoxes(_currentImagePath, _lastPredict.detections)
                : Image.FromFile(_currentImagePath);

            // ── Hiển thị Segmentation ────────────────────────────────────────
            var segBitmap   = ApiService.Base64ToBitmap(segResult.mask_image_base64);
            pbSegment.Image = segBitmap;

            // ── So sánh Split-view ────────────────────────────────────────────
            pbCompareOrig.Image = Image.FromFile(_currentImagePath);
            pbCompareSeg.Image  = (Image)segBitmap.Clone();

            // ── Hiển thị Heatmap ─────────────────────────────────────────────
            pbHeatmap.Image = ApiService.Base64ToBitmap(explResult.heatmap_image_base64);

            // ── Hiển thị bảng xác suất với ICD codes ─────────────────────────
            dgvResults.Rows.Clear();
            foreach (var d in _lastPredict.diagnoses)
                dgvResults.Rows.Add(d.Rank, d.Disease, d.IcdCode, $"{d.Probability * 100:F1}%");

            // ── Gọi SHAP (riêng sau predict để lấy class index chính xác) ────
            try
            {
                _lastShap = await ApiService.ShapAsync(
                    _currentImagePath, _patient, diseaseIdx, vitals, clinicNotes);
                UpdateShapChart(_lastShap);
            }
            catch { /* SHAP không bắt buộc cho demo */ }

            // ── Lưu vào CSDL ─────────────────────────────────────────────────
            await SaveDiagnosisAsync(segResult, explResult);

            tabRight.SelectedIndex = 3;   // Chuyển sang tab Xác suất
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi trong quá trình chẩn đoán:\n{ex.Message}",
                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnDiagnose.Enabled = true;
            btnDiagnose.Text    = "🔬 Bắt đầu chẩn đoán";
        }
    }

    private async Task SaveDiagnosisAsync(SegmentApiResponse seg, ExplainApiResponse expl)
    {
        if (_lastPredict is null || _currentImagePath is null) return;

        string saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ChestAI", "Results", _patient.PatientID.ToString());
        Directory.CreateDirectory(saveDir);

        string ts = DateTime.Now.ToString("yyyyMMddHHmmss");
        string heatmapPath = Path.Combine(saveDir, $"{ts}_heatmap.png");
        string maskPath    = Path.Combine(saveDir, $"{ts}_mask.png");
        ApiService.Base64ToBitmap(expl.heatmap_image_base64).Save(heatmapPath);
        ApiService.Base64ToBitmap(seg.mask_image_base64).Save(maskPath);

        int imageId = await Task.Run(() =>
            DatabaseService.InsertXRayImage(
                _patient.PatientID, _currentImagePath, "PA", SessionContext.UserId));

        var diag = new Diagnosis
        {
            PatientID         = _patient.PatientID,
            ImageID           = imageId,
            TopDisease        = _lastPredict.top_disease,
            TopIcdCode        = _lastPredict.top_icd_code,
            TopProbability    = _lastPredict.top_probability,
            IsNormal          = _lastPredict.normal,
            LesionAreaPercent = seg.lesion_area_percent,
            HeatmapPath       = heatmapPath,
            SegMaskPath       = maskPath,
            TextualRationale  = _lastPredict.textual_rationale,
            ModalitiesUsed    = _lastPredict.modalities_used,
            Details           = _lastPredict.diagnoses
                .Select(d => new DiseaseResult
                {
                    Disease               = d.Disease,
                    IcdCode               = d.IcdCode,
                    Probability           = d.Probability,
                    CalibratedProbability = d.CalibratedProbability,
                    Rank                  = d.Rank
                }).ToList()
        };

        _lastDiagnosisId = await Task.Run(() =>
            DatabaseService.InsertDiagnosis(diag, SessionContext.UserId));

        LoadHistory();
    }

    private void BtnConfirm_Click(object? sender, EventArgs e)
    {
        if (_lastDiagnosisId == 0)
        {
            MessageBox.Show("Chưa có kết quả chẩn đoán để xác nhận.", "Thông báo",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DatabaseService.ConfirmDiagnosis(_lastDiagnosisId, txtConclusion.Text.Trim());
        MessageBox.Show("Đã lưu kết luận của bác sĩ.", "Thành công",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadHistory();
    }

    private void LoadHistory()
    {
        try
        {
            var list = DatabaseService.GetPatientDiagnoses(_patient.PatientID);
            dgvHistory.Rows.Clear();
            foreach (var d in list)
                dgvHistory.Rows.Add(
                    d.DiagnosedAt.ToString("dd/MM/yyyy HH:mm"),
                    d.IsNormal ? "Bình thường" : d.TopDisease,
                    d.IsNormal ? "" : d.TopIcdCode,
                    $"{d.TopProbability * 100:F1}%",
                    d.DoctorName,
                    d.DoctorConfirmed ? "✅" : "⏳"
                );
        }
        catch { /* CSDL chưa sẵn sàng – bỏ qua */ }
    }

    // ─── VẼ BOUNDING BOX LÊN ẢNH ─────────────────────────────────────────────
    private static Bitmap DrawBoxes(string imgPath, List<DetectionBox> boxes)
    {
        var bmp = new Bitmap(imgPath);
        using var g = Graphics.FromImage(bmp);
        var pen   = new Pen(Color.FromArgb(255, 50, 50), 3);
        var brush = new SolidBrush(Color.FromArgb(180, 255, 50, 50));
        var font  = new Font("Arial", 10, FontStyle.Bold);

        float sx = 1f, sy = 1f;   // Nếu YOLO trả tọa độ theo ảnh 640px thì cần scale

        foreach (var box in boxes)
        {
            float x = (float)box.x1 * sx, y  = (float)box.y1 * sy;
            float w = (float)(box.x2 - box.x1) * sx;
            float h = (float)(box.y2 - box.y1) * sy;
            g.DrawRectangle(pen, x, y, w, h);
            string label = $"{box.label} {box.confidence * 100:F0}%";
            g.FillRectangle(brush, x, y - 20, g.MeasureString(label, font).Width + 4, 20);
            g.DrawString(label, font, Brushes.White, x + 2, y - 19);
        }
        return bmp;
    }

    private static Button MakeBtn(string text, Color bg, Point loc, int w) => new()
    {
        Text      = text, Location = loc, Size = new Size(w, 34),
        FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
        Font      = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
    };
}
