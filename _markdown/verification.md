# Silmoon.Tools.Installer.WindowsMaker 验证记录

首版功能验证日期：2026-10-19，执行环境为 Windows x64 开发机。工具正式名称为 `Silmoon.Tools.Installer.WindowsMaker`；根目录唯一解决方案为 `Silmoon.Tools.Installer.slnx`，主项目、Core 类库和测试项目均位于根目录同一级。本文保留已有功能验证结果，并按当前结构给出复现命令。

下列路径均相对于仓库根目录。安装包及其打包日志位于主项目的 `artifacts/`，测试结果、截图和验证日志位于测试项目的 `artifacts/`，两处均已被 Git 忽略。原始日志、截图、客户端二进制及安装器不随源码提交；文中的文件名和校验值是验证记录，不是公开下载入口。

## 结果

| 项目 | 结果 | 原始证据 |
| --- | --- | --- |
| Release 解决方案构建 | 0 警告、0 错误，包含启用的 WinUIAnalyzer 0.7.1 | `Silmoon.Tools.Installer.Tests/artifacts/verification/release-build.log` |
| Core 自动验证 | 47/47 通过 | `Silmoon.Tools.Installer.Tests/artifacts/tests/core-results.json` |
| WinUI 主流程 | 11/11 通过，含真实打包 | `Silmoon.Tools.Installer.Tests/artifacts/ui-run-2/ui-results.json` |
| 原生打开/另存为/架构选择 | 4/4 通过 | `Silmoon.Tools.Installer.Tests/artifacts/ui-pickers-final/picker-results.json` |
| 界面截图审阅 | 最终批次全部 9 张已检查 | `Silmoon.Tools.Installer.Tests/artifacts/ui-run-2/*.png` |
| 完整包内容 | 590 个文件与 publish 的 SHA256 完全一致 | 成功目录的 `package-content-verification.json` |
| 离线 WebView2 | 复制前后校验 SHA256、有效 Microsoft Authenticode 签名及预期安装器文件版本 | 成功目录的 `build.log`、`dependencies.json` |

开发工具版本：.NET SDK 10.0.402、WinApp CLI 0.7.1、vpk 1.2.161。GUI 使用 .NET 10、Windows App SDK 2.5.1、CommunityToolkit.Mvvm 8.4.2、普通 JIT 和自包含多文件输出。没有开启裁剪、AOT 或单文件。

## 2026-10-22 命名与 unpackaged 项目对齐复验

主项目已按 `Silmoon.Trading.Client.WindowsClient.csproj` 的桌面项目类型对齐：`WindowsPackageType=None`、`net10.0-windows10.0.19041.0`、最低版本 `19041`、`win-x64`，自包含 .NET 与 Windows App SDK，普通 JIT、多文件，不启用裁剪、AOT 或单文件。移除了 MSIX 模板专用构建配置，保留本工具的 Core 引用、WinUIAnalyzer、资源和 artifacts 排除规则；没有复制客户端专用依赖或自定义启动入口。

解决方案的 Debug / Release × Any CPU / x64 四组配置映射均通过检查：WinUI 主项目始终为 x64，Core 和 Tests 为 Any CPU。Release / x64 与 Debug / Any CPU 实际构建均为 0 警告、0 错误；默认 Core 测试 38/38 通过。`Build.ps1 -Run` 和 `Start-WindowsMaker.ps1 -Configuration Release` 均成功直接启动 unpackaged 输出，不进行 MSIX 注册。

本机证据目录为 `Silmoon.Tools.Installer.Tests/artifacts/verification/rename-20261022-163939/`：

