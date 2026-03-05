using ChestAI.Forms;

namespace ChestAI;

/// <summary>
/// Điểm vào ứng dụng WinForms.
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Kiểm tra kết nối SQL Server trước khi mở Login
        if (!DatabaseService.TestConnection())
        {
            MessageBox.Show(
                "Không thể kết nối đến SQL Server.\n" +
                "Vui lòng kiểm tra chuỗi kết nối trong appsettings.",
                "Lỗi kết nối CSDL",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new LoginForm());
    }
}
