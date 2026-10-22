# 第三方来源与许可证

核对日期：2026-10-22。本文根据当前项目声明、本机还原的指定版本 NuGet 包（`.nuspec`、许可证及通知文件）和官方源文件整理。本仓库的 MIT 许可证适用于本仓库自行编写的内容，不改变第三方组件的许可证、版权或商标归属。

本清单记录直接依赖、默认图标来源和外部打包工具，不是所有传递依赖的完整清单，也不表示未来发布的二进制或安装包已经完成全部许可核查。

## 直接 NuGet 依赖

| 组件与版本 | 归属 | 许可证与本项目用途 |
| --- | --- | --- |
| CommunityToolkit.Mvvm 8.4.2 | .NET Foundation and Contributors；NuGet 作者为 Microsoft | MIT；用于 MVVM。包内随附 `License.md` 和 `ThirdPartyNotices.txt`。 |
| Microsoft.WindowsAppSDK 2.5.1 | Microsoft Corporation | **Microsoft Software License Terms — Microsoft Windows App SDK**；用于 WinUI 和 Windows App SDK。此 NuGet 包指定 `license.txt`，不能将其二进制包整体标为 MIT。包内另有 `NOTICE.txt`。 |
| Microsoft.Windows.SDK.BuildTools.WinUIAnalyzer 0.7.1 | Microsoft Corporation and Contributors | MIT；构建期分析器，项目设置 `PrivateAssets="all"`，不是应用的运行依赖。 |

对应官方资料：

- CommunityToolkit.Mvvm：[8.4.2 包](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2)、[对应提交许可证](https://github.com/CommunityToolkit/dotnet/blob/35dfe7b12da0e64384bfaec7ebc0883196a89f9f/License.md)、[对应提交第三方通知](https://github.com/CommunityToolkit/dotnet/blob/35dfe7b12da0e64384bfaec7ebc0883196a89f9f/ThirdPartyNotices.txt)。
- Windows App SDK：[2.5.1 包及其依赖](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1)、[该版本完整许可证](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1/License)。公开源代码仓库的许可证不能代替此 NuGet 包随附的二进制条款。
- WinUIAnalyzer：[0.7.1 包](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.WinUIAnalyzer/0.7.1)、[对应提交许可证](https://github.com/microsoft/WinAppCli/blob/e3cd0b94f3e7b776ac51ba0564f4908094e979b5/LICENSE)。

## 默认图标与模板来源

`Silmoon.Tools.Installer.WindowsMaker/Assets/AppIcon.ico` 来自 Microsoft 官方 `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` **0.0.7-alpha** 的 WinUI MVVM 项目模板。该模板包声明 MIT，版权归 Microsoft Corporation。默认图标不是本仓库原创设计，也不表示本工具获得 Microsoft 背书。

- [模板 NuGet 包](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI.CSharp.Templates/0.0.7-alpha)
- [对应提交许可证](https://github.com/microsoft/WindowsAppSDK/blob/164ad9681a9133f056ff38a43609e6227121e91a/LICENSE)
- [对应提交原始图标](https://github.com/microsoft/WindowsAppSDK/blob/164ad9681a9133f056ff38a43609e6227121e91a/dev/Templates/Source/ProjectTemplates/Desktop/CSharp/MvvmApp/Assets/AppIcon.ico)

来源核对：文件大小 **370070 字节**，SHA256 为 `B622196BADECED33CC37B6FE166979395A1AF41D6C421326BE5F2671CE38260A`，与上述模板文件逐字节一致。Git blob 为 `235dad2c569272e9a3c7158ce4f93042044ea5d1`。对应 Microsoft 版权声明与 MIT 全文保留在本文下方。

根目录 `.gitignore` 基于 [github/gitignore 的 VisualStudio.gitignore](https://github.com/github/gitignore/blob/main/VisualStudio.gitignore)，其项目采用 [CC0 1.0](https://github.com/github/gitignore/blob/main/LICENSE)。

## 外部打包工具

当前适配的 **vpk / Velopack 1.2.161** 主体采用 MIT。两个包的 NuGet 元数据均如此声明，对应提交的许可证与之相符。NuGet 作者列出 Velopack Ltd、Caelan Sayler 和 Kevin Bost；对应许可证保留 Caelan Sayler 与 Velopack Ltd 的版权声明。

- [vpk 1.2.161](https://www.nuget.org/packages/vpk/1.2.161)
- [Velopack 1.2.161](https://www.nuget.org/packages/Velopack/1.2.161)
- [对应提交许可证](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/LICENSE)
- [对应提交 vendor 第三方通知](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/vendor/THIRD_PARTY_NOTICES.md)

本工具通过子进程调用用户自行安装的 `vpk`，源码仓库不附带该工具二进制。`Velopack` 是目标应用需要接入的生命周期库，不是当前 WindowsMaker 主项目的直接 NuGet 引用。

vpk 主体的 MIT 不覆盖所有随工具附带的 vendor 内容。上游通知另列 Windows SDK 工具、zstd、AppImage runtime、backhand 和 WiX fork 等各自的许可证；分发这些内容时应按对应版本和实际包含的组件处理。这里的结论限定为 **1.2.161**，升级版本时重新核对。

## 分发边界

- 发布源码与发布自包含应用、生成的 Setup 或离线依赖是不同的交付范围。本仓库不提交目标客户端二进制、运行时、已生成安装器或本机产物。
- 分发 MIT 组件或其重要部分时，保留其版权声明与许可正文；CommunityToolkit 自带的第三方通知也应按实际分发内容保留。
- Windows App SDK 的二进制条款第 3 节规定可随应用分发的文件及相应条件，包括自包含部署。分发时应保留相关许可证和 `NOTICE.txt`，遵守其分发要求，不能以本仓库 MIT 替代这些条款。
- 真正发布 WindowsMaker 或目标应用安装包前，按最终输出检查实际携带的 .NET、Windows App SDK、Velopack 组件、目标应用依赖及独立 WebView2 安装器的许可与通知。本文不替代这些组件随附的原始条款，也不作完整合规保证。

## MIT 组件的版权声明与许可全文

下列版权声明分别对应前文注明的组件，共用 MIT 正文不表示它们归同一权利人：

- Microsoft WinUI 项目模板及默认图标：Copyright © Microsoft Corporation. All rights reserved.
- Microsoft WinUIAnalyzer：Copyright (c) Microsoft Corporation and Contributors.
- .NET Community Toolkit：Copyright © .NET Foundation and Contributors. All rights reserved.
- Velopack：Copyright © 2021 Caelan Sayler；Copyright © 2024 Velopack Ltd.

```text
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