| 验证 | 结果 | 证据文件 |
| --- | --- | --- |
| Release 构建、默认 Core 测试、启动 | 0 警告、0 错误；38/38 | `unpackaged-build-test-launch.log` |
| Debug / Any CPU 构建 | GUI 实际为 x64；0 警告、0 错误 | `debug-anycpu-build.log` |
| 现有输出启动脚本 | 成功启动 unpackaged GUI | `unpackaged-start-final.log` |
| GUI 主流程 | 10/10 | `ui-final/ui-results.json` |
| 原生打开、另存为、架构选择 | 4/4 | `pickers-final/picker-results.json` |
| 项目有效属性与资源项 | None、x64、自包含；artifacts 未进入项目项 | `project-evaluation.json` |

GUI 验证使用独立配置副本，覆盖加载、静态分析、编辑保存、重载、未保存提示、无效版本阻止构建及支持边界；最终 7 张截图均已审阅，当前浅色主题、100% DPI 下未发现本次改动引起的显示问题。早期受桌面输入干扰的批次只作为诊断保留，以 `ui-final` 和 `pickers-final` 为本次结果。

本次没有重新打包或安装 Trader；已有最终 Setup 的 SHA256 仍为 `C5E422AA218BBA89BD445CD1E526696EECFD9B090674193EDF53AE4841060276`。仓库示例配置原文件未变，参考客户端仓库未修改。原有部署和更新验收边界仍适用。

## 2026-10-22 模板资源清理复验

删除当前 unpackaged 项目不使用的 `Package.appxmanifest` 和 8 个 MSIX 默认 PNG，并移除本机被 Git 忽略的 ARM64/x86 发布 Profile。保留的 `AppIcon.ico`、`app.manifest`、启动配置、两套语言资源和 x64 Profile 的 SHA256 均未改变。

运行 `dotnet clean` 后重新执行 Release / x64 构建，结果为 0 警告、0 错误，默认 Core 测试 38/38 通过。首次启动检查发现仅指定 `--arch x64` 仍可能让 WinApp 从旧的 `bin/Release` 输出启动；现已在两个开发启动脚本中明确加入 `-p Platform=x64`。此前的启动记录保留为历史结果，不能据此证明启动的是重构后的 x64 输出。

修正后已核对实际进程路径位于 `bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/`。资源启动检查 3/3 通过：无包清单也可打开 unpackaged 主窗口，中文 PRI 资源正常，新输出的 `Assets/` 仅包含与源文件 SHA256 相同的 `AppIcon.ico`。最终截图已审阅。本次为资源清理验证，没有重复真实打包或完整 GUI 编辑测试。

本机证据目录为 `Silmoon.Tools.Installer.Tests/artifacts/verification/template-cleanup-20261022-085926Z/`，包含 `file-inventory.json`、`clean.log`、`build-test-launch.log`、`start-final.log`、`ui-smoke-final.json` 和 `startup-final.png`；前期启动路径诊断保留在同目录。既有 Setup、演示配置和历史验证产物未改动。

## 2026-10-22 Core 与 Tests 目录调整复验

Core 和测试项目分别使用 `Silmoon.Tools.Installer.Core/`、`Silmoon.Tools.Installer.Tests/` 及同名命名空间。WinUI 主项目仍为 `Silmoon.Tools.Installer.WindowsMaker/`，三个项目在磁盘上保持同级；解决方案中的 `Testing` 仅是虚拟分组。

修正了测试报告定位使用的 csproj 文件名，以及 `Build.ps1` 调用的测试项目路径。从仓库根目录执行 `Build.ps1 -Configuration Release`，Release / x64 解决方案构建为 0 警告、0 错误，默认 Core 测试 38/38 通过，脚本退出码为 0。测试报告成功写入新测试目录，未再发生报告定位异常。

本机证据目录为 `Silmoon.Tools.Installer.Tests/artifacts/verification/core-tests-rename-20261022/`，包含 `build-test.log` 和 `build-test-status.json`；对应测试报告为 `Silmoon.Tools.Installer.Tests/artifacts/tests/20261022-094842-ff981aa0b960453088053c608567e2e3/core-results.json`。上述产物均被 Git 忽略。

README、AGENTS 和本文的当前路径及复现命令已同步；历史验证文件的引用按其当前位置更新，原始日志内容保持不变。本次没有修改 UI，也没有重新执行 GUI、Trader 打包或安装验证，先前结果仍只代表对应批次。

