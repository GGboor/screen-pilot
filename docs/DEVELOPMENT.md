# 开发与验证

## 技术栈

本项目使用 C#、WinForms 和 .NET Framework 4.x，通过 P/Invoke 调用 Windows CCD 显示配置接口。没有第三方运行时包和 NuGet 依赖，目前提供 x64 构建。

`build.ps1` 使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，先构建图标，再编译主程序和测试程序。项目采用源码列表构建，没有 `.sln` 或 `.csproj` 文件。

## 显示切换流程

1. 使用 `QueryDisplayConfig` 读取已连接和正在启用的显示路径。
2. 按设备路径识别屏幕，结合已保存模式与布局计算目标桌面，并关闭空隙。
3. 通过 `SDC_VALIDATE` 验证目标配置。
4. 创建恢复快照并启动独立恢复进程，然后提交显示配置。
5. 核验实际启用集合、位置、分辨率、旋转和刷新率。
6. 用户确认后保存；未确认或失败时尝试恢复仍然可用的屏幕。

目标设备 ID 可能在重连后改变。恢复快照同时保存设备路径，重新计划时把已知屏幕绑定到当前路径。新接入且已启用的桌面不会因恢复旧快照而被全部关闭。

屏幕拼接只改变本次提交的布局；完整布局记忆用于以后重新启用屏幕。程序对设备通知做延迟处理，并定期读取完整设备列表，以发现未启用的新屏幕。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\ScreenPilot.Tests.exe
.\ScreenPilot.Tests.exe --inspect
```

默认测试覆盖路径分配、全部关闭拒绝、布局拼接、恢复竞争、多屏数量变化、稳定编号、设备重绑定、屏幕类型和排列图比例等。

`--inspect` 额外读取真实设备并调用配置验证接口。当前屏幕数量不超过三块时，会验证全部非空屏幕组合；它不会提交这些组合。该模式还会渲染主窗口并移动测试专用的离屏窗口，生成文件位于 `test-output/`。

测试程序会启动随后取消的恢复子进程，在正常本机数据目录留下测试恢复记录。界面检查会使用正常布局记忆路径；如不希望接触个人布局文件，请使用独立 Windows 测试用户运行 `--inspect`。

硬件拔插、远程连接和驱动实际执行结果仍需人工验证。测试数量会随当前可用设备而变化，不应把固定检查数量作为唯一判断依据。

## 调试入口

```powershell
.\屏幕管家.exe --list "$env:TEMP\screen-list.txt"
```

导出字段包括名称、是否启用、GDI 名称、模式说明、设备路径、类型、位置及适配器名称。`--guard` 是内部恢复进程入口，不作为用户操作命令。

## 发布

1. 修改 `AssemblyInfo.cs` 中的版本号，同步 README 和更新记录。
2. 构建并运行对应测试，检查正常窗口、最小窗口和确认面板。
3. 退出正在运行的主程序后替换根目录程序，保留可用的恢复进程直到其任务结束。
4. 更新便携包，包含程序、源码、图标、预览图和公共文档。
5. 核对压缩包中的程序与根目录程序哈希一致。

个人布局、Sunshine 配置、凭据、恢复目录和机器故障记录不随工程发布。`.gitignore` 排除了测试生成物及本机诊断文件；打包也应使用明确的文件清单，避免直接压缩整个工作目录。

## 数据目录与完整性级别

布局记忆（`%LOCALAPPDATA%\ScreenPilot\layout.xml`）和恢复记录（`…\Recovery\`）都是辅助数据，不是显示切换的前置条件：

- `LayoutMemory.TrySave()` 捕获写入失败并记录到 `LastSaveError`，界面继续显示屏幕列表，状态栏追加原因；`Keep()` 只在真正需要提示时追加同一段说明。
- 恢复记录写不进去时 `BeginChange` 主动取消切换，避免出现无法回滚的改屏。
- 程序所在目录若带 Windows 低完整性标记，Windows 会以低完整性启动其中的 exe，写用户配置目录会被拒绝。确认方式：`icacls 屏幕管家.exe` 中是否出现 `Mandatory Label\Low Mandatory Level`；修复方式：`icacls 屏幕管家.exe /setintegritylevel Medium`，或把程序复制到普通目录运行。
- 在该类目录中重新构建时，新生成的 exe 会继承目录的低完整性标记，需要再次执行上面的 `icacls` 命令；提交到 git 后检出的副本同样会继承目录标记。

## 主要接口参考

- [QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig)
- [SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)
- [显示输出类型](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_video_output_technology)
