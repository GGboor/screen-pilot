# 屏幕管家

Windows 多屏管理工具，适合 Sunshine + Moonlight 串流和日常多屏切换。可以独立开启或停用显示输出、设置主屏幕，并查看屏幕类型与当前排列。

当前程序版本：**1.7.1.0**。界面采用圆角卡片、滑动开关和托盘菜单。

[下载便携包](./屏幕管家-便携版.zip?raw=true) · [下载程序](./屏幕管家.exe?raw=true) · [完整使用说明](./使用说明.txt) · [Sunshine 串流指南](./docs/SUNSHINE.md)

![屏幕管家界面](./界面预览.png)

## 功能

- 独立开启、关闭各个屏幕，支持只开一屏、全部开启、设置主屏和恢复启动时配置。
- 停用 Windows 显示输出，关闭的屏幕不再参与桌面；程序列表保留其入口，方便再次开启。
- 显示实体、内置、虚拟、无线等屏幕类型，以及 HDMI、DisplayPort 等连接方式；支持识别 Zako 虚拟屏幕。
- 按实际桌面像素比例展示屏幕排列、编号和主屏，卡片显示左上角坐标。
- 记忆完整布局；关闭中间屏幕后拼接剩余屏幕，重新开启时恢复记住的排列。
- 自动检测新增、拔出和重新连接的屏幕，不限定为三屏；按设备标识保存编号。
- 切换后提供 30 秒确认倒计时，由独立进程负责超时恢复。
- 确认后找回离屏窗口，也支持手动找回窗口。
- 支持全局快捷键和系统托盘操作。

## 运行

需要 Windows 10/11 64 位及 .NET Framework 4.x。无需额外安装 NuGet 包。

1. 下载并解压便携包，运行 `屏幕管家.exe`。串流场景应在安装 Sunshine 的被控电脑上运行。
2. 首次使用时，建议先在 Windows 显示设置中开启所有需要记忆的屏幕并排好位置，然后打开程序。
3. 调整卡片开关，点击“应用所选屏幕”；也可以直接使用“只开此屏”或“设为主屏”。
4. 画面正常时，在 30 秒内点击“画面正常，保留”；否则等待自动恢复或点击“立即恢复”。
5. 关闭窗口后程序进入托盘。右键托盘图标可以切换屏幕或退出。

“串流保护”可手动指定不允许关闭的屏幕。它不会自动识别 Sunshine 当前捕获对象。如果希望平时关闭串流屏幕、下次连接时自动启用，请先阅读 [Sunshine 重连设置](./docs/SUNSHINE.md)。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Ctrl + Alt + 1～9` | 开启 / 关闭对应编号的屏幕 |
| `Ctrl + Alt + Shift + 1～9` | 将对应屏幕设为主屏；未开启时会同时开启 |
| `Ctrl + Alt + Shift + 0` | 打开屏幕管家面板 |

编号以本工具显示为准，与 Windows 的显示器编号不保证相同。拔出的屏幕保留原编号；超过 9 号的屏幕可以通过面板或托盘操作。快捷键被其他程序或 Moonlight 拦截时，可使用按钮。

## 布局和屏幕类型

排列图展示当前 Windows 桌面，蓝色为主屏幕，关闭的屏幕不占图中位置。负坐标表示屏幕位于主屏左侧或上方。图中的比例根据像素尺寸计算，不代表显示器实际物理尺寸。

布局记忆会保留分辨率、刷新率和完整排列。关闭主屏时需要将另一屏变为坐标原点；关闭中间屏幕时，程序会消除剩余排列中的空隙。图示目前仅用于查看，手动拖动调整位置请使用 Windows 显示设置。

设备类型由 Windows 接口和驱动信息判断。不确定的间接显示会标注“USB / 虚拟待确认”。HDMI/DP 显示模拟器可能与实体屏幕报告相同的连接类型，程序无法仅凭接口区分。

## 编译与测试

在 Windows PowerShell 中进入工程目录，先退出正在运行的屏幕管家，再执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\ScreenPilot.Tests.exe
```

构建脚本使用 Windows 自带的 .NET Framework C# 编译器，生成程序、图标和测试程序。也可以保持当前版本运行，将新程序编译到其他目录：

```powershell
New-Item -ItemType Directory -Path _release -Force
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputName '_release\屏幕管家.exe'
```

在本机验证 Windows 显示配置和界面：

```powershell
.\ScreenPilot.Tests.exe --inspect
```

`--inspect` 不切换真实屏幕；它会读取设备、验证候选配置、渲染界面，并创建及移回一个测试窗口。它也会读取并保存程序正常的布局记忆。测试通过不代表已经完成所有硬件插拔及 Moonlight 端到端验证。

更多实现说明见 [开发文档](./docs/DEVELOPMENT.md)。

## 数据与诊断

个人布局和恢复记录存放在以下目录，不写入工程仓库：

```text
%LOCALAPPDATA%\ScreenPilot\layout.xml
%LOCALAPPDATA%\ScreenPilot\Recovery\
```

只读导出当前屏幕列表：

```powershell
.\屏幕管家.exe --list "$env:TEMP\screen-list.txt"
```

如果程序所在目录带 Windows 的“低完整性”标记（沙箱或受管目录），Windows 会以低完整性启动它，写入上面两个目录会被拒绝。1.7.1 起这种情况不再中断界面：屏幕列表、排列图和开关照常显示，状态栏说明原因；把程序放到普通文件夹（例如 `C:\Tools\屏幕管家`）或对该文件执行 `icacls "屏幕管家.exe" /setintegritylevel Medium` 后即可恢复布局记忆。恢复记录写不进去时，程序会取消本次切换，避免改了屏幕却无法自动恢复。

反馈问题时，请附上 Windows 版本、显卡与驱动、屏幕类型、具体操作步骤，以及相关报错。报告中如含本机路径或设备标识，可先删去无关信息。

## 使用范围

- “关闭”是从 Windows 桌面中停用显示输出，不是遥控显示器电源，也不会卸载显卡或虚拟显示驱动。
- 至少保留一块开启的屏幕；可同时启用的数量受显卡和驱动限制。
- 程序不创建虚拟屏幕，也不自动修改 Sunshine 配置；串流前启用屏幕需要在 Sunshine 中配置。
- 驱动拒绝记录的模式、所有屏幕不可用或系统退出时，恢复可能失败。
- 更换接口或重新安装驱动后，Windows 若分配了新设备标识，会作为新屏幕加入。

## 项目文件

| 文件 | 用途 |
| --- | --- |
| `ScreenPilot.cs` | 主窗口、托盘、快捷键与切换流程 |
| `NativeDisplay.cs` | Windows 显示配置接口与设备识别 |
| `LayoutMemory.cs` | 布局记忆、设备编号和屏幕拼接 |
| `MonitorLayoutView.cs` | 屏幕排列图 |
| `Recovery.cs` | 独立恢复进程与热插拔后的恢复计划 |
| `WindowRecovery.cs` | 离屏窗口找回 |
| `RoundedControls.cs` | 圆角控件与开关 |
| `BuildIcon.cs` / `AppIcon.ico` | 图标生成源码与应用图标 |
| `Tests.cs` | 模拟测试与本机只读检查 |
| `build.ps1` | 构建脚本 |

版本记录见 [CHANGELOG.md](./CHANGELOG.md)。