## 产物归档位置

已有本机输出按以下映射归入所属项目，根目录不再保留 `artifacts/`：

| 历史位置 | 当前位置 |
| --- | --- |
| 根目录 `artifacts/trader/` | `Silmoon.Tools.Installer.WindowsMaker/artifacts/trader/` |
| 根目录 `artifacts/` 中的其余目录与文件 | `Silmoon.Tools.Installer.Tests/artifacts/`，保留 `tests/`、`verification/`、`ui-*` 等原有子目录结构 |

既有 Setup、依赖安装器、更新包、日志、JSON 和截图保留原始内容；因此历史记录中的旧绝对路径不会被改写。定位这些历史文件时使用上表映射，不能直接使用日志中旧位置。新的构建和验证使用本文的当前路径。

本机迁移完整性报告位于 `Silmoon.Tools.Installer.Tests/artifacts/verification/artifact-relocation.json`，记录逐文件的原位置、新位置及迁移前后 SHA256，核验结论以报告内容为准。报告和原始证据均不随源码提交。已有 `Silmoon.Tools.Installer.Tests/artifacts/tests/core-results.json` 保持原样。新 Core 测试报告写入 `Silmoon.Tools.Installer.Tests/artifacts/tests/<UTC时间>-<随机ID>/core-results.json`；测试通过程序集位置向上定位测试项目，输出不依赖启动工作目录。默认 UI 输出目录为 `Silmoon.Tools.Installer.Tests/artifacts/ui/`。

## 同级项目布局复验

产物目录迁移完成后，56 个原有文件（共 875806724 字节）的大小与 SHA256 全部一致，两份 Setup.exe 及全部历史验证记录均保留。根目录 `artifacts/` 已不存在。迁移后 Release 构建为 0 警告、0 错误；从主项目目录启动 Core 测试仍为 47/47，并将新报告写入测试项目的独立时间戳目录，没有覆盖旧报告。两项目的 MSBuild 默认项均不包含 `artifacts/` 文件，Git 忽略规则也已验证。对应新日志为 `Silmoon.Tools.Installer.Tests/artifacts/verification/artifact-layout-build.log` 和 `Silmoon.Tools.Installer.Tests/artifacts/verification/artifact-layout-tests.log`。

将三个 C# 项目整理到根目录同一级，并同步示例、脚本及项目引用路径后，重新构建 `Silmoon.Tools.Installer.slnx`：Release 为 0 警告、0 错误，Core 测试 47/47 通过。对应本机证据为 `Silmoon.Tools.Installer.Tests/artifacts/verification/flat-layout-build.log` 与 `Silmoon.Tools.Installer.Tests/artifacts/verification/flat-layout-tests.log`。以下 GUI 和真实打包记录仍是首版功能验证，不能将目录迁移后的编译结果当作重新完成全部部署验收。

## 实际 GUI 演示链路

迁移后已通过 WinApp CLI 从新的项目路径启动 GUI，成功加载移动后的 Trader 配置并完成发布目录分析；截图 `Silmoon.Tools.Installer.Tests/artifacts/verification/flat-layout-gui.png` 已检查。该检查验证新位置的启动和路径解析，没有重复执行客户端安装或打包。

通过原生 WinUI 窗口加载 Silmoon Trader 安装项目配置，分析已有 publish，将安装包版本从 `0.1.0-preview.1` 编辑为 `0.1.0-preview.2`，保存、新建后重新加载，再点击构建。GUI 调用 Core 和真实 `vpk pack`，退出码为 0，产物验证完成后才展示成功。示例目前位于 `Silmoon.Tools.Installer.WindowsMaker/Examples/Silmoon.Trader.installer.json`。

本次复用已有客户端 publish，没有重新发布或修改客户端源码。`preview.2` 是安装项目编辑和重复打包演示，不声称客户端业务二进制发生变化。安装身份仍为 `Silmoon.Trading.Client.WindowsClient` / `win-x64-preview` / x64。

