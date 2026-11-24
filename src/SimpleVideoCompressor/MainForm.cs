using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using SimpleVideoCompressor.Services;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SimpleVideoCompressor;

public sealed class MainForm : Form
{
    private readonly TextBox _txtInput = new();
    private readonly Button _btnBrowse = new();
    private readonly Label _lblInfo = new();
    private readonly NumericUpDown _numTarget = new();
    private readonly CheckBox _chk1080p = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnCheckUpdate = new();
    private readonly AppUpdateService _updateService = new();
    private bool _isRunning;

    public MainForm()
    {
        string ver = AppUpdateService.GetCurrentVersionString();
        Text = $"极简视频压缩工具 v{ver}";
        Size = new Size(520, 410);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AllowDrop = true;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        var lblTitle = new Label
        {
            Text = "1. 选择或直接拖入要压缩的视频：",
            Location = new Point(20, 18),
            Size = new Size(340, 22)
        };
        Controls.Add(lblTitle);

        _btnCheckUpdate.Text = "检查更新";
        _btnCheckUpdate.Location = new Point(405, 14);
        _btnCheckUpdate.Size = new Size(80, 26);
        _btnCheckUpdate.Click += async (_, _) => await CheckUpdateInteractiveAsync();
        Controls.Add(_btnCheckUpdate);

        _txtInput.Location = new Point(20, 48);
        _txtInput.Size = new Size(375, 26);
        _txtInput.ReadOnly = true;
        _txtInput.BackColor = Color.White;
        _txtInput.PlaceholderText = "把视频文件拖到这里，或点击右侧选择...";
        Controls.Add(_txtInput);

        _btnBrowse.Text = "选择视频";
        _btnBrowse.Location = new Point(405, 47);
        _btnBrowse.Size = new Size(80, 28);
        _btnBrowse.Click += (_, _) => BrowseFile();
        Controls.Add(_btnBrowse);

        _lblInfo.Text = "当前文件：未选择";
        _lblInfo.Location = new Point(20, 82);
        _lblInfo.Size = new Size(465, 22);
        _lblInfo.ForeColor = Color.DimGray;
        Controls.Add(_lblInfo);

        var lblTarget = new Label
        {
            Text = "2. 目标大小（压缩到多少 MB 以内）：",
            Location = new Point(20, 122),
            Size = new Size(460, 22)
        };
        Controls.Add(lblTarget);

        _numTarget.Location = new Point(20, 151);
        _numTarget.Size = new Size(90, 26);
        _numTarget.Minimum = 5;
        _numTarget.Maximum = 4000;
        _numTarget.Value = 95;
        Controls.Add(_numTarget);

        var lblMb = new Label
        {
            Text = "MB",
            Location = new Point(116, 154),
            Size = new Size(30, 22)
        };
        Controls.Add(lblMb);

        AddPresetButton("95 MB (<100M)", 160, 110, 95);
        AddPresetButton("50 MB", 280, 95, 50);
        AddPresetButton("20 MB", 385, 100, 20);

        _chk1080p.Text = "限制最高分辨率为 1080p（推荐，更省体积且手机/网页通用）";
        _chk1080p.Location = new Point(20, 192);
        _chk1080p.Size = new Size(465, 24);
        _chk1080p.Checked = true;
        Controls.Add(_chk1080p);

        _progressBar.Location = new Point(20, 236);
        _progressBar.Size = new Size(465, 22);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        Controls.Add(_progressBar);

        _lblStatus.Text = "准备就绪。输出文件会自动保存在原视频同一文件夹。";
        _lblStatus.Location = new Point(20, 266);
        _lblStatus.Size = new Size(465, 22);
        _lblStatus.ForeColor = Color.DimGray;
        Controls.Add(_lblStatus);

        _btnStart.Text = "开始压缩";
        _btnStart.Location = new Point(180, 306);
        _btnStart.Size = new Size(150, 38);
        _btnStart.BackColor = Color.FromArgb(0, 120, 215);
        _btnStart.ForeColor = Color.White;
        _btnStart.FlatStyle = FlatStyle.Flat;
        _btnStart.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _btnStart.Click += async (_, _) => await StartCompressAsync();
        Controls.Add(_btnStart);

        DragEnter += (_, e) =>
        {
            if (!_isRunning && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };

        DragDrop += (_, e) =>
        {
            if (_isRunning) return;
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                SetInputFile(files[0]);
        };

        Shown += async (_, _) => await CheckUpdateSilentlyAsync();
    }

    private void AddPresetButton(string text, int x, int width, decimal value)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, 149),
            Size = new Size(width, 28)
        };
        btn.Click += (_, _) => _numTarget.Value = value;
        Controls.Add(btn);
    }

    private void BrowseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.m4v|所有文件|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            SetInputFile(dlg.FileName);
    }

    private void SetInputFile(string path)
    {
        if (!File.Exists(path)) return;
        _txtInput.Text = path;
        double sizeMb = new FileInfo(path).Length / (1024.0 * 1024.0);
        _lblInfo.Text = $"当前文件：{Path.GetFileName(path)}（原大小：{sizeMb:F2} MB）";
    }

    private async Task CheckUpdateSilentlyAsync()
    {
        try
        {
            var rel = await _updateService.CheckForUpdateAsync();
            if (rel is not null && !IsDisposed)
            {
                _btnCheckUpdate.Text = $"有新版 v{rel.Version.ToString(3)}";
                _btnCheckUpdate.BackColor = Color.FromArgb(255, 243, 205);
            }
        }
        catch
        {
            // 启动静默检查失败不影响主功能
        }
    }

    private async Task CheckUpdateInteractiveAsync()
    {
        if (_isRunning) return;
        _btnCheckUpdate.Enabled = false;
        try
        {
            var rel = await _updateService.CheckForUpdateAsync();
            if (rel is null)
            {
                MessageBox.Show($"当前已是最新版本（v{AppUpdateService.GetCurrentVersionString()}）。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string sizeHint = rel.PatchUrl is not null
                ? $"增量补丁约 {rel.PatchSize / 1024.0:F0} KB"
                : $"完整包约 {rel.FullZipSize / (1024.0 * 1024.0):F1} MB";

            var ans = MessageBox.Show(
                $"发现新版本 v{rel.Version.ToString(3)}（{sizeHint}）。\n\n是否立即更新并自动重启？",
                "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (ans != DialogResult.Yes) return;

            _isRunning = true;
            _btnStart.Enabled = false;
            _btnBrowse.Enabled = false;

            bool shouldExit = await _updateService.DownloadAndApplyUpdateAsync(rel, status =>
            {
                if (!IsDisposed)
                    BeginInvoke(() => _lblStatus.Text = status);
            });

            if (shouldExit)
            {
                Application.Exit();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"检查或应用更新失败：\n{ex.Message}", "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _isRunning = false;
            _btnStart.Enabled = true;
            _btnBrowse.Enabled = true;
            _btnCheckUpdate.Enabled = true;
        }
    }

    private async Task StartCompressAsync()
    {
        string inputPath = _txtInput.Text;
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            MessageBox.Show("请先选择或拖入一个视频文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        double targetMb = (double)_numTarget.Value;
        double origMb = new FileInfo(inputPath).Length / (1024.0 * 1024.0);
        if (origMb <= targetMb)
        {
            var res = MessageBox.Show(
                $"原视频当前只有 {origMb:F2} MB，已经小于目标 {targetMb} MB，确定还要继续压缩吗？",
                "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes) return;
        }

        _isRunning = true;
        _btnStart.Enabled = false;
        _btnBrowse.Enabled = false;
        _btnCheckUpdate.Enabled = false;
        _progressBar.Value = 0;
        _lblStatus.Text = "正在读取视频信息并准备压缩...";

        try
        {
            StorageFile inputFile = await StorageFile.GetFileFromPathAsync(inputPath);
            var videoProps = await inputFile.Properties.GetVideoPropertiesAsync();
            double durationSec = videoProps.Duration.TotalSeconds;
            if (durationSec <= 0)
            {
                MessageBox.Show("无法读取视频时长，请确认视频文件未损坏。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            uint audioBps = 128_000;
            double totalBps = (targetMb * 0.95 * 8.0 * 1024.0 * 1024.0) / durationSec;
            if (totalBps <= audioBps + 150_000)
                audioBps = 64_000;
            uint videoBps = (uint)Math.Max(120_000, totalBps - audioBps);

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            uint w = videoProps.Width;
            uint h = videoProps.Height;
            if (_chk1080p.Checked && (w > 1920 || h > 1080) && w > 0 && h > 0)
            {
                double scale = Math.Min(1920.0 / w, 1080.0 / h);
                w = (uint)(Math.Floor(w * scale / 2.0) * 2);
                h = (uint)(Math.Floor(h * scale / 2.0) * 2);
            }
            if (w >= 2 && h >= 2)
            {
                profile.Video.Width = (w / 2) * 2;
                profile.Video.Height = (h / 2) * 2;
            }
            profile.Video.Bitrate = videoBps;
            profile.Audio.Bitrate = audioBps;

            string dir = Path.GetDirectoryName(inputPath)!;
            string outName = Path.GetFileNameWithoutExtension(inputPath) + "_压缩版.mp4";
            string outPath = Path.Combine(dir, outName);

            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(dir);
            StorageFile outputFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prep = await transcoder.PrepareFileTranscodeAsync(inputFile, outputFile, profile);
            if (!prep.CanTranscode)
            {
                MessageBox.Show($"无法转码该视频：{prep.FailureReason}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var op = prep.TranscodeAsync();
            op.Progress = (_, pct) =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    int val = Math.Clamp((int)Math.Round(pct), 0, 100);
                    _progressBar.Value = val;
                    _lblStatus.Text = $"正在压缩：{val}%";
                });
            };
            await op;

            _progressBar.Value = 100;
            double finalMb = new FileInfo(outPath).Length / (1024.0 * 1024.0);
            _lblStatus.Text = $"压缩完成！输出大小：{finalMb:F2} MB";

            var openRes = MessageBox.Show(
                $"压缩完成！\n输出文件：{outName}\n压缩后大小：{finalMb:F2} MB\n\n是否立即打开文件所在位置？",
                "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (openRes == DialogResult.Yes)
                Process.Start("explorer.exe", $"/select,\"{outPath}\"");
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "压缩出错。";
            MessageBox.Show($"压缩过程中出错：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isRunning = false;
            _btnStart.Enabled = true;
            _btnBrowse.Enabled = true;
            _btnCheckUpdate.Enabled = true;
        }
    }
}
