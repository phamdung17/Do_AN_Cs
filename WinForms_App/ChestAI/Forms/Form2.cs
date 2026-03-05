using ChestAI.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;

namespace ChestAI.Forms;

/// <summary>
/// Form hiển thị kết quả chẩn đoán AI đầy đủ.
/// Bố cục: Header thông tin bệnh nhân → Tab kết quả (Tổng quan / Ảnh / Bệnh lý / SHAP / Kết luận).
/// Được mở bởi NewDiagnosisForm hoặc DiagnosisPanel sau khi chạy AI xong.
/// </summary>
public class DiagnosisResultForm : Form
{
    // ── Dữ liệu đầu vào ──────────────────────────────────────────────────────
    private readonly Patient            _patient;
    private readonly string             _imagePath;
    private readonly EhrVitals?         _vitals;
    private readonly PredictApiResponse _predict;
    private readonly SegmentApiResponse _segment;
    private readonly ExplainApiResponse _explain;
    private readonly ShapApiResponse?   _shap;

    // ── Controls ─────────────────────────────────────────────────────────────
    private TabControl       tabMain      = null!;
    private CartesianChart?  _shapChart;
    private DataGridView     dgvDiseases  = null!;
    private TextBox          txtConclusion = null!;
    private int              _savedDiagId  = 0;

    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color BgDark    = Color.FromArgb(15,  23,  42);
    private static readonly Color BgCard    = Color.FromArgb(30,  41,  59);
    private static readonly Color Accent    = Color.FromArgb(56,  189, 248);
    private static readonly Color Green     = Color.FromArgb(74,  222, 128);
    private static readonly Color Red       = Color.FromArgb(248, 113, 113);
    private static readonly Color Yellow    = Color.FromArgb(251, 191, 36);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextGray  = Color.FromArgb(148, 163, 184);

    public DiagnosisResultForm(
        Patient patient, string imagePath, EhrVitals? vitals,
        PredictApiResponse predict, SegmentApiResponse segment,
        ExplainApiResponse explain, ShapApiResponse? shap)
    {
        _patient   = patient;
        _imagePath = imagePath;
        _vitals    = vitals;
        _predict   = predict;
        _segment   = segment;
        _explain   = explain;
        _shap      = shap;

        Build();
        SaveToDatabase();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  XÂY DỰNG FORM
    // ═════════════════════════════════════════════════════════════════════════
    private void Build()
    {
        Text = $"Kết Quả Chẩn Đoán AI – {_patient.FullName}  |  ChestAI";
        Size = new Size(1200, 800);
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        WindowState = FormWindowState.Maximized;

        // ── Header bệnh nhân + kết quả chính ─────────────────────────────────
        var pnlHeader = BuildHeader();

        // ── TabControl nội dung ───────────────────────────────────────────────
        tabMain = new TabControl
        {
            Dock   = DockStyle.Fill,
            Font   = new Font("Segoe UI", 9),
            Padding = new Point(12, 5)
        };

        tabMain.TabPages.Add(BuildTabOverview());
        tabMain.TabPages.Add(BuildTabImages());
        tabMain.TabPages.Add(BuildTabDiseases());
        tabMain.TabPages.Add(BuildTabShap());
        tabMain.TabPages.Add(BuildTabConclusion());

        // ── Bottom action bar ─────────────────────────────────────────────────
        var pnlActions = BuildActionBar();

        Controls.AddRange(new Control[] { pnlHeader, tabMain, pnlActions });
    }

    // ─── HEADEr ──────────────────────────────────────────────────────────────
    private Panel BuildHeader()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Top, Height = 100,
            BackColor = Color.FromArgb(9, 17, 34)
        };

        // Accent bar
        var bar = new Panel { Location = new Point(0,0), Height = 4, Dock = DockStyle.Top,
            BackColor = _predict.normal ? Green : Red };

        // Tên bệnh nhân
        var lblName = new Label
        {
            Text = $"🧑  {_patient.FullName}",
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = TextWhite, AutoSize = true, Location = new Point(20, 12)
        };

