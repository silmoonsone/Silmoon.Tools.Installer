# Silmoon.Tools.WindowsInstallerMaker

C# / WinUI 3 原生桌面安装项目编辑器，使用 MVVM 和独立于 WinUI 的 Core，将已有 Windows publish 目录交给 Velopack 生成真实 Setup.exe。配置以带 `schemaVersion` 的 JSON 保存，可重新打开、编辑并重复构建。

## 开发与运行

克隆仓库后，在仓库根目录执行下面的命令。开发环境需要：

- Windows x64、.NET 10 SDK、Windows SDK 与 WinUI 构建环境。
- WinApp CLI 0.7+ 用于开发启动、控件/API 查询和 UI 验证。
- 打包电脑需要全局 `vpk`，版本须与目标应用及项目中的 `expectedVelopackVersion` 完全相同。本次基线是 **1.2.161**，工具不会自动安装或升级 vpk。
- 当前 GUI 使用 packaged 开发启动；开发模式须事先启用。脚本不启用开发模式、不导入证书信任、不修改 Windows 功能。

```powershell
dotnet build ./Silmoon.Tools.Installer.slnx -c Release -p:Platform=x64
dotnet run --project ./Silmoon.Tools.WindowsInstallerMaker.Tests -c Release
```

构建、运行 Core 测试并启动 GUI：

```powershell
pwsh -NoProfile -File ./Silmoon.Tools.WindowsInstallerMaker/Scripts/Build.ps1 -Configuration Release -Run
```

或从 Visual Studio 打开当前目录的 `Silmoon.Tools.Installer.slnx`，选择 **x64**。项目使用固定包版本、普通 JIT 和多文件输出；不启用裁剪、AOT 或单文件。

首次完成开发注册后，可从开始菜单打开程序，或运行 `Silmoon.Tools.WindowsInstallerMaker/Scripts/Start-WindowsInstallerMaker.ps1`。该方式依赖当前电脑的开发输出，不是可复制到其他电脑的已签名安装包。

## 仓库结构

根目录只有一个解决方案，三个 C# 项目位于同一级。工具正式名称和主命名空间为 `Silmoon.Tools.WindowsInstallerMaker`。

```text
Silmoon.Tools.Installer.slnx
Silmoon.Tools.WindowsInstallerMaker/          WinUI 3 主项目
  Examples/                                  安装项目示例与接入说明
  Scripts/                                   构建和开发启动脚本
  artifacts/                                 安装包、离线依赖与打包日志（本机）
Silmoon.Tools.WindowsInstallerMaker.Core/     配置、分析、依赖与打包逻辑
Silmoon.Tools.WindowsInstallerMaker.Tests/    Core 测试项目
  Scripts/                                   UI 和真实产物验证脚本
  artifacts/                                 测试结果、截图与验证日志（本机）
_markdown/                                   公共技术说明
```

各项目的 `bin/`、`obj/` 和 `artifacts/` 不随源码提交。主项目和测试项目也将 `artifacts/` 排除在 MSBuild 默认文件项之外，避免产物被当作源码或应用资源。根目录不放构建或测试输出。Agent 的项目约定见 [AGENTS.md](AGENTS.md)。

## 使用

1. 新建项目，或用「打开」/顶部配置路径加载已有 `.installer.json`。
2. 在「项目与文件」选择已经完成 Release publish 的目录、根目录下的主 EXE、输出目录。运行分析，查看架构、runtimeconfig、deps、PE 导入、资源与自包含证据。
3. 编辑产品 ID、显示名称、发布者、三段版本号、渠道、架构、图标与快捷方式。产品 ID、渠道和架构关系到安装与更新身份，应保持稳定。
4. 配置依赖。可使用预设，也可编辑依赖定义中的检测、最低版本、架构、来源、哈希、签名、参数、退出码、提权和重启信息。每种交付方式的实际支持范围见下表。
5. 保存项目。相对路径以**配置文件目录**为基准；另存为会重新计算路径并保持所指文件不变。新建、打开和关闭时会处理未保存修改。
6. 在「构建日志」调用真实构建。界面显示阶段、进度、工具日志、退出码及错误；成功后展示 Setup 和其他产物，可打开输出目录。

第一版以**已有 publish 目录**作为输入，不替用户恢复源码、执行任意发布脚本或修改业务程序。换版本时先按应用自己的发布流程生成新的 Release publish，再更换路径/版本并构建。目录里的任意 EXE 并不自动具有 Velopack 生命周期接入。

## 依赖交付与边界

