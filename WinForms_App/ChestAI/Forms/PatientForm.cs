using ChestAI.Models;

namespace ChestAI.Forms;

/// <summary>
/// Panel quản lý bệnh nhân: Tìm kiếm, thêm mới, chỉnh sửa hồ sơ.
/// Nhúng vào vùng nội dung của MainForm.
/// </summary>
public class PatientPanel : Panel
{
    private DataGridView dgv       = null!;
    private TextBox      txtSearch = null!;
    private Button       btnAdd    = null!;
    private Button       btnEdit   = null!;
    private Button       btnDiag   = null!;

    private List<Patient> _patients = [];

    public PatientPanel()
    {
        InitializeComponent();
        LoadPatients();
    }

    private void InitializeComponent()
    {
        BackColor = Color.FromArgb(15, 23, 42);

        // ── Tiêu đề ───────────────────────────────────────────────────────────
        var lblTitle = new Label
        {
            Text      = "Quản lý bệnh nhân",
            Font      = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(0, 10)
        };

        // ── Toolbar ───────────────────────────────────────────────────────────
        var pnlToolbar = new FlowLayoutPanel
        {
            Height    = 44,
            Location  = new Point(0, 45),
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent
        };

        txtSearch = new TextBox
        {
            Width = 280, Height = 30,
            Font  = new Font("Segoe UI", 10),
            PlaceholderText = "🔍 Tìm kiếm theo tên...",
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White
        };
        txtSearch.TextChanged += async (s, e) => await SearchAsync(txtSearch.Text);

        btnAdd  = MakeToolBtn("➕ Thêm mới",   Color.FromArgb(5,  150, 105));
        btnEdit = MakeToolBtn("✏️ Chỉnh sửa",  Color.FromArgb(59, 130, 246));
        btnDiag = MakeToolBtn("🔬 Chẩn đoán",  Color.FromArgb(124,58,  237));

        btnAdd.Click  += BtnAdd_Click;
        btnEdit.Click += BtnEdit_Click;
        btnDiag.Click += BtnDiag_Click;

        pnlToolbar.Controls.AddRange(new Control[]
        { txtSearch, btnAdd, btnEdit, btnDiag });

        // ── DataGridView ──────────────────────────────────────────────────────
        dgv = new DataGridView
        {
            Location          = new Point(0, 100),
            BackgroundColor   = Color.FromArgb(30, 41, 59),
            GridColor         = Color.FromArgb(51, 65, 85),
            ForeColor         = Color.White,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15,23,42),
                ForeColor = Color.FromArgb(56,189,248),
                Font      = new Font("Segoe UI", 9, FontStyle.Bold)
            },
            DefaultCellStyle   = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(30,41,59),
                ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(56,189,248),
                SelectionForeColor = Color.Black
            },
            RowHeadersVisible  = false,
            AllowUserToAddRows = false,
            ReadOnly           = true,
            SelectionMode      = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Anchor             = AnchorStyles.Top | AnchorStyles.Bottom
                                 | AnchorStyles.Left | AnchorStyles.Right
        };

        dgv.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "PatientID", HeaderText = "ID",         Width = 50 },
            new DataGridViewTextBoxColumn { Name = "FullName",  HeaderText = "Họ tên",     FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "Age",       HeaderText = "Tuổi",       Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Gender",    HeaderText = "Giới tính",  Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Phone",     HeaderText = "Điện thoại", FillWeight = 20 },
            new DataGridViewTextBoxColumn { Name = "CreatedAt", HeaderText = "Ngày tạo",  Width = 130 }
        );

        Controls.AddRange(new Control[] { lblTitle, pnlToolbar, dgv });

        // Resize handler
        SizeChanged += (s, e) =>
        {
            pnlToolbar.Width = Width;
            dgv.Size = new Size(Width, Height - 105);
        };
    }

    private async Task SearchAsync(string keyword)
    {
        await Task.Delay(300);   // Debounce 300ms
        _patients = string.IsNullOrWhiteSpace(keyword)
            ? await Task.Run(() => DatabaseService.GetAllPatients())
            : await Task.Run(() => DatabaseService.SearchPatients(keyword));
        BindGrid();
    }

    private void LoadPatients()
    {
        _patients = DatabaseService.GetAllPatients();
        BindGrid();
    }

    private void BindGrid()
    {
        dgv.Rows.Clear();
        foreach (var p in _patients)
        {
            dgv.Rows.Add(
                p.PatientID,
                p.FullName,
                p.Age,
                p.Gender == 'M' ? "Nam" : "Nữ",
                p.Phone ?? "—",
                p.CreatedAt.ToString("dd/MM/yyyy HH:mm")
            );
        }
    }

    private Patient? GetSelectedPatient()
    {
        if (dgv.SelectedRows.Count == 0) return null;
        int id = (int)dgv.SelectedRows[0].Cells["PatientID"].Value;
        return _patients.Find(p => p.PatientID == id);
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        var dlg = new PatientEditDialog(null);
        if (dlg.ShowDialog() == DialogResult.OK)
            LoadPatients();
    }

    private void BtnEdit_Click(object? sender, EventArgs e)
    {
        var p = GetSelectedPatient();
        if (p is null) { ShowSelectWarning(); return; }
        var dlg = new PatientEditDialog(p);
        if (dlg.ShowDialog() == DialogResult.OK)
            LoadPatients();
    }

    private void BtnDiag_Click(object? sender, EventArgs e)
    {
        var p = GetSelectedPatient();
        if (p is null) { ShowSelectWarning(); return; }

        // Thay thế panel hiện tại bằng DiagnosisPanel
        var parent = Parent;
        if (parent is null) return;
        parent.Controls.Clear();
        parent.Controls.Add(new DiagnosisPanel(p) { Dock = DockStyle.Fill });
    }

    private static void ShowSelectWarning() =>
        MessageBox.Show("Vui lòng chọn một bệnh nhân từ danh sách.",
            "Chưa chọn bệnh nhân", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static Button MakeToolBtn(string text, Color bg) => new()
    {
        Text      = text,
        Height    = 34,
        Width     = 130,
        Margin    = new Padding(4, 5, 0, 0),
        FlatStyle = FlatStyle.Flat,
        BackColor = bg,
        ForeColor = Color.White,
        Font      = new Font("Segoe UI", 9, FontStyle.Bold),
        Cursor    = Cursors.Hand
    };
}