        // Thông tin cơ bản
        var lblInfo = new Label
        {
            Text = $"{(_patient.Gender == 'M' ? "Nam" : "Nữ")}, {_patient.Age} tuổi  " +
                   $"|  ID: {_patient.PatientID}  " +
                   $"|  {DateTime.Now:dd/MM/yyyy HH:mm}",
            Font = new Font("Segoe UI", 9), ForeColor = TextGray,
            AutoSize = true, Location = new Point(22, 42)
        };

        // Kết quả chính (banner phải)
        bool normal = _predict.normal;
        string topText = normal
            ? "✅  Phổi bình thường"
            : $"⚠️  {_predict.top_disease}";
        int probPct = (int)(_predict.top_probability * 100);

        var pnlResult = new Panel
        {
            Size = new Size(400, 80), Location = new Point(760, 10),
            BackColor = normal ? Color.FromArgb(6, 78, 59) : Color.FromArgb(69, 10, 10)
        };

        var lblDiseaseMain = new Label
        {
            Text = topText,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = normal ? Green : Red,
            AutoSize = true, Location = new Point(12, 10)
        };

        var lblIcd = new Label
        {
            Text = $"ICD-10: {_predict.top_icd_code}   |   Xác suất: {probPct}%",
            Font = new Font("Segoe UI", 9),
            ForeColor = normal ? Color.FromArgb(167, 243, 208) : Color.FromArgb(254, 202, 202),
            AutoSize = true, Location = new Point(12, 46)
        };

        pnlResult.Controls.AddRange(new Control[] { lblDiseaseMain, lblIcd });

        // Modalities badge
        string modBadge = string.Join(" + ", _predict.modalities_used.Select(m => m switch
        {
            "image" => "🖼 Hình ảnh",
            "text"  => "📝 BERT",
            "ehr"   => "🩺 EHR",
            _       => m
        }));
        var lblMod = new Label
        {
            Text = $"Phương thức: {modBadge}",
            Font = new Font("Segoe UI", 8, FontStyle.Italic),
            ForeColor = Accent, AutoSize = true, Location = new Point(22, 68)
        };

