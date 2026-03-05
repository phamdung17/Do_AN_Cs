namespace ChestAI.Forms;

partial class LoginForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblTitle = new Label();
        lblSub = new Label();
        lblUser = new Label();
        txtUsername = new TextBox();
        lblPass = new Label();
        txtPassword = new TextBox();
        chkShow = new CheckBox();
        lblError = new Label();
        btnLogin = new Button();
        SuspendLayout();
        // 
        // lblTitle
        // 
        lblTitle.AutoSize = true;
        lblTitle.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(56, 189, 248);
        lblTitle.Location = new Point(120, 25);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(185, 41);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "\U0001fac1  ChestAI";
        // 
        // lblSub
        // 
        lblSub.AutoSize = true;
        lblSub.Font = new Font("Segoe UI", 9F);
        lblSub.ForeColor = Color.Silver;
        lblSub.Location = new Point(40, 68);
        lblSub.Name = "lblSub";
        lblSub.Size = new Size(253, 15);
        lblSub.TabIndex = 1;
        lblSub.Text = "Hệ thống chẩn đoán X-quang đa phương thức";
        // 
        // lblUser
        // 
        lblUser.AutoSize = true;
        lblUser.Font = new Font("Segoe UI", 9F);
        lblUser.ForeColor = Color.Silver;
        lblUser.Location = new Point(40, 105);
        lblUser.Name = "lblUser";
        lblUser.Size = new Size(86, 15);
        lblUser.TabIndex = 2;
        lblUser.Text = "Tên đăng nhập";
        // 
        // txtUsername
        // 
        txtUsername.BackColor = Color.FromArgb(30, 41, 59);
        txtUsername.BorderStyle = BorderStyle.FixedSingle;
        txtUsername.Font = new Font("Segoe UI", 10F);
        txtUsername.ForeColor = Color.White;
        txtUsername.Location = new Point(40, 125);
        txtUsername.Name = "txtUsername";
        txtUsername.PlaceholderText = "admin";
        txtUsername.Size = new Size(340, 25);
        txtUsername.TabIndex = 3;
        // 
        // lblPass
        // 
        lblPass.AutoSize = true;
        lblPass.Font = new Font("Segoe UI", 9F);
        lblPass.ForeColor = Color.Silver;
        lblPass.Location = new Point(40, 165);
        lblPass.Name = "lblPass";
        lblPass.Size = new Size(57, 15);
        lblPass.TabIndex = 4;
        lblPass.Text = "Mật khẩu";
        // 
        // txtPassword
        // 
        txtPassword.BackColor = Color.FromArgb(30, 41, 59);
        txtPassword.BorderStyle = BorderStyle.FixedSingle;
        txtPassword.Font = new Font("Segoe UI", 10F);
        txtPassword.ForeColor = Color.White;
        txtPassword.Location = new Point(40, 185);
        txtPassword.Name = "txtPassword";
        txtPassword.PasswordChar = '●';
        txtPassword.Size = new Size(340, 25);
        txtPassword.TabIndex = 5;
        // 
        // chkShow
        // 
        chkShow.AutoSize = true;
        chkShow.Font = new Font("Segoe UI", 8F);
        chkShow.ForeColor = Color.Silver;
        chkShow.Location = new Point(40, 216);
        chkShow.Name = "chkShow";
        chkShow.Size = new Size(118, 17);
        chkShow.TabIndex = 6;
        chkShow.Text = "Hiển thị mật khẩu";
        chkShow.CheckedChanged += ChkShow_CheckedChanged;
        // 
        // lblError
        // 
        lblError.AutoSize = true;
        lblError.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
        lblError.ForeColor = Color.FromArgb(248, 113, 113);
        lblError.Location = new Point(40, 220);
        lblError.Name = "lblError";
        lblError.Size = new Size(0, 13);
        lblError.TabIndex = 7;
        // 
        // btnLogin
        // 
        btnLogin.BackColor = Color.FromArgb(56, 189, 248);
        btnLogin.Cursor = Cursors.Hand;
        btnLogin.FlatAppearance.BorderSize = 0;
        btnLogin.FlatStyle = FlatStyle.Flat;
        btnLogin.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnLogin.ForeColor = Color.Black;
        btnLogin.Location = new Point(40, 240);
        btnLogin.Name = "btnLogin";
        btnLogin.Size = new Size(340, 40);
        btnLogin.TabIndex = 8;
        btnLogin.Text = "Đăng nhập";
        btnLogin.UseVisualStyleBackColor = false;
        btnLogin.Click += BtnLogin_Click;
        // 
        // LoginForm
        // 
        AcceptButton = btnLogin;
        BackColor = Color.FromArgb(15, 23, 42);
        ClientSize = new Size(429, 322);
        Controls.Add(lblTitle);
        Controls.Add(lblSub);
        Controls.Add(lblUser);
        Controls.Add(txtUsername);
        Controls.Add(lblPass);
        Controls.Add(txtPassword);
        Controls.Add(chkShow);
        Controls.Add(lblError);
        Controls.Add(btnLogin);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Name = "LoginForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ChestAI – Đăng nhập";
        ResumeLayout(false);
        PerformLayout();
    }

    private System.Windows.Forms.TextBox  txtUsername;
    private System.Windows.Forms.TextBox  txtPassword;
    private System.Windows.Forms.Button   btnLogin;
    private System.Windows.Forms.Label    lblError;
    private System.Windows.Forms.Label    lblTitle;
    private System.Windows.Forms.Label    lblSub;
    private System.Windows.Forms.Label    lblUser;
    private System.Windows.Forms.Label    lblPass;
    private System.Windows.Forms.CheckBox chkShow;
}