| 方式 | 实际行为 |
| --- | --- |
| 已随应用携带 | 声明应用已有运行文件。静态分析提供 .NET、Windows App SDK 和原生导入证据；无法穷举动态加载依赖，需要开发者补充确认。 |
| 缺失时在线安装 | 只支持本版适配的 Velopack `--framework` 预设。检测、下载及执行由 Velopack 管理，不能覆盖下载地址、哈希、签名或执行参数。需要联网。 |
| 完整离线安装器 | 检查 SHA256、有效 Authenticode 签名、预期签名者及可选安装器文件版本，复制到 `prerequisites/<id>/`，与 Setup 一起分发。用户按说明先手动安装缺失依赖；没有自动执行或内嵌到 Setup。 |

类别包括 VC++ Redistributable、.NET Runtime、Desktop Runtime、.NET Framework、Windows App SDK、WebView2、自定义组件，以及分开的数据库客户端库、嵌入式数据库、数据库服务模型。后三者没有通用自动部署适配器。SDK 只作为明确的高级开发工具选项，普通用户不默认安装 .NET SDK 或 Windows SDK。

离线旁置模式中，检测条件、最低 Runtime/系统版本、安装参数、成功/重启退出码、提权和重启策略是**人工部署说明**。工具验证离线文件的完整性和签名，不会在目标电脑执行这些条件，也不会把安装器版本说成已安装 Runtime 版本。

| 能力 | 第一版状态 |
| --- | --- |
| Windows 当前用户完整 Setup、桌面/开始菜单快捷方式、图标、版本说明 | 已实现 |
| 完整更新包、渠道版本清单 | 随真实 vpk pack 生成并验证 |
| 小型在线入口、所有用户安装、代码签名执行、差分包生成 | 界面禁用/明确标记；从 JSON 强行启用也阻止构建 |
| 更新源 HTTPS 地址 | 分发元数据；不会自动给目标应用添加更新 UI、后台服务或安全退出更新逻辑 |
| macOS / Linux | 未实现；需要对应平台输入和构建环境，不能把 WinUI Windows 程序转换为跨平台应用 |

配置不提供签名密码、私钥或发布令牌字段；未知字段、未来 schema、无效 JSON 和枚举会拒绝加载。带凭据的 URL 不可作为更新源。不要把秘密写进自由文本。

## 构建可靠性

Core 负责配置、路径、验证、目录分析、依赖校验和进程调用，WinUI 只负责交互。`VelopackEngine` 是当前唯一引擎适配器，之后可以被 CLI 或流水线复用。

- `ProcessStartInfo.ArgumentList` 传递独立参数，不把用户配置拼接为 shell 命令。
- 检查目标 deps 中的 Velopack 版本、全局 vpk 和配置一致性；保留 `VeloAppCheck`，不提供跳过选项。
- 每次使用独立 `.building-<id>` 目录。校验 Setup 的 PE 结构、完整 nupkg 的身份/版本/主程序，以及渠道清单的大小和 SHA256 后，才将目录更名为成功输出。
- 取消仅停止该次创建的子进程树。失败/取消不显示有效安装器，诊断目录可能保留，不能作为交付物。
- 输出目录不能位于输入 publish 中。旧成功产物不被覆盖，安装身份也不会随机生成。
- 工具清除打包子进程继承的 `VPK_*` 覆盖，避免环境变量悄悄改变已保存设置。
- vpk 1.2.161 默认过滤 PDB、`createdump` 等诊断/开发文件。本次演示中 590 个实际入包文件均与 publish 的 SHA256 一致；另外 6 个 PDB 和 `createdump.exe` 按已核对的上游默认规则排除，详见验证记录。

成功目录形如：

```text
<outputDirectory>/<productId>-<version>-<channel>-<UTC时间>-<构建ID>/
  <productId>-<channel>-Setup.exe
  <productId>-<version>-<channel>-full.nupkg
  releases.<channel>.json
  build.log
  build-manifest.json
  DISTRIBUTION.md
  dependencies.json
  prerequisites/<id>/<offline-installer.exe>
```

请保留整份成功目录及日志和哈希清单。默认安装包、运行时、离线依赖和打包日志保存在主项目的 `artifacts/`；测试截图、测试结果和验证日志保存在测试项目的 `artifacts/`。两处均被 Git 忽略，不自动提交或上传。

## Silmoon Trader 演示

仓库包含安装项目配置示例，不包含 Trader 客户端二进制、已生成的 Setup 或微软离线安装器。首版曾从 GUI 实际生成 `0.1.0-preview.2` 的 Setup，用于验证配置编辑和重复打包；本地验证结果见 [验证记录](_markdown/verification.md)。

