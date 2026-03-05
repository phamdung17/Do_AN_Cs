namespace ChestAI.Forms;

partial class MainForm
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
        this.pnlSidebar   = new System.Windows.Forms.Panel();
        this.lblLogo      = new System.Windows.Forms.Label();
        this.lblWelcome   = new System.Windows.Forms.Label();
        this.btnDashboard = new System.Windows.Forms.Button();
        this.btnPatients  = new System.Windows.Forms.Button();
        this.btnDiagnosis = new System.Windows.Forms.Button();
        this.btnLogout    = new System.Windows.Forms.Button();
        this.pnlContent   = new System.Windows.Forms.Panel();
        this.pnlSidebar.SuspendLayout();
        this.SuspendLayout();
        //
        // pnlSidebar
        //
        this.pnlSidebar.BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
        this.pnlSidebar.Controls.AddRange(new System.Windows.Forms.Control[] {
            this.btnLogout,
            this.btnDiagnosis,
            this.btnPatients,
            this.btnDashboard,
            this.lblWelcome,
            this.lblLogo });
        this.pnlSidebar.Dock  = System.Windows.Forms.DockStyle.Left;
        this.pnlSidebar.Name  = "pnlSidebar";
        this.pnlSidebar.Width = 220;
        //
        // lblLogo
        //
        this.lblLogo.Dock      = System.Windows.Forms.DockStyle.Top;
        this.lblLogo.Font      = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
        this.lblLogo.ForeColor = System.Drawing.Color.FromArgb(56, 189, 248);
        this.lblLogo.Height    = 70;
        this.lblLogo.Name      = "lblLogo";
        this.lblLogo.Text      = "ChestAI";
        this.lblLogo.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        //
        // lblWelcome
        //
        this.lblWelcome.Dock      = System.Windows.Forms.DockStyle.Top;
        this.lblWelcome.Font      = new System.Drawing.Font("Segoe UI", 9F);
        this.lblWelcome.ForeColor = System.Drawing.Color.Silver;
        this.lblWelcome.Height    = 55;
        this.lblWelcome.Name      = "lblWelcome";
        this.lblWelcome.Text      = "Xin chao,";
        this.lblWelcome.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        //
        // btnDashboard
        //
        this.btnDashboard.Cursor    = System.Windows.Forms.Cursors.Hand;
        this.btnDashboard.Dock      = System.Windows.Forms.DockStyle.Top;
        this.btnDashboard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnDashboard.FlatAppearance.BorderSize = 0;
        this.btnDashboard.Font      = new System.Drawing.Font("Segoe UI", 10F);
        this.btnDashboard.ForeColor = System.Drawing.Color.White;
        this.btnDashboard.Height    = 48;
        this.btnDashboard.Name      = "btnDashboard";
        this.btnDashboard.Padding   = new System.Windows.Forms.Padding(15, 0, 0, 0);
        this.btnDashboard.Text      = "Dashboard";
        this.btnDashboard.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnDashboard.Click    += new System.EventHandler(this.BtnDashboard_Click);
        //
        // btnPatients
        //
        this.btnPatients.Cursor    = System.Windows.Forms.Cursors.Hand;
        this.btnPatients.Dock      = System.Windows.Forms.DockStyle.Top;
        this.btnPatients.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnPatients.FlatAppearance.BorderSize = 0;
        this.btnPatients.Font      = new System.Drawing.Font("Segoe UI", 10F);
        this.btnPatients.ForeColor = System.Drawing.Color.White;
        this.btnPatients.Height    = 48;
        this.btnPatients.Name      = "btnPatients";
        this.btnPatients.Padding   = new System.Windows.Forms.Padding(15, 0, 0, 0);
        this.btnPatients.Text      = "Benh nhan";
        this.btnPatients.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnPatients.Click    += new System.EventHandler(this.BtnPatients_Click);
        //
        // btnDiagnosis
        //
        this.btnDiagnosis.Cursor    = System.Windows.Forms.Cursors.Hand;
        this.btnDiagnosis.Dock      = System.Windows.Forms.DockStyle.Top;
        this.btnDiagnosis.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnDiagnosis.FlatAppearance.BorderSize = 0;
        this.btnDiagnosis.Font      = new System.Drawing.Font("Segoe UI", 10F);
        this.btnDiagnosis.ForeColor = System.Drawing.Color.White;
        this.btnDiagnosis.Height    = 48;
        this.btnDiagnosis.Name      = "btnDiagnosis";
        this.btnDiagnosis.Padding   = new System.Windows.Forms.Padding(15, 0, 0, 0);
        this.btnDiagnosis.Text      = "Chan doan moi";
        this.btnDiagnosis.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnDiagnosis.Click    += new System.EventHandler(this.BtnDiagnosis_Click);
        //
        // btnLogout
        //
        this.btnLogout.BackColor   = System.Drawing.Color.FromArgb(127, 29, 29);
        this.btnLogout.Cursor      = System.Windows.Forms.Cursors.Hand;
        this.btnLogout.Dock        = System.Windows.Forms.DockStyle.Bottom;
        this.btnLogout.FlatStyle   = System.Windows.Forms.FlatStyle.Flat;
        this.btnLogout.FlatAppearance.BorderSize = 0;
        this.btnLogout.Font        = new System.Drawing.Font("Segoe UI", 10F);
        this.btnLogout.ForeColor   = System.Drawing.Color.White;
        this.btnLogout.Height      = 45;
        this.btnLogout.Name        = "btnLogout";
        this.btnLogout.Padding     = new System.Windows.Forms.Padding(15, 0, 0, 0);
        this.btnLogout.Text        = "Dang xuat";
        this.btnLogout.TextAlign   = System.Drawing.ContentAlignment.MiddleLeft;
        this.btnLogout.Click      += new System.EventHandler(this.BtnLogout_Click);
        //
        // pnlContent
        //
        this.pnlContent.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
        this.pnlContent.Dock      = System.Windows.Forms.DockStyle.Fill;
        this.pnlContent.Name      = "pnlContent";
        this.pnlContent.Padding   = new System.Windows.Forms.Padding(10);
        //
        // MainForm
        //
        this.BackColor    = System.Drawing.Color.FromArgb(15, 23, 42);
        this.ClientSize   = new System.Drawing.Size(1264, 761);
        this.MinimumSize  = new System.Drawing.Size(1100, 700);
        this.Name         = "MainForm";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text          = "ChestAI";
        this.Controls.Add(this.pnlContent);
        this.Controls.Add(this.pnlSidebar);
        this.pnlSidebar.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    private System.Windows.Forms.Panel  pnlSidebar;
    private System.Windows.Forms.Panel  pnlContent;
    private System.Windows.Forms.Label  lblLogo;
    private System.Windows.Forms.Label  lblWelcome;
    private System.Windows.Forms.Button btnDashboard;
    private System.Windows.Forms.Button btnPatients;
    private System.Windows.Forms.Button btnDiagnosis;
    private System.Windows.Forms.Button btnLogout;
}
