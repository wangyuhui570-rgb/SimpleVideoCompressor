using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SimpleVideoCompressor.Services;

internal sealed record UpdateReleaseInfo(
    Version Version,
    string TagName,
    string FullZipUrl,
    string FullZipSha256,
    long FullZipSize,
    string? PatchFrom,
    string? PatchUrl,
    string? PatchSha256,
    long PatchSize);

internal sealed class AppUpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/wangyuhui570-rgb/SimpleVideoCompressor/releases?per_page=10";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimpleVideoCompressor", GetCurrentVersionString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version GetCurrentVersion()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return AppUpdatePolicy.TryParseVersion(raw) ?? new Version(1, 0, 0);
    }

    public static string GetCurrentVersionString() => GetCurrentVersion().ToString(3);

    public async Task<UpdateReleaseInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(ReleasesApiUrl, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Version current = GetCurrentVersion();
        UpdateReleaseInfo? best = null;

        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (rel.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) continue;

            string tag = rel.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            Version? ver = AppUpdatePolicy.TryParseVersion(tag);
            if (ver is null || ver <= current) continue;

            string notes = rel.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            string? fullSha = AppUpdatePolicy.ExtractSha256(notes, "SHA256");
            if (fullSha is null) continue;

            if (!rel.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array) continue;

            string? fullUrl = null;
            long fullSize = 0;
            string? patchUrl = null;
            long patchSize = 0;

            string expectedPatchName = $"{AppUpdatePolicy.PatchPrefix}{GetCurrentVersionString()}-to-{ver.ToString(3)}.zip";

            foreach (var asset in assetsEl.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                string dl = asset.TryGetProperty("browser_download_url", out var uEl) ? uEl.GetString() ?? "" : "";
                long sz = asset.TryGetProperty("size", out var sEl) ? sEl.GetInt64() : 0;

                if (name.StartsWith(AppUpdatePolicy.PortablePrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    fullUrl = dl;
                    fullSize = sz;
                }
                else if (string.Equals(name, expectedPatchName, StringComparison.OrdinalIgnoreCase))
                {
                    patchUrl = dl;
                    patchSize = sz;
                }
            }

            if (string.IsNullOrWhiteSpace(fullUrl)) continue;

            string? patchFrom = AppUpdatePolicy.ExtractField(notes, "PatchFrom");
            string? patchSha = AppUpdatePolicy.ExtractSha256(notes, "PatchSHA256");
            bool canUsePatch = !string.IsNullOrWhiteSpace(patchUrl) &&
                               patchSha is not null &&
                               AppUpdatePolicy.TryParseVersion(patchFrom) == current;

            var candidate = new UpdateReleaseInfo(
                ver,
                tag,
                fullUrl,
                fullSha,
                fullSize,
                canUsePatch ? patchFrom : null,
                canUsePatch ? patchUrl : null,
                canUsePatch ? patchSha : null,
                canUsePatch ? patchSize : 0);

            if (best is null || candidate.Version > best.Version)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// 下载更新包（优先增量补丁，失败则回退完整包），准备 staging 目录并启动外部替换脚本。
    /// 返回 true 表示已启动更新脚本，调用方应立即退出当前进程。
    /// </summary>
    public async Task<bool> DownloadAndApplyUpdateAsync(
        UpdateReleaseInfo release,
        Action<string> onStatus,
        CancellationToken ct = default)
    {
        string? installRoot = ResolveInstallRoot();
        if (installRoot is null)
        {
            throw new InvalidOperationException("当前不是便携版目录结构（缺少 app\\ 主目录），无法自动覆盖更新。请手动下载新版覆盖。");
        }

        string workDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpleVideoCompressor",
            "update");
        Directory.CreateDirectory(workDir);

        string stagingRoot = Path.Combine(workDir, "staging");
        if (Directory.Exists(stagingRoot))
        {
            Directory.Delete(stagingRoot, recursive: true);
        }

        bool patched = false;
        if (release.PatchUrl is not null && release.PatchSha256 is not null)
        {
            try
            {
                onStatus($"正在下载增量补丁（约 {release.PatchSize / 1024.0:F0} KB）...");
                string patchZip = Path.Combine(workDir, $"patch-{release.Version.ToString(3)}.zip");
                await DownloadFileAsync(release.PatchUrl, patchZip, ct);

                string actualSha = AppUpdatePatcher.ComputeSha256(patchZip);
                if (!string.Equals(actualSha, release.PatchSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("增量补丁 SHA256 校验失败，自动切换完整包");
                }

                onStatus("正在校验并合成增量补丁...");
                AppUpdatePatcher.Apply(installRoot, patchZip, stagingRoot, GetCurrentVersionString(), release.Version.ToString(3));
                patched = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onStatus("增量补丁不可用，改用完整包下载...");
            }
        }

        if (!patched)
        {
            onStatus($"正在下载完整更新包（约 {release.FullZipSize / (1024.0 * 1024.0):F1} MB）...");
            string fullZip = Path.Combine(workDir, $"portable-{release.Version.ToString(3)}.zip");
            await DownloadFileAsync(release.FullZipUrl, fullZip, ct);

            string actualSha = AppUpdatePatcher.ComputeSha256(fullZip);
            if (!string.Equals(actualSha, release.FullZipSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("完整更新包 SHA256 校验失败，已中止更新。");
            }

            onStatus("正在解压更新包...");
            ZipFile.ExtractToDirectory(fullZip, stagingRoot, overwriteFiles: true);
        }

        string stagedApp = Path.Combine(stagingRoot, "app");
        if (!File.Exists(Path.Combine(stagedApp, "SimpleVideoCompressor.exe")))
        {
            throw new InvalidDataException("更新包内缺少 app\\SimpleVideoCompressor.exe");
        }

        onStatus("正在启动替换程序并重启...");
        string scriptPath = Path.Combine(workDir, "apply-update.ps1");
        string installedApp = Path.Combine(installRoot, "app");
        string backupApp = Path.Combine(installRoot, "app.old");
        string exeToLaunch = Path.Combine(installedApp, "SimpleVideoCompressor.exe");
        int pid = Environment.ProcessId;

        string logPath = Path.Combine(workDir, "update.log");
        string ps = $@"
$ErrorActionPreference = 'Stop'
$log = '{logPath.Replace("'", "''")}'
function Log([string]$msg) {{ Add-Content -LiteralPath $log -Value ""$(Get-Date -Format 'HH:mm:ss') $msg"" -Encoding UTF8 }}
Log 'Updater started.'
$pidToWait = {pid}
for ($i = 0; $i -lt 100; $i++) {{
    if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) {{ break }}
    Start-Sleep -Milliseconds 300
}}
Start-Sleep -Milliseconds 500
$installed = '{installedApp.Replace("'", "''")}'
$staged = '{stagedApp.Replace("'", "''")}'
$backup = '{backupApp.Replace("'", "''")}'
$exe = '{exeToLaunch.Replace("'", "''")}'

if (Test-Path -LiteralPath $backup) {{ Remove-Item -LiteralPath $backup -Recurse -Force }}
$moved = $false
for ($try = 1; $try -le 6; $try++) {{
    try {{
        Move-Item -LiteralPath $installed -Destination $backup
        $moved = $true
        break
    }} catch {{
        Log ""Move attempt $try failed: $($_.Exception.Message)""
        Start-Sleep -Milliseconds 500
    }}
}}
if (-not $moved) {{ throw 'Failed to move installed app folder.' }}

try {{
    Move-Item -LiteralPath $staged -Destination $installed
    Log 'Swapped staged app into place. Launching...'
    Start-Process -FilePath $exe -WorkingDirectory $installed
    Start-Sleep -Seconds 2
    if (Test-Path -LiteralPath $backup) {{ Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue }}
    Log 'Update succeeded.'
}} catch {{
    Log ""Swap failed: $($_.Exception.Message)""
    if (-not (Test-Path -LiteralPath $installed) -and (Test-Path -LiteralPath $backup)) {{
        Move-Item -LiteralPath $backup -Destination $installed -Force
    }}
    throw
}}
";
        File.WriteAllText(scriptPath, ps, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            WorkingDirectory = workDir,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        return true;
    }

    private static string? ResolveInstallRoot()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirInfo = new DirectoryInfo(baseDir);
        if (string.Equals(dirInfo.Name, "app", StringComparison.OrdinalIgnoreCase) && dirInfo.Parent is not null)
        {
            return dirInfo.Parent.FullName;
        }
        return null;
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destinationPath);
        await src.CopyToAsync(dst, ct);
    }
}