本机成功构建目录：

```text
Silmoon.Tools.Installer.WindowsMaker/artifacts/trader/Silmoon.Trading.Client.WindowsClient-0.1.0-preview.2-win-x64-preview-20261019-094725-c1fd9fe3/
```

| 产物 | 字节数 | SHA256 |
| --- | ---: | --- |
| `Silmoon.Trading.Client.WindowsClient-win-x64-preview-Setup.exe` | 115099009（109.77 MiB） | `C5E422AA218BBA89BD445CD1E526696EECFD9B090674193EDF53AE4841060276` |
| `Silmoon.Trading.Client.WindowsClient-0.1.0-preview.2-win-x64-preview-full.nupkg` | 107584897 | `3B0DE89D1795D14CDAEB7E33F458787E63AD38A673517EEF816F342079CF5A22` |
| `prerequisites/webview2-evergreen-x64/MicrosoftEdgeWebView2RuntimeInstallerX64.exe` | 214156024 | `0625FB5C1E75BE58A164E14F95AA58A3E5BADB859B3D17C952385F3E4A9FF3E8` |

同目录包含渠道清单、构建日志、构建清单、依赖清单和分发说明。Setup 未签名。WebView2 是单独随附的完整离线安装器，没有内嵌到 Setup，也没有自动串联执行；缺失时须先手动安装。其文件版本 `1.3.287.9` 是安装器版本，不是浏览器 Runtime 版本。

