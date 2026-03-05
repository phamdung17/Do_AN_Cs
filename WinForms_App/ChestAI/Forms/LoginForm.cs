namespace ChestAI.Forms;

/// <summary>
/// Form đăng nhập – kiểm tra tài khoản bác sĩ với SQL Server.
/// Lưu thông tin phiên vào SessionContext sau khi đăng nhập thành công.
/// </summary>
public partial class LoginForm : Form
{
    public LoginForm()
    {
        InitializeComponent();
    }

    private async void BtnLogin_Click(object? sender, EventArgs e)
    {
        lblError.Text     = string.Empty;
        btnLogin.Enabled  = false;
        btnLogin.Text     = "Đang kiểm tra...";

        try
        {
            string user  = txtUsername.Text.Trim();
            string pass  = txtPassword.Text;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                lblError.Text = "Vui lòng nhập tên đăng nhập và mật khẩu.";
                return;
            }

            var result = await Task.Run(() => DatabaseService.ValidateLogin(user, pass));

            if (result is null)
            {
                lblError.Text = "Tên đăng nhập hoặc mật khẩu không đúng.";
                txtPassword.Clear();
                txtPassword.Focus();
                return;
            }

            // Lưu session
            SessionContext.UserId   = result.Value.userId;
            SessionContext.FullName = result.Value.fullName;
            SessionContext.Role     = result.Value.role;

            Hide();
            new MainForm().ShowDialog();
            Close();
        }
        finally
        {
            btnLogin.Enabled = true;
            btnLogin.Text    = "Đăng nhập";
        }
    }

    private void ChkShow_CheckedChanged(object? sender, EventArgs e)
    {
        txtPassword.PasswordChar = chkShow.Checked ? '\0' : '●';
    }
}

/// <summary>Lưu thông tin đăng nhập trong suốt phiên làm việc.</summary>
public static class SessionContext
{
    public static int    UserId   { get; set; }
    public static string FullName { get; set; } = string.Empty;
    public static string Role     { get; set; } = string.Empty;
}
