# SimpleVideoCompressor（极简视频压缩工具）

Windows 10/11 极简视频压缩桌面软件：**不打包 .NET 8（安装包仅约 1 MB）**、零第三方转码软件依赖，直接调用 Windows 系统自带的硬件加速转码引擎（`Windows.Media.Transcoding.MediaTranscoder`），把视频压缩到指定 MB 以内。

## 功能特点

- **极简单文件 & 批量文件夹压缩**：
  - **单文件**：拖入视频（或点「选择视频」）→ 选目标大小（默认 `95 MB`，快捷按钮 `95 MB / 50 MB / 20 MB`）→ 点「开始压缩」。
  - **批量文件夹**：拖入文件夹（或点「选文件夹」），自动扫描文件夹内所有视频——**高于目标大小的自动批量压缩，本来就低于或等于目标大小的自动跳过**。
- **不打包 .NET 8，体积极小**：
  - 提供 **`SimpleVideoCompressor-Setup-vx.y.z.exe`** 安装程序（仅约 1–2 MB）：安装时自动检测系统是否已有 `.NET 8 Desktop Runtime`，没有则自动从微软官方下载并安装。
  - 同时提供 **`SimpleVideoCompressor-Portable-x.y.z.zip`** 便携包（仅约 200 KB）。
- **增量更新设计**（参考 `QuickReplyTool`）：
  - 每次发布 `vX.Y.Z` tag 时，GitHub Actions 自动对比上一版本的便携包文件 SHA256，生成仅包含变更文件的 `SimpleVideoCompressor-Patch-<from>-to-<to>.zip` 与 `patch-manifest.json`。
  - 客户端点击「检查更新」时优先下载极小的增量补丁包，在 staging 目录逐文件校验 SHA256 后自动替换 `app/` 目录并重启；跨版本或校验异常时自动回退完整包。

## 下载与使用

1. 前往 [Releases](https://github.com/wangyuhui570-rgb/SimpleVideoCompressor/releases) 下载最新的 **`SimpleVideoCompressor-Setup-vx.y.z.exe`** 并双击安装（若电脑未装 .NET 8 会自动下载安装）。
2. 打开「极简视频压缩工具」，拖入单个视频或整个文件夹，设置目标 MB 后点击 **「开始压缩」**。
3. 压缩后的文件会自动保存为原视频同目录下的 `原文件名_压缩版.mp4`，不会覆盖原文件。