逐文件验证最初发现 `createdump.exe` 缺失，经核对固定版本源码确认属于默认规则，随后脚本按准确规则记录排除原因。6 个 PDB 和 1 个 createdump 被排除，其余 590 个文件全部匹配。规则来源为 [PackageBuilder 的内置排除](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/vpk/Velopack.Packaging/PackageBuilder.cs#L33)、[PackCommand 的默认 PDB 排除](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/vpk/Velopack.Vpk/Commands/Packaging/PackCommand.cs#L102-L105)及 [Windows 复制阶段启用排除](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/vpk/Velopack.Packaging.Windows/Commands/WindowsPackCommandRunner.cs#L54-L56)。独立校验脚本将此规则限定为已审阅的 vpk 1.2.161。

## 自动化和错误路径

Core 验证涵盖 Unicode 配置往返、所有路径的另存为重定位、原子保存取消、未来/损坏/空值/未知字段/重复字段配置、非法枚举、未支持的能力、缺失文件、输出位于输入目录等参数错误。

进程验证覆盖独立参数传递、特殊字符不作为 shell 执行、非零退出码、取消当前子进程树且无关哨兵进程继续运行。引擎验证覆盖 vpk 不匹配而不自动升级、模拟退出 42、模拟退出 0 但缺失产物仍失败、离线依赖哈希错误、预先取消、运行时系列/架构不匹配和 SDK 不能替代 Runtime。真实演示校验保留 VeloAppCheck；没有绕过生命周期检查。

GUI 覆盖加载、分析、编辑保存、新建后重载、未保存提示取消、依赖/分发边界展示、非法版本阻止构建并清空旧产物、真实成功打包。补充批次通过 Windows 原生 Open/Save As，验证相对路径保持原目标；真实选择 x86 后保存，再选回 x64 并保存。

首次界面检查发现枚举选择框内容为空，已改为字符串列表和强类型索引绑定。首次自动化对关闭按钮的选择也过宽，后来改用对话框 `CloseButton` 并断言对话框消失。早期 `ui-run-1` 只用于诊断，不作为最终 UI 验收证据。最终批次 9 张截图均已人工审阅；中文深色界面在当前 100% DPI 窗口下可用，长日志和页面正常滚动。

## WinUI 代码审查

- ViewModel 使用 ObservableObject、partial ObservableProperty 和 RelayCommand；Core 不引用 WinUI。代码后置负责导航、窗口关闭和交互协调。
- 动态绑定采用 OneWay/TwoWay，编辑框使用 PropertyChanged 更新；六个选择器的显示与枚举保存已验证。
- 界面采用原生控件、内置文字样式和 ThemeResource。自行定义的交互控件有稳定 AutomationId，浏览按钮有可访问名称。
- 分析和打包异步执行，进度回到 UI 线程；无同步 Wait/Result 阻塞。忙碌期间禁止修改配置，失败或取消清除成功产物状态。
- 未发现阻止本版交付的审查问题。依赖高级参数仍通过 JSON 编辑；初版为中文界面，尚未完整翻译其他语言。

## 复现

在仓库根目录构建并运行默认 Core 测试，不需要 Trader 的发布产物：

```powershell
dotnet build ./Silmoon.Tools.Installer.slnx -c Release -p:Platform=x64
dotnet run --project ./Silmoon.Tools.Installer.Tests -c Release
```

完整演示需要自行准备目标应用 publish 和离线依赖，并先修改示例的路径。以下命令中的进程 ID 和成功构建目录也需要替换：

```powershell
pwsh -NoProfile -File ./Silmoon.Tools.Installer.WindowsMaker/Scripts/Build.ps1 -Configuration Release -Run
dotnet run --project ./Silmoon.Tools.Installer.Tests -c Release -- --demo ./Silmoon.Tools.Installer.WindowsMaker/Examples/Silmoon.Trader.installer.json
pwsh -NoProfile -File ./Silmoon.Tools.Installer.Tests/Scripts/Test-UI.ps1 -AppPid <新工具进程ID> -ProjectFile ./Silmoon.Tools.Installer.WindowsMaker/Examples/Silmoon.Trader.installer.json -ArtifactDirectory ./Silmoon.Tools.Installer.Tests/artifacts/ui-recheck -BuildDemo
pwsh -NoProfile -File ./Silmoon.Tools.Installer.Tests/Scripts/Test-NativePickers.ps1 -AppPid <新工具进程ID> -ProjectFile ./Silmoon.Tools.Installer.WindowsMaker/Examples/Silmoon.Trader.installer.json -ArtifactDirectory ./Silmoon.Tools.Installer.Tests/artifacts/picker-recheck
pwsh -NoProfile -File ./Silmoon.Tools.Installer.Tests/Scripts/Verify-DemoArtifact.ps1 -ProjectFile ./Silmoon.Tools.Installer.WindowsMaker/Examples/Silmoon.Trader.installer.json -BuildDirectory '<成功构建目录>'
```

UI 测试会操作新工具并编辑示例版本/架构，结束时保存版本 `0.1.0-preview.2` 和 x64。使用新的截图/另存为目录，避免原生文件覆盖提示。只针对新工具进程运行，不能传入交易客户端进程。

2026-10-19 首版验证使用 WinApp CLI 的开发注册启动 GUI，没有导入证书信任。Windows Sandbox 未启用，因此当时的 UI 测试在开发机进行。当前项目已改为 unpackaged，使用 `Silmoon.Tools.Installer.WindowsMaker/Scripts/Start-WindowsMaker.ps1` 启动；当前验证见上文 2026-10-22 记录。

## 未验收和保护边界

验证只读取目标应用的既有发布产物，没有修改参考应用、启动或安装目标客户端，也没有执行卸载、升级或公网交易。没有读取或修改个人数据，没有上传产物、导入证书信任或修改系统安全策略。

干净 Windows、标准用户安装、完整卸载、v1→v2 数据保留及更新失败恢复尚未验收。Narrator、RTL、高对比、多显示器和不同 DPI 的完整矩阵也尚未验收。真实构建、静态分析及生命周期检查不能替代这些部署和更新测试。

在线入口、差分生成、机器范围安装和签名执行在第一版明确阻止；更新 URL 仅为分发元数据。工具不会默认添加后台升级、开机自启或自动删除数据的卸载逻辑。