// ─── DIALOG THÊM / CHỈNH SỬA BỆNH NHÂN ─────────────────────────────────────
public class PatientEditDialog : Form
{
    private readonly Patient? _existing;

    private TextBox      txtName    = null!;
    private DateTimePicker dtpDob   = null!;
    private ComboBox     cmbGender  = null!;
    private TextBox      txtPhone   = null!;
    private TextBox      txtAddress = null!;
    private CheckBox     chkSmoker  = null!;
    private TextBox      txtHistory = null!;
    private Button       btnSave    = null!;

    public PatientEditDialog(Patient? existing)
    {
        _existing = existing;
        InitializeComponent();
        if (existing is not null) FillForm(existing);
    }

    private void InitializeComponent()
    {
        Text            = _existing is null ? "Thêm bệnh nhân mới" : "Chỉnh sửa hồ sơ";
        Size            = new Size(460, 500);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        BackColor       = Color.FromArgb(15, 23, 42);

        int y = 20;
        Controls.Add(MakeLabel("Họ và tên *", 20, y));
        txtName = MakeTextBox(20, y + 22, 400); Controls.Add(txtName); y += 70;

        Controls.Add(MakeLabel("Ngày sinh *", 20, y));
        dtpDob = new DateTimePicker
        { Location = new Point(20, y + 22), Width = 180, Format = DateTimePickerFormat.Short };
        Controls.Add(dtpDob); y += 70;

        Controls.Add(MakeLabel("Giới tính", 20, y));
        cmbGender = new ComboBox
        {
            Location = new Point(20, y + 22), Width = 100,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbGender.Items.AddRange(["Nam", "Nữ"]);
        cmbGender.SelectedIndex = 0;
        Controls.Add(cmbGender); y += 70;

        Controls.Add(MakeLabel("Điện thoại", 20, y));
        txtPhone = MakeTextBox(20, y + 22, 180); Controls.Add(txtPhone); y += 70;

        Controls.Add(MakeLabel("Địa chỉ", 20, y));
        txtAddress = MakeTextBox(20, y + 22, 400); Controls.Add(txtAddress); y += 70;

        chkSmoker = new CheckBox
        {
            Text = "Hút thuốc lá", ForeColor = Color.White,
            Font = new Font("Segoe UI", 9), Location = new Point(20, y), AutoSize = true
        };
        Controls.Add(chkSmoker); y += 35;

        Controls.Add(MakeLabel("Tiền sử bệnh", 20, y));
        txtHistory = new TextBox
        {
            Location = new Point(20, y + 22), Size = new Size(400, 60),
            Multiline = true, BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White
        };
        Controls.Add(txtHistory); y += 92;

        btnSave = new Button
        {
            Text = _existing is null ? "Lưu bệnh nhân" : "Cập nhật",
            Location = new Point(20, y), Size = new Size(400, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(5, 150, 105), ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand
        };
        btnSave.Click += BtnSave_Click;
        Controls.Add(btnSave);

        Height = y + 80;
        AcceptButton = btnSave;
    }

    private void FillForm(Patient p)
    {
        txtName.Text           = p.FullName;
        dtpDob.Value           = p.DateOfBirth;
        cmbGender.SelectedIndex = p.Gender == 'M' ? 0 : 1;
        txtPhone.Text          = p.Phone   ?? string.Empty;
        txtAddress.Text        = p.Address ?? string.Empty;
        chkSmoker.Checked      = p.IsSmoker;
        txtHistory.Text        = p.MedicalHistory ?? string.Empty;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Vui lòng nhập họ tên bệnh nhân.", "Thiếu thông tin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var p = _existing ?? new Patient();
        p.FullName       = txtName.Text.Trim();
        p.DateOfBirth    = dtpDob.Value.Date;
        p.Gender         = cmbGender.SelectedIndex == 0 ? 'M' : 'F';
        p.Phone          = string.IsNullOrWhiteSpace(txtPhone.Text)  ? null : txtPhone.Text.Trim();
        p.Address        = string.IsNullOrWhiteSpace(txtAddress.Text)? null : txtAddress.Text.Trim();
        p.IsSmoker       = chkSmoker.Checked;
        p.MedicalHistory = string.IsNullOrWhiteSpace(txtHistory.Text)? null : txtHistory.Text.Trim();

        try
        {
            if (_existing is null)
                DatabaseService.InsertPatient(p, SessionContext.UserId);
            else
                DatabaseService.UpdatePatient(p);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi lưu: {ex.Message}", "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text, ForeColor = Color.Silver,
        Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(x, y)
    };
    private static TextBox MakeTextBox(int x, int y, int w) => new()
    {
        Location = new Point(x, y), Width = w,
        BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White,
        Font = new Font("Segoe UI", 10)
    };
}


// ─── DIALOG CHỌN BỆNH NHÂN ──────────────────────────────────────────────────
public class PatientSelectorDialog : Form
{
    public Patient? SelectedPatient { get; private set; }

    private DataGridView dgv     = null!;
    private TextBox      txtSrch = null!;
    private List<Patient> _list  = [];

    public PatientSelectorDialog()
    {
        InitializeComponent();
        Load();
    }

    private void InitializeComponent()
    {
        Text = "Chọn bệnh nhân"; Size = new Size(600, 450);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(15, 23, 42);

        txtSrch = new TextBox
        {
            Dock = DockStyle.Top, Height = 32,
            PlaceholderText = "🔍 Tìm theo tên...",
            BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        txtSrch.TextChanged += async (s, e) =>
        {
            await Task.Delay(300);
            _list = string.IsNullOrWhiteSpace(txtSrch.Text)
                ? DatabaseService.GetAllPatients()
                : DatabaseService.SearchPatients(txtSrch.Text);
            BindGrid();
        };

        dgv = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            BackgroundColor = Color.FromArgb(30,41,59),
            DefaultCellStyle = new DataGridViewCellStyle
            { BackColor = Color.FromArgb(30,41,59), ForeColor = Color.White }
        };
        dgv.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name="PatientID", HeaderText="ID",     Width=50 },
            new DataGridViewTextBoxColumn { Name="FullName",  HeaderText="Họ tên", FillWeight=30 },
            new DataGridViewTextBoxColumn { Name="Age",       HeaderText="Tuổi",   Width=60 },
            new DataGridViewTextBoxColumn { Name="Gender",    HeaderText="Giới",   Width=60 }
        );
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgv.CellDoubleClick += (s, e) => SelectAndClose();

        var btnSelect = new Button
        {
            Text = "Chọn →", Dock = DockStyle.Bottom, Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(124,58,237), ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        btnSelect.Click += (s, e) => SelectAndClose();

        Controls.AddRange(new Control[] { dgv, btnSelect, txtSrch });
    }

    private void Load()
    {
        _list = DatabaseService.GetAllPatients();
        BindGrid();
    }

    private void BindGrid()
    {
        dgv.Rows.Clear();
        foreach (var p in _list)
            dgv.Rows.Add(p.PatientID, p.FullName, p.Age, p.Gender == 'M' ? "Nam" : "Nữ");
    }

    private void SelectAndClose()
    {
        if (dgv.SelectedRows.Count == 0) return;
        int id = (int)dgv.SelectedRows[0].Cells["PatientID"].Value;
        SelectedPatient = _list.Find(p => p.PatientID == id);
        DialogResult = DialogResult.OK;
        Close();
    }
}
