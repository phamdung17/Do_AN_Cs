using ChestAI.Forms;

namespace ChestAI.Forms;

/// <summary>
/// Form chính – Thanh menu bên trái + vùng nội dung bên phải.
/// Điều hướng đến: Bảng điều khiển | Bệnh nhân | Chẩn đoán
/// </summary>
public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
        // Set Unicode text ở đây để tránh lỗi encoding trong Designer.cs
        lblLogo.Text      = "🏥 ChestAI";
        lblWelcome.Text   = $"Xin chào,\n{SessionContext.FullName}";
        btnDashboard.Text = "📊  Bảng điều khiển";
        btnPatients.Text  = "👥  Quản lý bệnh nhân";
        btnDiagnosis.Text = "🔬  Chẩn đoán mới";
        btnLogout.Text    = "🚪  Đăng xuất";
        Text              = "ChestAI – Hệ thống chẩn đoán X-quang";
        ShowDashboard();
    }

    // ─── EVENT HANDLERS ───────────────────────────────────────────────────────
    private void BtnDashboard_Click(object? sender, EventArgs e) => ShowDashboard();
    private void BtnPatients_Click(object? sender, EventArgs e)  => ShowPatients();
    private void BtnDiagnosis_Click(object? sender, EventArgs e) => ShowDiagnosis();
    private void BtnLogout_Click(object? sender, EventArgs e)    => Logout();

    // ─── ĐIỀU HƯỚNG ──────────────────────────────────────────────────────────
    private void ShowDashboard()
    {
        pnlContent.Controls.Clear();
        pnlContent.Controls.Add(BuildDashboardPanel());
    }

    private void ShowPatients()
    {
        pnlContent.Controls.Clear();
        pnlContent.Controls.Add(new PatientPanel { Dock = DockStyle.Fill });
    }

    private void ShowDiagnosis()
    {
        // Mở form wizard đăng ký bệnh án mới (NewDiagnosisForm)
        // Form này tự xử lý: chọn bệnh nhân → vitals → ảnh → gọi AI → hiển thị kết quả
        var form = new NewDiagnosisForm();
        form.Show(this);
    }

    private void Logout()
    {
        if (MessageBox.Show("Bạn có chắc muốn đăng xuất?", "Xác nhận",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            new LoginForm().Show();
            Close();
        }
    }

    // ─── DASHBOARD ───────────────────────────────────────────────────────────
    private Panel BuildDashboardPanel()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(
                Environment.GetEnvironmentVariable("CHESTAI_CONNSTR")
                ?? @"Server=localhost\SQLEXPRESS;Database=ChestAI;Integrated Security=True;TrustServerCertificate=True;");
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("sp_GetDashboardStats", conn)
            { CommandType = System.Data.CommandType.StoredProcedure };
            using var rdr = cmd.ExecuteReader();
            if (rdr.Read())
            {
                int totalPat  = rdr.GetInt32(0);
                int totalDiag = rdr.GetInt32(1);
                int todayDiag = rdr.GetInt32(2);
                int abnormal  = rdr.GetInt32(3);
                string topDis = rdr.IsDBNull(4) ? "N/A" : rdr.GetString(4);

                var cards = new[]
                {
                    ("👥 Tổng bệnh nhân",     totalPat.ToString(),  Color.FromArgb(56,189,248)),
                    ("🧪 Tổng chẩn đoán",     totalDiag.ToString(), Color.FromArgb(74,222,128)),
                    ("📅 Hôm nay",            todayDiag.ToString(), Color.FromArgb(251,191,36)),
                    ("⚠️ Ca bất thường",      abnormal.ToString(),  Color.FromArgb(248,113,113)),
                    ("🏆 Bệnh phổ biến nhất", topDis,               Color.FromArgb(167,139,250)),
                };

                int x = 20;
                foreach (var (title, value, color) in cards)
                {
                    pnl.Controls.Add(BuildStatCard(title, value, color, x, 30));
                    x += 230;
                }
            }
        }
        catch (Exception ex)
        {
            pnl.Controls.Add(new Label
            {
                Text      = $"Không thể tải thống kê: {ex.Message}",
                ForeColor = Color.Red, AutoSize = true, Location = new Point(20, 20)
            });
        }
        return pnl;
    }

    private static Panel BuildStatCard(string title, string value, Color accent, int x, int y)
    {
        var card = new Panel
        {
            Size      = new Size(210, 110),
            Location  = new Point(x, y),
            BackColor = Color.FromArgb(30, 41, 59)
        };
        var bar = new Panel
        { Size = new Size(210, 5), Location = new Point(0, 0), BackColor = accent };
        var lblTitle = new Label
        {
            Text = title, ForeColor = Color.Silver,
            Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(12, 18)
        };
        var lblValue = new Label
        {
            Text = value, ForeColor = accent,
            Font = new Font("Segoe UI", 24, FontStyle.Bold),
            AutoSize = true, Location = new Point(12, 45)
        };
        card.Controls.AddRange(new Control[] { bar, lblTitle, lblValue });
        return card;
    }

}
