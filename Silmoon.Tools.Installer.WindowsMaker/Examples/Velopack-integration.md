# 目标应用的一次性 Velopack 接入

本工具要求目标 publish 保留对应的 deps.json 和匹配的 Velopack 包版本。当前实现严格检查 `vpk`、项目配置和目标 NuGet 版本一致；不会自动升级依赖或跳过 `VeloAppCheck`。

对于当前 .NET / WinUI 3 基线，在应用项目中引用 `Velopack` 1.2.161，并保留现有常量，追加 `DISABLE_XAML_GENERATED_MAIN`。新增自己的入口源文件，不要修改 obj 中生成的代码：

```csharp
using Velopack;

namespace YourApplication;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false).Run();
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
```

`XamlGeneratedProgram` 属于当前 Windows App SDK 生成器契约，升级后需核实。保留 STA、COM wrappers、Application.Start 与 UI 同步上下文，重新 Release publish 后再打包。本工具只提供指引，不会修改用户业务代码。

`Velopack.dll` 或 deps 引用仅是静态证据，不能证明入口已正确调用 Run；最终仍由打包器检查生命周期。安装生命周期接入不等于自动更新已实现。更新源、检查/下载 UI、安全退出、交易状态协调与更新失败恢复需要应用自己实现。

参考：[Velopack 集成文档](https://docs.velopack.io/integrating/overview)。