        pnl.Controls.AddRange(new Control[] { bar, lblName, lblInfo, pnlResult, lblMod });
        return pnl;
    }

    // ─── TAB 0: TỔNG QUAN ─────────────────────────────────────────────────────
    private TabPage BuildTabOverview()
    {
        var tab = new TabPage("📋 Tổng Quan") { BackColor = BgDark };
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BgDark };

        // Top 5 stat cards
        int x = 15;
        var top5 = _predict.diagnoses
            .OrderByDescending(d => d.Probability).Take(5).ToList();

        for (int i = 0; i < Math.Min(5, top5.Count); i++)
        {
            var d = top5[i];
            int pct = (int)(d.Probability * 100);
            bool positive = pct >= 35;
            var card = BuildResultCard(
                d.Disease, d.IcdCode, pct, positive,
                i == 0 && !_predict.normal, x, 15);
            pnl.Controls.Add(card);
            x += 220;
        }

        // Lung area info
        var pnlArea = BuildCard(15, 165, 350, 90, "🫁 Thông Số Hình Ảnh");
        AddInfoRow(pnlArea, "Diện tích tổn thương",
            $"{_segment.lesion_area_percent:F1}%", 45, _segment.has_lesion ? Red : Green);
        AddInfoRow(pnlArea, "Phát hiện tổn thương",
            _segment.has_lesion ? "Có" : "Không", 68, _segment.has_lesion ? Red : Green);
        pnl.Controls.Add(pnlArea);

        // EHR vitals summary
        if (_vitals != null)
        {
            var pnlVitals = BuildCard(380, 165, 580, 90, "🩺 Thông Số Lâm Sàng đã nhập");
            string vitText =
                $"HR: {_vitals.HeartRate} bpm  |  SpO₂: {_vitals.SpO2}%  |  " +
                $"T°: {_vitals.Temperature:F1}  |  SBP: {_vitals.SBP} mmHg  |  " +
                $"RR: {_vitals.RespiratoryRate}/phút  |  " +
                $"WBC: {_vitals.WBC:F1}  |  CRP: {_vitals.CRP:F1}  |  Lactate: {_vitals.Lactate:F1}";
            pnlVitals.Controls.Add(new Label
            {
                Text = vitText, ForeColor = TextWhite, Font = new Font("Segoe UI", 8.5f),
                Location = new Point(12, 45), Size = new Size(555, 35), AutoEllipsis = true
            });
            pnl.Controls.Add(pnlVitals);
        }

        // AI textual rationale
        var pnlRationale = BuildCard(15, 268, 1130, 160, "🤖 Giải Thích Của AI");
        var lblRationale = new Label
        {
            Text = string.IsNullOrWhiteSpace(_predict.textual_rationale)
                ? "Không có giải thích văn bản."
                : _predict.textual_rationale,
            ForeColor = TextWhite, Font = new Font("Segoe UI", 10),
            Location = new Point(12, 42), Size = new Size(1100, 105),
            AutoEllipsis = false
        };
        pnlRationale.Controls.Add(lblRationale);
        pnl.Controls.Add(pnlRationale);

        tab.Controls.Add(pnl);
        return tab;
    }

    // ─── TAB 1: ẢNH ───────────────────────────────────────────────────────────
    private TabPage BuildTabImages()
    {
        var tab = new TabPage("🖼️ Ảnh X-Quang") { BackColor = BgDark };

        // 3-column image layout
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = BgDark };

        int imgW = 370;
        pnl.Controls.Add(BuildImagePanel(
            "📷 Ảnh X-Quang Gốc",
            Image.FromFile(_imagePath), 10, imgW));

        if (!string.IsNullOrEmpty(_segment.mask_image_base64))
            pnl.Controls.Add(BuildImagePanel(
                $"🔴 Phân Đoạn Phổi  ({_segment.lesion_area_percent:F1}% tổn thương)",
                ApiService.Base64ToBitmap(_segment.mask_image_base64), 395, imgW));

        if (!string.IsNullOrEmpty(_explain.heatmap_image_base64))
            pnl.Controls.Add(BuildImagePanel(
                $"🌡️ Grad-CAM — {_explain.disease_explained}",
                ApiService.Base64ToBitmap(_explain.heatmap_image_base64), 780, imgW));

        // Resize handler for equal-width panels
        pnl.SizeChanged += (s, e) =>
        {
            int w = (pnl.Width - 40) / 3;
            int col = 10;
            foreach (Control c in pnl.Controls)
            {
                c.Location = new Point(col, 10);
                c.Size = new Size(w, pnl.Height - 60);
                col += w + 10;
            }
        };

        tab.Controls.Add(pnl);
        return tab;
    }

    // ─── TAB 2: BẢNG 14 BỆNH ─────────────────────────────────────────────────
    private TabPage BuildTabDiseases()
    {
        var tab = new TabPage("📊 Tất Cả Bệnh Lý") { BackColor = BgDark };

        dgvDiseases = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true, AllowUserToAddRows = false,
            RowHeadersVisible = false, ColumnHeadersHeight = 36,
            BackgroundColor = BgCard, GridColor = Color.FromArgb(51, 65, 85),
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgDark, ForeColor = Accent,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgCard, ForeColor = TextWhite,
                SelectionBackColor = Color.FromArgb(37, 99, 235),
                SelectionForeColor = TextWhite,
                Font = new Font("Segoe UI", 10)
            },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowTemplate        = { Height = 36 }
        };

        dgvDiseases.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Rank",         Name = "rank",    Width = 55 },
            new DataGridViewTextBoxColumn { HeaderText = "Bệnh lý",      Name = "disease", FillWeight = 30 },
            new DataGridViewTextBoxColumn { HeaderText = "ICD-10",       Name = "icd",     Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Xác suất (%)", Name = "prob",    Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "Hiệu chỉnh (%)", Name = "calib", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "Đánh giá",     Name = "eval",    Width = 110 }
        );

        foreach (var d in _predict.diagnoses.OrderBy(d => d.Rank))
        {
            int pct  = (int)(d.Probability * 100);
            int cpct = (int)(d.CalibratedProbability * 100);
            string eval = pct >= 60 ? "🔴 Cao"
                        : pct >= 35 ? "🟡 Trung bình"
                        :             "🟢 Thấp";
            var row = dgvDiseases.Rows[dgvDiseases.Rows.Add(
                d.Rank, d.Disease, d.IcdCode,
                $"{pct,3}%", $"{cpct,3}%", eval)];

            // Colour-code probability cell
            row.Cells["prob"].Style.ForeColor = pct >= 60 ? Red
                                              : pct >= 35 ? Yellow : Green;
            row.Cells["prob"].Style.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        }

        tab.Controls.Add(dgvDiseases);
        return tab;
    }

    // ─── TAB 3: SHAP ──────────────────────────────────────────────────────────
    private TabPage BuildTabShap()
    {
        var tab = new TabPage("📈 SHAP – Giải Thích EHR") { BackColor = BgDark };

        if (_shap is null || _shap.feature_contributions.Count == 0)
        {
            tab.Controls.Add(new Label
            {
                Text = "Không có dữ liệu SHAP.", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = TextGray, Font = new Font("Segoe UI", 12, FontStyle.Italic)
            });
            return tab;
        }

        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = BgDark };

        // Header info
        var lblDisease = new Label
        {
            Text = $"Bệnh giải thích: {_shap.disease}  (ICD: {_shap.icd_code})  " +
                   $"—  Xác suất: {_shap.probability * 100:F1}%",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = TextWhite, Dock = DockStyle.Top, Height = 32,
            Padding = new Padding(12, 6, 0, 0)
        };

        // Positive / Negative feature lists
        var pnlLists = new Panel { Dock = DockStyle.Bottom, Height = 80, BackColor = BgCard };
        var lblPos = new Label
        {
            Text = "🔴 Tăng nguy cơ: " + string.Join(", ", _shap.top_positive_features),
            ForeColor = Red, Font = new Font("Segoe UI", 9),
            Location = new Point(12, 8), Width = 550, AutoEllipsis = true
        };
        var lblNeg = new Label
        {
            Text = "🟢 Giảm nguy cơ: " + string.Join(", ", _shap.top_negative_features),
            ForeColor = Green, Font = new Font("Segoe UI", 9),
            Location = new Point(12, 34), Width = 550, AutoEllipsis = true
        };
        var lblTextExpl = new Label
        {
            Text = _shap.textual_explanation,
            ForeColor = TextGray, Font = new Font("Segoe UI", 8, FontStyle.Italic),
            Location = new Point(12, 58), Width = 1100, AutoEllipsis = true
        };
        pnlLists.Controls.AddRange(new Control[] { lblPos, lblNeg, lblTextExpl });

        // Horizontal bar chart
        _shapChart = new CartesianChart { Dock = DockStyle.Fill, BackColor = BgDark };
        BuildShapChart();

        pnl.Controls.AddRange(new Control[] { pnlLists, _shapChart, lblDisease });
        tab.Controls.Add(pnl);
        return tab;
    }

    private void BuildShapChart()
    {
        if (_shapChart is null || _shap is null) return;

        var features = _shap.feature_contributions
            .OrderByDescending(f => Math.Abs(f.shap_value))
            .Take(14)
            .ToList();

        var series = new List<ISeries>();
        for (int i = 0; i < features.Count; i++)
        {
            int idx = i;
            var f = features[idx];
            bool positive = f.shap_value >= 0;
            series.Add(new RowSeries<double>
            {
                Name          = f.name,
                Values        = new[] { f.shap_value },
                Fill          = new SolidColorPaint(
                    positive ? new SKColor(248, 113, 113) : new SKColor(74, 222, 128)),
                MaxBarWidth   = 20,
                XToolTipLabelFormatter = p =>
                    $"{f.name}: {p.Model:F4}  [{(f.value ?? "")}]"
            });
        }

        _shapChart.Series = series.ToArray();
        _shapChart.YAxes = new[]
        {
            new Axis
            {
                Labels      = features.Select(f => f.name).ToArray(),
                LabelsPaint = new SolidColorPaint(SKColors.White),
                TextSize    = 11
            }
        };
        _shapChart.XAxes = new[]
        {
            new Axis
            {
                Name      = "SHAP Value  (🔴+risk  🟢−risk)",
                NamePaint   = new SolidColorPaint(SKColors.Silver),
                LabelsPaint = new SolidColorPaint(SKColors.Silver),
                TextSize    = 10
            }
        };
    }

    // ─── TAB 4: KẾT LUẬN BÁC SĨ ──────────────────────────────────────────────
    private TabPage BuildTabConclusion()
    {
        var tab = new TabPage("✍️ Kết Luận Bác Sĩ") { BackColor = BgDark };
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BgDark };

        // AI summary box (read-only)
        var pnlAi = BuildCard(15, 15, 1130, 130, "🤖 Tóm Tắt Kết Quả AI");
        var lblAi = new Label
        {
            Text = _predict.normal
                ? "AI KHÔNG phát hiện tổn thương đáng kể. " +
                  $"Xác suất phổi bình thường: {(1 - _predict.top_probability) * 100:F1}%. " +
                  "Tuy nhiên kết quả cần được bác sĩ xác nhận."
                : $"AI phát hiện khả năng cao: {_predict.top_disease} " +
                  $"(ICD-10: {_predict.top_icd_code}, xác suất: {_predict.top_probability * 100:F1}%). " +
                  _predict.textual_rationale,
            ForeColor = TextWhite, Font = new Font("Segoe UI", 10),
            Location = new Point(12, 40), Size = new Size(1100, 80), AutoEllipsis = false
        };
        pnlAi.Controls.Add(lblAi);

        // Doctor conclusion input
        var pnlConc = BuildCard(15, 160, 1130, 220, "👨‍⚕️ Kết Luận / Nhận Xét Bác Sĩ");
        txtConclusion = new TextBox
        {
            Location = new Point(12, 45), Size = new Size(1100, 130),
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            BackColor = BgDark, ForeColor = TextWhite,
            Font = new Font("Segoe UI", 10),
            PlaceholderText =
                "Nhập nhận xét, kết luận lâm sàng, đề xuất điều trị hoặc theo dõi thêm..."
        };
        pnlConc.Controls.Add(txtConclusion);

        // Save button
        var btnSaveConclusion = MakeBtn("✅ Lưu Kết Luận", Green, new Point(15, 395), 200, 40);
        btnSaveConclusion.Click += BtnSaveConclusion_Click;

        // Export row
        var btnExportPdf = MakeBtn("📄 Xuất PDF", Color.FromArgb(59, 130, 246),
            new Point(225, 395), 160, 40);
        var btnPrint     = MakeBtn("🖨️ In kết quả", Color.FromArgb(100, 116, 139),
            new Point(395, 395), 160, 40);
        var btnNewDiag   = MakeBtn("🔬 Chẩn đoán mới", Color.FromArgb(124, 58, 237),
            new Point(565, 395), 170, 40);

        btnExportPdf.Click += (s, e) => ExportPdf();
        btnPrint.Click     += (s, e) => PrintResult();
        btnNewDiag.Click   += (s, e) => { new NewDiagnosisForm().Show(); Close(); };

        pnl.Controls.AddRange(new Control[]
        { pnlAi, pnlConc, btnSaveConclusion, btnExportPdf, btnPrint, btnNewDiag });
        tab.Controls.Add(pnl);
        return tab;
    }

    // ─── ACTION BAR ───────────────────────────────────────────────────────────
    private Panel BuildActionBar()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Bottom, Height = 52,
            BackColor = Color.FromArgb(9, 17, 34)
        };

        var btnClose = MakeBtn("✖  Đóng", Color.FromArgb(71, 85, 105),
            new Point(20, 10), 120, 32);
        btnClose.Click += (s, e) => Close();

        var lblStatus = new Label
        {
            Text = "💾 Kết quả đã được lưu vào CSDL tự động",
            ForeColor = Green, Font = new Font("Segoe UI", 9, FontStyle.Italic),
            AutoSize = true, Location = new Point(155, 17)
        };

        pnl.Controls.AddRange(new Control[] { btnClose, lblStatus });
        return pnl;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LOGIC
    // ═════════════════════════════════════════════════════════════════════════
    private void SaveToDatabase()
    {
        try
        {
            // Lưu đường dẫn ảnh heatmap / mask
            string saveDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ChestAI", "Results", _patient.PatientID.ToString());
            Directory.CreateDirectory(saveDir);
            string ts = DateTime.Now.ToString("yyyyMMddHHmmss");

            string? heatmapPath = null, maskPath = null;
            if (!string.IsNullOrEmpty(_explain.heatmap_image_base64))
            {
                heatmapPath = Path.Combine(saveDir, $"{ts}_heatmap.png");
                ApiService.Base64ToBitmap(_explain.heatmap_image_base64).Save(heatmapPath);
            }
            if (!string.IsNullOrEmpty(_segment.mask_image_base64))
            {
                maskPath = Path.Combine(saveDir, $"{ts}_mask.png");
                ApiService.Base64ToBitmap(_segment.mask_image_base64).Save(maskPath);
            }

            int imageId = DatabaseService.InsertXRayImage(
                _patient.PatientID, _imagePath, "PA", SessionContext.UserId);

            var diag = new Diagnosis
            {
                PatientID        = _patient.PatientID,
                ImageID          = imageId,
                TopDisease       = _predict.top_disease,
                TopIcdCode       = _predict.top_icd_code,
                TopProbability   = _predict.top_probability,
                IsNormal         = _predict.normal,
                LesionAreaPercent = _segment.lesion_area_percent,
                HeatmapPath      = heatmapPath,
                SegMaskPath      = maskPath,
                TextualRationale = _predict.textual_rationale,
                ModalitiesUsed   = _predict.modalities_used,
                Details          = _predict.diagnoses.Select(d => new DiseaseResult
                {
                    Disease               = d.Disease,
                    IcdCode               = d.IcdCode,
                    Probability           = d.Probability,
                    CalibratedProbability = d.CalibratedProbability,
                    Rank                  = d.Rank
                }).ToList()
            };

            _savedDiagId = DatabaseService.InsertDiagnosis(diag, SessionContext.UserId);
        }
        catch (Exception ex)
        {
            // Không chặn UI – log lỗi
            System.Diagnostics.Debug.WriteLine($"SaveToDatabase error: {ex.Message}");
        }
    }

    private void BtnSaveConclusion_Click(object? sender, EventArgs e)
    {
        if (_savedDiagId == 0)
        {
            MessageBox.Show("Chưa có ID chẩn đoán để lưu kết luận.", "Thông báo",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            DatabaseService.ConfirmDiagnosis(_savedDiagId, txtConclusion.Text.Trim());
            MessageBox.Show("✅ Đã lưu kết luận bác sĩ.", "Thành công",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportPdf()
    {
        MessageBox.Show(
            "Tính năng xuất PDF sẽ sử dụng iTextSharp.\n" +
            "Tích hợp NuGet: itext7 (v8.x) vào project để kích hoạt.",
            "Xuất PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PrintResult()
    {
        using var pd = new System.Drawing.Printing.PrintDocument();
        pd.PrintPage += (s, e) =>
        {
            if (e.Graphics is null) return;
            float y = 40;
            using var titleFont = new Font("Arial", 16, FontStyle.Bold);
            using var h2Font    = new Font("Arial", 12, FontStyle.Bold);
            using var bodyFont  = new Font("Arial", 10);

            e.Graphics.DrawString("PHIẾU KẾT QUẢ X-QUANG NGỰC – ChestAI", titleFont,
                Brushes.Black, 40, y); y += 35;
            e.Graphics.DrawString(
                $"Bệnh nhân: {_patient.FullName}  |  Tuổi: {_patient.Age}  |  " +
                $"Giới: {(_patient.Gender == 'M' ? "Nam" : "Nữ")}  |  " +
                $"Ngày: {DateTime.Now:dd/MM/yyyy}", bodyFont, Brushes.Black, 40, y); y += 25;
            e.Graphics.DrawLine(Pens.Black, 40, y, 760, y); y += 15;

            e.Graphics.DrawString("KẾT QUẢ CHẨN ĐOÁN AI:", h2Font, Brushes.Black, 40, y); y += 22;
            e.Graphics.DrawString(
                _predict.normal
                    ? "Không phát hiện bất thường"
                    : $"{_predict.top_disease} (ICD-10: {_predict.top_icd_code})  —  {_predict.top_probability * 100:F1}%",
                bodyFont, Brushes.Black, 55, y); y += 20;

            foreach (var d in _predict.diagnoses.OrderBy(d => d.Rank).Take(5))
            {
                e.Graphics.DrawString(
                    $"  [{d.Rank}] {d.Disease} ({d.IcdCode}): {d.Probability * 100:F1}%  [cal: {d.CalibratedProbability * 100:F1}%]",
                    bodyFont, Brushes.Black, 55, y); y += 18;
            }
            y += 10;

            if (!string.IsNullOrWhiteSpace(txtConclusion?.Text))
            {
                e.Graphics.DrawString("KẾT LUẬN BÁC SĨ:", h2Font, Brushes.Black, 40, y); y += 22;
                e.Graphics.DrawString(txtConclusion.Text, bodyFont, Brushes.Black, 55, y);
            }
        };

        using var preview = new System.Windows.Forms.PrintPreviewDialog
        { Document = pd, WindowState = FormWindowState.Maximized };
        preview.ShowDialog(this);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════════
    private static Panel BuildResultCard(
        string disease, string icd, int prob, bool positive, bool isTop,
        int x, int y)
    {
        var card = new Panel
        {
            Size = new Size(205, 130), Location = new Point(x, y),
            BackColor = isTop
                ? Color.FromArgb(69, 10, 10)
                : Color.FromArgb(30, 41, 59)
        };
        var bar = new Panel
        {
            Size = new Size(205, 4), Location = new Point(0, 0),
            BackColor = prob >= 60 ? Color.FromArgb(248,113,113)
                      : prob >= 35 ? Color.FromArgb(251,191,36)
                      :              Color.FromArgb(74,222,128)
        };
        var lblIcd = new Label
        {
            Text = icd, ForeColor = Color.FromArgb(148,163,184),
            Font = new Font("Segoe UI", 8), AutoSize = true, Location = new Point(10, 10)
        };
        var lblName = new Label
        {
            Text = disease, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(10, 28), Width = 185, AutoEllipsis = true
        };
        var lblProb = new Label
        {
            Text = $"{prob}%",
            ForeColor = prob >= 60 ? Color.FromArgb(248,113,113)
                      : prob >= 35 ? Color.FromArgb(251,191,36)
                      :              Color.FromArgb(74,222,128),
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            AutoSize = true, Location = new Point(10, 52)
        };
        var pb = new ProgressBar
        {
            Location = new Point(10, 100), Width = 185, Height = 12,
            Value = Math.Min(prob, 100), Style = ProgressBarStyle.Continuous
        };
        card.Controls.AddRange(new Control[] { bar, lblIcd, lblName, lblProb, pb });
        return card;
    }

    private static Panel BuildImagePanel(string title, Image img, int x, int w)
    {
        var card = new Panel
        {
            Location = new Point(x, 10), Size = new Size(w, 500),
            BackColor = BgCard
        };
        var bar = new Panel { Size = new Size(w, 4), BackColor = Accent };
        var lbl = new Label
        {
            Text = title, ForeColor = TextWhite,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Dock = DockStyle.None, Location = new Point(5, 8), AutoSize = true
        };
        var pb = new PictureBox
        {
            Location = new Point(5, 32), Size = new Size(w - 10, 455),
            Image = img, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black
        };
        card.Controls.AddRange(new Control[] { bar, lbl, pb });
        return card;
    }

    private static Panel BuildCard(int x, int y, int w, int h, string title)
    {
        var card = new Panel
        { Location = new Point(x, y), Size = new Size(w, h), BackColor = BgCard };
        card.Controls.Add(new Panel
        { Size = new Size(w, 4), BackColor = Accent });
        card.Controls.Add(new Label
        {
            Text = title, ForeColor = TextWhite,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            AutoSize = true, Location = new Point(12, 10)
        });
        return card;
    }

    private static void AddInfoRow(Panel parent, string label, string value, int y, Color valueColor)
    {
        parent.Controls.Add(new Label
        {
            Text = label, ForeColor = TextGray, Font = new Font("Segoe UI", 9),
            AutoSize = true, Location = new Point(12, y)
        });
        parent.Controls.Add(new Label
        {
            Text = value, ForeColor = valueColor,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            AutoSize = true, Location = new Point(200, y)
        });
    }

    private static Button MakeBtn(string text, Color bg, Point loc, int w, int h = 36) => new()
    {
        Text = text, Location = loc, Size = new Size(w, h),
        FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
        Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
    };
}
