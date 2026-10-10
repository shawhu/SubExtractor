#nullable enable

using System.Drawing;
using System.Windows.Forms;
using LibVLCSharp.WinForms;

namespace SubExtractor;

public partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Label lblInfo = null!;
    private Panel pnlDropZone = null!;
    private Label lblDropPrompt = null!;
    private VideoView videoView = null!;
    private TrackBar trackPosition = null!;
    private Button btnPlayStop = null!;
    private Button btnPause = null!;
    private Button btnCalibrateRedBox = null!;
    private System.Windows.Forms.Timer playbackTimer = null!;
    private Size ButtonSize = new Size(90, 60);

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblInfo = new Label();
        pnlDropZone = new Panel();
        lblDropPrompt = new Label();
        videoView = new VideoView();
        trackPosition = new TrackBar();
        btnPlayStop = new Button();
        btnPause = new Button();
        btnCalibrateRedBox = new Button();
        playbackTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        pnlDropZone.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)trackPosition).BeginInit();
        lblInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblInfo.AutoSize = false;
        lblInfo.AutoEllipsis = true;
        lblInfo.BackColor = Color.Black;
        lblInfo.ForeColor = Color.White;
        lblInfo.Location = new Point(0, 0);
        lblInfo.Padding = new Padding(10);
        lblInfo.Name = "lblInfo";
        lblInfo.Size = new Size(100, 1);
        lblInfo.TabIndex = 0;
        lblInfo.Text = "hello\nworld";
        pnlDropZone.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        pnlDropZone.BackColor = Color.Gray;
        pnlDropZone.Location = new Point(10, 10);
        pnlDropZone.Name = "pnlDropZone";
        pnlDropZone.Padding = new Padding(1);
        pnlDropZone.Size = new Size(1, 1);
        pnlDropZone.TabIndex = 1;
        pnlDropZone.Paint += PnlDropZone_Paint;
        videoView.BackColor = Color.Black;
        videoView.Dock = DockStyle.Fill;
        videoView.Name = "videoView";
        videoView.TabIndex = 1;
        lblDropPrompt.Dock = DockStyle.Fill;
        lblDropPrompt.BackColor = Color.Gray;
        lblDropPrompt.Name = "lblDropPrompt";
        lblDropPrompt.Size = new Size(1, 1);
        lblDropPrompt.TabIndex = 0;
        lblDropPrompt.Text = "Drag and drop a video file here";
        lblDropPrompt.TextAlign = ContentAlignment.MiddleCenter;
        trackPosition.AutoSize = false;
        trackPosition.Height = 30;
        trackPosition.Enabled = false;
        trackPosition.Maximum = 1000;
        trackPosition.Name = "trackPosition";
        trackPosition.TabIndex = 2;
        trackPosition.TickStyle = TickStyle.None;
        trackPosition.MouseDown += (_, _) => isDragging = true;
        trackPosition.MouseUp += TrackPosition_MouseUp;
        btnPlayStop.Enabled = false;
        btnPlayStop.Name = "btnPlayStop";
        btnPlayStop.Size = ButtonSize;
        btnPlayStop.TabIndex = 3;
        btnPlayStop.Text = "Play";
        btnPlayStop.Click += BtnPlayStop_Click;
        btnPause.Enabled = false;
        btnPause.Name = "btnPause";
        btnPause.Size = ButtonSize;
        btnPause.TabIndex = 4;
        btnPause.Text = "Pause";
        btnPause.Click += BtnPause_Click;
        btnCalibrateRedBox.Enabled = false;
        btnCalibrateRedBox.Name = "btnCalibrateRedBox";
        btnCalibrateRedBox.Size = ButtonSize;
        btnCalibrateRedBox.TabIndex = 5;
        btnCalibrateRedBox.Text = "Extract Frames";
        btnCalibrateRedBox.Click += BtnCalibrateRedBox_Click;
        playbackTimer.Interval = 100;
        playbackTimer.Enabled = true;
        playbackTimer.Tick += PlaybackTimer_Tick;
        pnlDropZone.Controls.Add(lblDropPrompt);
        pnlDropZone.Controls.Add(videoView);
        pnlDropZone.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)trackPosition).EndInit();
        Controls.Add(lblInfo);
        Controls.Add(pnlDropZone);
        Controls.Add(trackPosition);
        Controls.Add(btnPlayStop);
        Controls.Add(btnPause);
        Controls.Add(btnCalibrateRedBox);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "MainForm";
        ResumeLayout(false);
        PerformLayout();
    }
}
