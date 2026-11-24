using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SimpleVideoCompressor.Services;

/// <summary>
/// 将当前已安装的 app 目录与增量补丁包合成新版 staging 目录，并逐文件校验 SHA256。
/// 补丁包格式与 scripts/build-update-patch.ps1 对齐。
/// </summary>
internal static class AppUpdatePatcher
{
    public static void Apply(string installRoot, string patchZipPath, string stagingRoot, string expectedFrom, string expectedTo)
    {
        if (Directory.Exists(stagingRoot))
        {
            Directory.Delete(stagingRoot, recursive: true);
        }

        try
        {
            ApplyCore(installRoot, patchZipPath, stagingRoot, expectedFrom, expectedTo);
        }
        catch
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private static void ApplyCore(string installRoot, string patchZipPath, string stagingRoot, string expectedFrom, string expectedTo)
    {
        using var archive = ZipFile.OpenRead(patchZipPath);
        var manifestEntry = archive.GetEntry(AppUpdatePolicy.PatchManifestFileName)
            ?? throw new InvalidDataException("补丁缺少 " + AppUpdatePolicy.PatchManifestFileName);

        var manifest = ReadManifest(manifestEntry);
        if (!SameVersion(manifest.From, expectedFrom) || !SameVersion(manifest.To, expectedTo))
        {
            throw new InvalidDataException($"补丁版本不符：{manifest.From}->{manifest.To}，需要 {expectedFrom}->{expectedTo}");
        }

        string installedApp = Path.Combine(installRoot, "app");
        if (!Directory.Exists(installedApp))
        {
            throw new DirectoryNotFoundException("找不到已安装的 app 目录：" + installedApp);
        }

        // 1. 先从当前已安装目录复制新版清单仍保留的文件
        foreach (string relative in manifest.Files.Keys)
        {
            string source = Path.Combine(installRoot, ToLocalPath(relative));
            if (File.Exists(source))
            {
                string target = Path.Combine(stagingRoot, ToLocalPath(relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
        }

        // 2. 覆盖写入补丁包内新增/变更的文件
        string stagingFull = Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') ||
                string.Equals(entry.FullName, AppUpdatePolicy.PatchManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string target = Path.GetFullPath(Path.Combine(stagingRoot, ToLocalPath(entry.FullName)));
            if (!target.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("补丁包含越界路径：" + entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        // 3. 按 manifest 逐一校验每个文件的 SHA256
        foreach (var (relative, expectedSha) in manifest.Files)
        {
            string path = Path.Combine(stagingRoot, ToLocalPath(relative));
            if (!File.Exists(path))
            {
                throw new InvalidDataException("应用补丁后缺少文件：" + relative);
            }

            if (!string.Equals(ComputeSha256(path), expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("应用补丁后文件校验失败：" + relative);
            }
        }

        if (!File.Exists(Path.Combine(stagingRoot, "app", "SimpleVideoCompressor.exe")))
        {
            throw new InvalidDataException("应用补丁后缺少 app\\SimpleVideoCompressor.exe");
        }
    }

    internal sealed record PatchManifest(string From, string To, IReadOnlyDictionary<string, string> Files);

    internal static PatchManifest ReadManifest(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in filesElement.EnumerateObject())
            {
                string relative = prop.Name.Replace('\\', '/').TrimStart('/');
                if (relative.Length == 0 || relative.Split('/').Any(p => p == ".."))
                {
                    throw new InvalidDataException("补丁清单包含越界路径：" + prop.Name);
                }

                files[relative] = prop.Value.GetString() ?? string.Empty;
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException("补丁清单没有文件列表");
        }

        return new PatchManifest(
            root.TryGetProperty("from", out var from) ? from.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("to", out var to) ? to.GetString() ?? string.Empty : string.Empty,
            files);
    }

    private static bool SameVersion(string a, string b) =>
        AppUpdatePolicy.TryParseVersion(a) is { } left &&
        AppUpdatePolicy.TryParseVersion(b) is { } right &&
        left == right;

    private static string ToLocalPath(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