准备自己的发布产物后，加载 [Silmoon Trader 示例](Silmoon.Tools.WindowsInstallerMaker/Examples/Silmoon.Trader.installer.json)，替换输入路径，修改版本号或说明，保存，再在 GUI 运行构建。示例保留原产品身份：

- 产品 ID：`Silmoon.Trading.Client.WindowsClient`
- 显示名称/发布者：`Silmoon Trading` / `Silmoon`
- 渠道/架构：`win-x64-preview` / x64
- vpk / 目标 NuGet：`1.2.161`

**需要替换的本机路径**是示例的 `publishDirectory` 和 WebView2 依赖的 `offlineInstallerPath`，它们指向兄弟客户端仓库中已有的时间戳发布输出与离线安装器。它们只存在于独立演示配置，通用 Core/GUI 不包含客户端硬编码路径。输出默认为 `Silmoon.Tools.WindowsInstallerMaker/artifacts/trader`，配置中的相对值为 `../artifacts/trader`。

本次复用的客户端发布基线是 .NET 10、Windows App SDK 2.5.1、unpackaged、win-x64、`SelfContained=true`、`WindowsAppSDKSelfContained=true`、多文件、普通 JIT、无裁剪/AOT。工具不依赖本机 `PublishApp.pubxml`；本版也不自动重新 publish 客户端。

WebView2 使用已有微软完整离线安装器及已知 SHA256，构建时复验签名和哈希。**最终 Setup 不内嵌 WebView2**。需要一起交付 `prerequisites`，缺失时先手动安装微软依赖。VC CRT 静态导入没有证据并不代表所有应用或未来版本都不需要 VC++ Runtime。

客户端已保留 STAThread 并在 XAML 初始化前执行 Velopack 生命周期入口，关闭启动时自动应用更新。新应用请按 [Velopack 接入说明](Silmoon.Tools.WindowsInstallerMaker/Examples/Velopack-integration.md) 手动接入并重新发布；工具不会替用户修改业务代码。

## 数据与更新

用户数据必须位于安装 `current` 等可替换目录之外。当前引擎不添加删除个人数据的卸载钩子。工具不读取目标应用的账户、Cookie、布局、数据库、用户源码或脚本，也不自动启动目标应用。

离线制作/安装不需要下载服务器。以后在线分发可用 HTTPS 静态站点或对象存储，但目标应用仍需自行实现更新检查、UI、下载验证、安全退出与应用更新。需要协调应用生命周期与业务状态，不能把等待退出超时等同于安全退出，也不能默认强杀目标进程。

## 验证

默认 Core 测试不依赖 Trader 仓库或本机发布产物：

```powershell
dotnet run --project ./Silmoon.Tools.WindowsInstallerMaker.Tests -c Release
```

已准备并修改演示路径时，可额外运行真实目录分析和产物验证：

```powershell
dotnet run --project ./Silmoon.Tools.WindowsInstallerMaker.Tests -c Release -- --demo ./Silmoon.Tools.WindowsInstallerMaker/Examples/Silmoon.Trader.installer.json
pwsh -NoProfile -File ./Silmoon.Tools.WindowsInstallerMaker.Tests/Scripts/Verify-DemoArtifact.ps1 -ProjectFile ./Silmoon.Tools.WindowsInstallerMaker/Examples/Silmoon.Trader.installer.json -BuildDirectory '<成功构建目录>'
```

测试覆盖配置往返、另存为路径、非法/未来配置、未支持能力、无效路径、参数边界、非零退出码和取消隔离；可选演示还覆盖真实静态分析和打包失败路径。首版记录为 Core 47/47（含演示）、GUI 11/11、原生选择器 4/4；结果、产物哈希和验收边界见 [验证记录](_markdown/verification.md)。原始测试日志、截图和结果保存在本机 `Silmoon.Tools.WindowsInstallerMaker.Tests/artifacts/`；安装包及其附带日志保存在 `Silmoon.Tools.WindowsInstallerMaker/artifacts/trader/`，不在源码仓库提供。每次 Core 测试将新报告写入测试项目的 `artifacts/tests/<UTC时间>-<随机ID>/core-results.json`，保留旧报告；结果目录根据测试程序集位置定位测试项目，不依赖启动工作目录。默认 UI 结果写入其 `artifacts/ui/`。

已有产物和验证记录已按项目归档。历史日志、JSON 和截图保留原始内容，其中的旧绝对路径仅代表当时位置；当前目录映射及本机迁移完整性报告见 [验证记录](_markdown/verification.md)。

已成功打包及生命周期校验不等同于干净 Windows、标准用户安装、完整卸载、v1→v2 数据保留或更新失败恢复。上述安装/升级场景仍待专门验收；本次不安装、卸载、升级或启动用户已有交易客户端。
