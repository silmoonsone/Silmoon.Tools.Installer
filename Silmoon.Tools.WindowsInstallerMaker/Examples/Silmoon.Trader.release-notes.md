# Silmoon Trader 安装包制作演示

复用既有 Silmoon Trading Windows Release publish，通过 Silmoon.Tools.WindowsInstallerMaker 生成完整应用安装包。此演示不包含交易功能变更。

- 安装身份保持 `Silmoon.Trading.Client.WindowsClient`、`win-x64-preview`、x64。
- 普通 JIT、多文件、自包含 .NET 与 Windows App SDK；未启用裁剪、AOT 或单文件。
- Setup 未签名，不提供自动在线升级。应用已接入 Velopack 安装生命周期，并关闭启动时自动应用更新。
- WebView2 是单独随附的微软完整离线安装器；缺失时需先手动安装，不在 Setup 内自动串联。
- 用户数据保留在独立数据目录，当前工具不添加删除个人数据的卸载钩子。
- 干净系统、标准用户、完整卸载、v1→v2 数据保留及更新失败恢复仍需验收。
