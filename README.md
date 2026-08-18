# Windows 悬浮时钟

轻量的 Windows 原生悬浮时钟。无边框、常驻托盘、置顶，并随当前用户开机启动。

时钟本体是 CLI 风格单色界面，使用 Consolas 等宽数字：年份在左，时间居中，月份和日期在右侧上下排列。默认荧光绿色。右键与托盘菜单均为中文。

## 使用

- 左键拖动时钟；右键打开全部设置。默认开机自启，并停在当前屏幕工作区左下角（任务栏上方）。
- 托盘图标可显示/隐藏时钟，并包含与窗口右键相同的设置（穿透模式下也能改选项）。
- 可切换日期、数字秒钟、24 小时制、数字颜色、背景颜色、字体、六档大小和背景浓度。深色背景使用真实逐像素透明，透明度由设置固定且不随鼠标移动改变，数字不透明。显示和鼠标交互共用一个原生分层窗口，避免重叠窗口重复合成；浅色背景仍是不透明实色。
- 字体：赛博、锐线、终端、几何、仪表、霓虹、冰岛、电刻。数字颜色和背景颜色各有十余种。大小有迷你、小、标准、大号、很大、超大。
- 右键/托盘菜单使用固定深色样式，不会跟着时钟配色变。
- 「锁定位置」防止误拖动；「鼠标穿透」允许点击下方窗口。按 `Ctrl+Alt+T` 或单击托盘图标可关闭穿透。热键被占用时会提示，并改用托盘菜单。
- 「复位到右上角」回到**当前显示器**工作区右上角，而不是强制回到主屏。
- 关掉日期后两侧年份/日期列会收起，窗口变窄。年份和月/日数字比时间略小一档，但仍保持清晰可读。
- 打开秒钟时 `HH:mm` 保持大号，秒钟以较小字号跟在后面。12 小时制使用 `AM` / `PM`。
- 点击或拖动时钟默认不抢焦点，方便叠在正在输入的窗口上。
- 设置保存在 `%LOCALAPPDATA%\FloatingClock\settings.xml`。升级版本时会保留你已选的主题、大小、秒钟等选项。

## Chrome 硬件合成兼容性

部分 Windows、AMD 显示驱动和 Chrome 组合在 DirectComposition/MPO 路径下，会出现只在物理屏幕可见、截图无法捕获的透明窗口亮度闪烁。此时需完全退出 Chrome（包括后台进程），并使用以下参数重新启动：

```text
--disable-direct-composition
```

可将该参数追加到 Chrome 快捷方式的“目标”末尾。它只切换 Chrome 的显示合成路径，不会把 Floating Clock 的背景改成不透明。

## 重新编译和安装

在 Windows PowerShell 中运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

也可以用 Visual Studio / `dotnet build` 打开 `FloatingClock.csproj`（目标框架 .NET Framework 4.8）。日常安装仍建议走 `build.ps1 -Install`。

## 卸载

默认保留设置：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

同时删除设置：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -RemoveSettings
```
