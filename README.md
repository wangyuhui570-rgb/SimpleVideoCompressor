# SimpleVideoCompressor（极简视频压缩工具）

Windows 10/11 极简视频压缩桌面软件：**零外部软件依赖**（不需要装 VidCoder、HandBrake 或 FFmpeg），直接调用 Windows 系统自带的硬件加速转码引擎（`Windows.Media.Transcoding.MediaTranscoder`），把视频压缩到指定 MB 以内。

## 功能特点

- **极简操作**：拖入视频（或点「选择视频」）→ 选目标大小（默认 `95 MB`，快捷按钮 `95 MB / 50 MB / 20 MB`）→ 点「开始压缩」。
- **零外部依赖**：不依赖任何第三方转码软件，自动输出为手机/微信/网页通用的 H.264 MP4（保存为 `原文件名_压缩版.mp4`，不覆盖原文件）。
- **增量更新设计**（参考 `QuickReplyTool`）：
  - 每次发布 `vX.Y.Z` tag 时，GitHub Actions 自动对比上一版本的便携包文件 SHA256，生成仅包含变更文件的 `SimpleVideoCompressor-Patch-<from>-to-<to>.zip` 与 `patch-manifest.json`。
  - 客户端点击「检查更新」时优先下载几百 KB 的增量补丁包，并在 staging 目录逐文件校验 SHA256 后无缝替换 `app/` 目录重启；若跨多版本或校验失败，自动回退为完整包更新。

## 使用方法

1. 从本仓库 [Releases](https://github.com/wangyuhui570-rgb/SimpleVideoCompressor/releases) 下载 `SimpleVideoCompressor-Portable-x.y.z.zip` 并解压。
2. 双击 **`启动极简视频压缩.bat`**（或直接运行 `app\SimpleVideoCompressor.exe`）。
3. 拖入视频、设置目标 MB，点 **「开始压缩」**。

## 发布新版本（自动构建完整包 + 增量补丁）

1. 修改 `src/SimpleVideoCompressor/SimpleVideoCompressor.csproj` 中的 `<Version>`（如 `1.0.1`）。
2. 提交并推送到 `main`，然后打 tag 并推送：
   ```bash
   git tag v1.0.1
   git push origin v1.0.1
   ```
3. GitHub Actions 会自动在 `windows-latest` 上编译、生成完整便携包与增量补丁包，并发布到 Releases。
