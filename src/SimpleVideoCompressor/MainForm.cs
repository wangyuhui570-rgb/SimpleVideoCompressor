using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SimpleVideoCompressor.Services;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SimpleVideoCompressor;

public sealed class MainForm : Form
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v"
    };

    private readonly TextBox _txtInput = new();
    private readonly Button _btnBrowseFile = new();
    private readonly Button _btnBrowseFolder = new();
    private readonly Label _lblInfo = new();
    private readonly NumericUpDown _numTarget = new();
    private readonly CheckBox _chk1080p = new();
    private readonly CheckBox _chkSubfolders = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnCheckUpdate = new();
    private readonly AppUpdateService _updateService = new();

    private bool _isRunning;
    private bool _isFolderMode;
    private CancellationTokenSource? _cts;

    public MainForm()
    {
        string ver = AppUpdateService.GetCurrentVersionString();
        Text = $"极简视频压缩工具 v{ver}";
        Size = new Size(560, 430);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AllowDrop = true;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Icon = LoadAppIcon();

        var lblTitle = new Label
        {
            Text = "1. 拖入或选择视频文件 / 文件夹（文件夹可自动批量压缩）：",
            Location = new Point(20, 18),
            Size = new Size(410, 22)
        };
        Controls.Add(lblTitle);

        _btnCheckUpdate.Text = "检查更新";
        _btnCheckUpdate.Location = new Point(445, 14);
        _btnCheckUpdate.Size = new Size(85, 26);
        _btnCheckUpdate.Click += async (_, _) => await CheckUpdateInteractiveAsync();
        Controls.Add(_btnCheckUpdate);

        _txtInput.Location = new Point(20, 48);
        _txtInput.Size = new Size(325, 26);
        _txtInput.ReadOnly = true;
        _txtInput.BackColor = Color.White;
        _txtInput.PlaceholderText = "拖入视频或文件夹，或点击右侧选择...";
        Controls.Add(_txtInput);

        _btnBrowseFile.Text = "选择视频";
        _btnBrowseFile.Location = new Point(353, 47);
        _btnBrowseFile.Size = new Size(84, 28);
        _btnBrowseFile.Click += (_, _) => BrowseFile();
        Controls.Add(_btnBrowseFile);

        _btnBrowseFolder.Text = "选文件夹";
        _btnBrowseFolder.Location = new Point(445, 47);
        _btnBrowseFolder.Size = new Size(85, 28);
        _btnBrowseFolder.Click += (_, _) => BrowseFolder();
        Controls.Add(_btnBrowseFolder);

        _lblInfo.Text = "当前未选择文件或文件夹";
        _lblInfo.Location = new Point(20, 82);
        _lblInfo.Size = new Size(510, 22);
        _lblInfo.ForeColor = Color.DimGray;
        Controls.Add(_lblInfo);

        var lblTarget = new Label
        {
            Text = "2. 目标大小（低于此大小的视频在批量压缩时会自动跳过）：",
            Location = new Point(20, 122),
            Size = new Size(510, 22)
        };
        Controls.Add(lblTarget);

        _numTarget.Location = new Point(20, 151);
        _numTarget.Size = new Size(90, 26);
        _numTarget.Minimum = 5;
        _numTarget.Maximum = 4000;
        _numTarget.Value = 95;
        _numTarget.ValueChanged += (_, _) => RefreshSelectionSummary();
        Controls.Add(_numTarget);

        var lblMb = new Label
        {
            Text = "MB",
            Location = new Point(116, 154),
            Size = new Size(30, 22)
        };
        Controls.Add(lblMb);

        AddPresetButton("95 MB (<100M)", 160, 115, 95);
        AddPresetButton("50 MB", 285, 95, 50);
        AddPresetButton("20 MB", 390, 95, 20);

        _chk1080p.Text = "限制最高分辨率为 1080p（推荐，更省体积且手机/网页通用）";
        _chk1080p.Location = new Point(20, 192);
        _chk1080p.Size = new Size(510, 24);
        _chk1080p.Checked = true;
        Controls.Add(_chk1080p);

        _chkSubfolders.Text = "批量选择文件夹时包含子文件夹";
        _chkSubfolders.Location = new Point(20, 220);
        _chkSubfolders.Size = new Size(320, 24);
        _chkSubfolders.Checked = false;
        _chkSubfolders.CheckedChanged += (_, _) => RefreshSelectionSummary();
        Controls.Add(_chkSubfolders);

        _progressBar.Location = new Point(20, 260);
        _progressBar.Size = new Size(510, 22);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        Controls.Add(_progressBar);

        _lblStatus.Text = "准备就绪。输出文件会自动保存在原视频同目录（_压缩版.mp4）。";
        _lblStatus.Location = new Point(20, 290);
        _lblStatus.Size = new Size(510, 22);
        _lblStatus.ForeColor = Color.DimGray;
        Controls.Add(_lblStatus);

        _btnStart.Text = "开始压缩";
        _btnStart.Location = new Point(145, 330);
        _btnStart.Size = new Size(150, 38);
        _btnStart.BackColor = Color.FromArgb(0, 120, 215);
        _btnStart.ForeColor = Color.White;
        _btnStart.FlatStyle = FlatStyle.Flat;
        _btnStart.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _btnStart.Click += async (_, _) => await StartCompressAsync();
        Controls.Add(_btnStart);

        _btnStop.Text = "停止";
        _btnStop.Location = new Point(310, 330);
        _btnStop.Size = new Size(95, 38);
        _btnStop.Enabled = false;
        _btnStop.Click += (_, _) => _cts?.Cancel();
        Controls.Add(_btnStop);

        DragEnter += (_, e) =>
        {
            if (!_isRunning && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };

        DragDrop += (_, e) =>
        {
            if (_isRunning) return;
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] items && items.Length > 0)
            {
                string first = items[0];
                if (Directory.Exists(first))
                    SetInputFolder(first);
                else if (File.Exists(first))
                    SetInputFile(first);
            }
        };

        Shown += async (_, _) => await CheckUpdateSilentlyAsync();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("SimpleVideoCompressor.Assets.app.ico");
            if (stream is not null)
                return new Icon(stream);
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
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

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择包含视频的文件夹（高于目标大小的视频会自动压缩）",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            SetInputFolder(dlg.SelectedPath);
    }

    private void SetInputFile(string path)
    {
        if (!File.Exists(path)) return;
        _isFolderMode = false;
        _txtInput.Text = path;
        RefreshSelectionSummary();
    }

    private void SetInputFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        _isFolderMode = true;
        _txtInput.Text = folderPath;
        RefreshSelectionSummary();
    }

    private void RefreshSelectionSummary()
    {
        string path = _txtInput.Text;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!_isFolderMode && File.Exists(path))
        {
            double sizeMb = new FileInfo(path).Length / (1024.0 * 1024.0);
            _lblInfo.Text = $"当前文件：{Path.GetFileName(path)}（原大小：{sizeMb:F2} MB）";
        }
        else if (_isFolderMode && Directory.Exists(path))
        {
            var (needCompress, skipped) = ScanFolderVideos(path, (double)_numTarget.Value, _chkSubfolders.Checked);
            int total = needCompress.Count + skipped;
            _lblInfo.Text = $"当前文件夹：共 {total} 个视频（需压缩 {needCompress.Count} 个，≤{_numTarget.Value}MB 将跳过 {skipped} 个）";
        }
    }

    private static (List<string> ToCompress, int SkippedCount) ScanFolderVideos(string folderPath, double targetMb, bool includeSubfolders)
    {
        var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allVideos = Directory.EnumerateFiles(folderPath, "*.*", option)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_压缩版", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        long thresholdBytes = (long)(targetMb * 1024.0 * 1024.0);
        var toCompress = new List<string>();
        int skipped = 0;

        foreach (string file in allVideos)
        {
            long len = new FileInfo(file).Length;
            if (len > thresholdBytes)
                toCompress.Add(file);
            else
                skipped++;
        }

        return (toCompress, skipped);
    }

    private async Task CheckUpdateSilentlyAsync()
    {
        try
        {
            var rel = await _updateService.CheckForUpdateAsync();
            if (rel is not null && !IsDisposed)
            {
                _btnCheckUpdate.Text = $"新版 v{rel.Version.ToString(3)}";
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

            SetRunningState(true);
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
            SetRunningState(false);
        }
    }

    private void SetRunningState(bool running)
    {
        _isRunning = running;
        _btnStart.Enabled = !running;
        _btnBrowseFile.Enabled = !running;
        _btnBrowseFolder.Enabled = !running;
        _btnCheckUpdate.Enabled = !running;
        _numTarget.Enabled = !running;
        _chk1080p.Enabled = !running;
        _chkSubfolders.Enabled = !running;
        _btnStop.Enabled = running;
    }

    private async Task StartCompressAsync()
    {
        string inputPath = _txtInput.Text;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            MessageBox.Show("请先选择或拖入一个视频文件或文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        double targetMb = (double)_numTarget.Value;

        if (!_isFolderMode)
        {
            if (!File.Exists(inputPath))
            {
                MessageBox.Show("找不到选中的视频文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            double origMb = new FileInfo(inputPath).Length / (1024.0 * 1024.0);
            if (origMb <= targetMb)
            {
                var res = MessageBox.Show(
                    $"原视频当前只有 {origMb:F2} MB，已经小于目标 {targetMb} MB，确定还要继续压缩吗？",
                    "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res != DialogResult.Yes) return;
            }

            await RunSingleAsync(inputPath, targetMb);
        }
        else
        {
            if (!Directory.Exists(inputPath))
            {
                MessageBox.Show("找不到选中的文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await RunBatchAsync(inputPath, targetMb);
        }
    }

    private async Task RunSingleAsync(string inputPath, double targetMb)
    {
        _cts = new CancellationTokenSource();
        SetRunningState(true);
        _progressBar.Value = 0;
        _lblStatus.Text = "正在读取视频信息并准备压缩...";

        try
        {
            string outPath = await CompressOneFileAsync(inputPath, targetMb, pct =>
            {
                if (!IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        _progressBar.Value = pct;
                        _lblStatus.Text = $"正在压缩：{pct}%";
                    });
                }
            }, _cts.Token);

            _progressBar.Value = 100;
            double finalMb = new FileInfo(outPath).Length / (1024.0 * 1024.0);
            _lblStatus.Text = $"压缩完成！输出大小：{finalMb:F2} MB";

            var openRes = MessageBox.Show(
                $"压缩完成！\n输出文件：{Path.GetFileName(outPath)}\n压缩后大小：{finalMb:F2} MB\n\n是否立即打开文件所在位置？",
                "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (openRes == DialogResult.Yes)
                Process.Start("explorer.exe", $"/select,\"{outPath}\"");
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "已停止压缩。";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "压缩出错。";
            MessageBox.Show($"压缩过程中出错：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetRunningState(false);
        }
    }

    private async Task RunBatchAsync(string folderPath, double targetMb)
    {
        var (toCompress, skipped) = ScanFolderVideos(folderPath, targetMb, _chkSubfolders.Checked);
        if (toCompress.Count == 0)
        {
            MessageBox.Show(
                $"文件夹扫描完成：共 {skipped} 个视频，均已小于或等于 {targetMb} MB，无需压缩。",
                "无需处理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _cts = new CancellationTokenSource();
        SetRunningState(true);
        _progressBar.Value = 0;

        int success = 0;
        var failed = new List<string>();

        try
        {
            for (int i = 0; i < toCompress.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                string file = toCompress[i];
                string fileName = Path.GetFileName(file);
                int index = i + 1;

                try
                {
                    await CompressOneFileAsync(file, targetMb, pct =>
                    {
                        if (!IsDisposed)
                        {
                            BeginInvoke(() =>
                            {
                                int overall = (int)Math.Clamp(((i * 100.0 + pct) / toCompress.Count), 0, 100);
                                _progressBar.Value = overall;
                                _lblStatus.Text = $"批量 [{index}/{toCompress.Count}] {fileName}（{pct}%）| 已跳过 {skipped} 个";
                            });
                        }
                    }, _cts.Token);
                    success++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed.Add($"{fileName}：{ex.Message}");
                }
            }

            _progressBar.Value = 100;
            _lblStatus.Text = $"批量完成：成功 {success} 个，跳过 {skipped} 个，失败 {failed.Count} 个。";

            string msg = $"批量压缩完成！\n\n已压缩：{success} 个\n已跳过（≤ {targetMb} MB）：{skipped} 个\n失败：{failed.Count} 个";
            if (failed.Count > 0)
                msg += "\n\n失败明细：\n" + string.Join("\n", failed.Take(5));
            msg += "\n\n是否打开文件夹？";

            if (MessageBox.Show(msg, "批量完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start("explorer.exe", $"\"{folderPath}\"");
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = $"已停止批量压缩（已完成 {success}/{toCompress.Count} 个）。";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetRunningState(false);
            RefreshSelectionSummary();
        }
    }

    private async Task<string> CompressOneFileAsync(string inputPath, double targetMb, Action<int> onProgress, CancellationToken ct)
    {
        StorageFile inputFile = await StorageFile.GetFileFromPathAsync(inputPath);
        var videoProps = await inputFile.Properties.GetVideoPropertiesAsync();
        double durationSec = videoProps.Duration.TotalSeconds;
        if (durationSec <= 0)
            throw new InvalidDataException("无法读取视频时长。");

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
            throw new InvalidOperationException($"无法转码：{prep.FailureReason}");

        var op = prep.TranscodeAsync();
        op.Progress = (_, pct) => onProgress(Math.Clamp((int)Math.Round(pct), 0, 100));

        using var reg = ct.Register(() =>
        {
            try { op.Cancel(); } catch { }
        });

        try
        {
            await op;
        }
        catch
        {
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
            throw;
        }

        return outPath;
    }
}
