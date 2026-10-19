# 项目约定

## 结构与职责

- 根目录唯一解决方案为 `Silmoon.Tools.Installer.slnx`。保持经典单解决方案、同级多个 C# 项目的结构，不新增 `src/`、`tests/` 包装目录或第二个解决方案。
- `Silmoon.Tools.WindowsInstallerMaker/` 是 WinUI 3 MVVM 主项目；示例放在其 `Examples/`，构建和开发启动脚本放在其 `Scripts/`。
- `Silmoon.Tools.WindowsInstallerMaker.Core/` 负责配置、路径、目录分析、依赖验证和安装引擎，不引用 WinUI 或 UI 类型。
- `Silmoon.Tools.WindowsInstallerMaker.Tests/` 是 Core 测试项目；UI 和真实产物验证脚本放在其 `Scripts/`。
- 工具名称、主项目和主命名空间保持 `Silmoon.Tools.WindowsInstallerMaker`。公共技术说明放在 `_markdown/`。安装包与离线依赖输出放在 `Silmoon.Tools.WindowsInstallerMaker/artifacts/`，测试结果、截图和验证日志放在 `Silmoon.Tools.WindowsInstallerMaker.Tests/artifacts/`；均被 Git 忽略，不在根目录新增 `artifacts/`。

## 编辑与验证

开发者维护的代码、脚本、项目/结构化配置、XAML 和技术 Markdown 使用 **UTF-8 BOM + CRLF**。只修改任务相关文件；不手工改写 `bin/`、`obj/`、第三方或工具生成文件。保留现有 C# 风格、MVVM 分层和异步交互方式。

在仓库根目录运行：

```powershell
dotnet build ./Silmoon.Tools.Installer.slnx -c Release -p:Platform=x64
dotnet run --project ./Silmoon.Tools.WindowsInstallerMaker.Tests -c Release
```

需要启动 GUI 时使用 `Silmoon.Tools.WindowsInstallerMaker/Scripts/Build.ps1 -Run`。实际修改 UI 后使用适用的 WinUI 技能检查设计、API、代码和交互；只对本工具进程执行 UI 验证。带 `--demo` 的测试和真实打包需要准备输入文件，并替换示例中的本机路径。

## 行为边界

- Velopack 是当前唯一安装引擎。保留工具/目标包版本检查和 `VeloAppCheck`；未知或未支持的配置必须明确拒绝，不能静默忽略。
- 使用结构化进程参数，取消仅作用于本次创建的进程树。验证完成前不报告打包成功，不将半成品当作安装器。
- 离线依赖目前是经验证的独立附件，没有自动内嵌执行。更新 URL 仅为元数据；签名执行、在线入口、差分包和机器范围安装尚未支持。
- 不自动启动、安装、卸载或升级示例/参考客户端，不修改其业务源码，不删除个人数据，不进行交易。需要重新发布时遵循目标项目既有流程。
- 不在源码、示例、日志或文档中写入私钥、密码、令牌或个人数据。不自动上传产物、修改安全策略、导入证书信任、提交或推送代码。
