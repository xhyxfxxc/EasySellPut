# 06 macOS 版设计（macOS 26 Tahoe 及以上，免安装便携版）

> 编写：2026-11-12。资料均在本日查证，来源见文末编号。没有 Mac 实机，凡是“只能在 Mac 上确认”的内容在第 8 节单独列出。

## 1. 结论摘要

| 决定 | 内容 | 依据 |
|---|---|---|
| 界面框架 | **Avalonia 12.1.6**（当前稳定版，2026-11-09 发布）+ **ScottPlot.Avalonia 5.1.59**（依赖 Avalonia ≥ 12.0.0，与 Windows 版 ScottPlot.WPF 同版本号，画图代码可共用） | Avalonia 官方把 macOS 26 Tahoe 列为 Tier 1（arm64 与 x64 都在测试矩阵内）[1][2][3] |
| 目标框架 | `net10.0`（不用 `net10.0-macos` 工作负载） | Avalonia 自带原生后端 `libAvaloniaNative.dylib`，可以在 Linux/Windows 上交叉编译；用 `net10.0-macos` 就必须在 Mac 上构建 [4] |
| 架构 | **主包 osx-arm64**，另附 **osx-x64** 包给仍在 macOS 26 上的 4 款 Intel Mac | macOS 26 是最后一个支持 Intel 的版本，只剩 2019 款 16 寸 MacBook Pro、2020 款四口 13 寸 MacBook Pro、2020 款 iMac、2019 款 Mac Pro；2026-09 发布的下一版只支持 Apple 芯片 [5][6][7]。.NET 自包含发布按 RID 各出一份，做“通用二进制”需要把两套运行时 lipo 合并，收益小、风险大，所以分两个 zip |
| 最低系统 | `LSMinimumSystemVersion = 26.0` | 用户要求；也与 Avalonia Tier 1 一致 |
| 签名 | **ad-hoc 签名**（Linux 上用 rcodesign 0.29.0），不公证 | Apple 芯片上的 arm64 代码必须有签名才能运行，ad-hoc 可以满足；没有 Developer ID，无法公证 [8][9] |
| 渲染 | Avalonia 默认顺序 **Metal → OpenGL → Software**，保持默认 | Avalonia 12 文档：`AvaloniaNativePlatformOptions.RenderingMode` 默认 Metal 优先 [4] |
| 通知 | 系统通知用 `osascript display notification`（独立子进程，异步、带超时），同时总是在应用内横幅和提醒历史里显示；Dock 图标弹跳提示 | ad-hoc 签名的 app 在 macOS 26 上通过 `UNUserNotificationCenter` 发通知不可靠（被丢弃或归到“脚本编辑器”）；正式归属需要 Developer ID 签名 [10][11] |

## 2. macOS 26 与旧版本的差异，以及我们怎么处理

| 差异 | 影响 | 处理 |
|---|---|---|
| **Liquid Glass 新外观**：链接 macOS 26 SDK 的应用，标题栏标题左对齐、控件更大更圆、窗口圆角可能更大；没用新 SDK 的应用保持旧外观。可在 Info.plist 写 `UIDesignRequiresCompatibility=YES` 强制旧外观 [12][13][14] | Avalonia 的内容区是自绘的（Skia），不会自动变成玻璃材质；只有原生窗口外框（红黄绿按钮、标题栏）跟随系统 | 不强制兼容模式，窗口外框交给系统；内容区沿用 Windows 版的 Fluent 风格配色并跟随系统深浅色。窗口内容四周留足边距，避免与大圆角冲突 |
| **菜单栏**：Tahoe 菜单栏默认透明；应用菜单名称来自 `CFBundleName`（最多 15 个字符），Dock 和访达显示 `CFBundleDisplayName` [4] | 中文全名 13 个汉字，但按 UTF-16 计 13 字符，可放进 CFBundleName；为保险，CFBundleName 用短名「美丽稻谷终端」，CFBundleDisplayName 用全名 | Info.plist 两项分开设置；使用 Avalonia `NativeMenu` 做原生菜单栏 |
| **应用图标**：Tahoe 优先使用 Icon Composer 生成的 `Assets.car`；只有 `.icns` 的应用会被缩小放进圆角方框（俗称“icon jail”）[15][16][17] | 生成 `Assets.car` 需要 Xcode 的 `actool`，Linux 上没有 | 本版只带 `.icns`（由现有图标生成），图标会显示在系统的圆角底板里，功能不受影响。以后有 Mac 时用 Icon Composer 补 `Assets.car` + `CFBundleIconName` |
| **Gatekeeper**：从 macOS 15 起，按住 Control 点“打开”不再能绕过检查；必须先双击一次，再到“系统设置 → 隐私与安全性”点“仍要打开”，并输入密码；该按钮只在尝试打开后约 1 小时内出现。macOS 26 相同 [8][18][19] | 用户第一次打开会被拦 | 使用说明写清两种方法：“仍要打开”或终端执行 `xattr -dr com.apple.quarantine <app>`；提醒用系统自带“归档实用工具”解压，避免第三方解压破坏签名 [19] |
| **Apple 芯片要求签名**：未签名的 arm64 可执行文件会被系统直接杀掉 | 不签名就打不开 | 打包脚本对整个 .app 做 ad-hoc 签名，并用 `rcodesign verify` 校验 |
| **Intel 支持结束**：macOS 26 是最后支持 Intel 的版本，Rosetta 2 只保留到 macOS 27 [5][6] | x64 包只服务 4 款机型 | 主推 arm64；x64 单独打包、说明中注明 |
| **通知权限**：macOS 26 对通知发送者的认定更严格，ad-hoc 签名应用的通知可能被归到“脚本编辑器”或不显示 [10][11] | 系统横幅可能看不到 | 提醒触发时：①应用内横幅 + 提醒历史（一定有）；②`osascript` 系统通知（需要用户在“系统设置 → 通知 → 脚本编辑器”里允许）；③Dock 图标弹跳。使用说明写明 |
| **App Nap / 计时器节流**：应用在后台、窗口被遮挡时，系统会推迟计时器、降低优先级 [20][21][22] | 后台时价格提醒的轮询会被拖慢 | 行情与提醒共用一个报价泵 `QuotePump`，它是**界面线程计时器**（Avalonia `DispatcherTimer`），每拍只发起一次合并请求：网络请求是异步 IO（数据层 `ConfigureAwait(false)`，等待和解析都不占界面线程），结果回到界面线程后只做合批更新。界面计时器会被 App Nap 推迟，所以应用启动时（未用 `--no-alerts`）通过 `NSProcessInfo beginActivityWithOptions:reason:`（`NSActivityUserInitiatedAllowingIdleSystemSleep`，仍允许系统空闲睡眠）声明“用户发起的活动”，退出时结束；不另开后台 `PeriodicTimer` |
| **ProMotion 120Hz / Metal** | 高刷新率下每帧时间只有 8 ms，界面线程上的任何重活都会掉帧 | Metal 渲染；图表重绘节流（见第 4 节）；大表格虚拟化 |
| **数据目录约定** | 应用数据应在 `~/Library/Application Support/<应用>` | 默认 `~/Library/Application Support/美丽稻谷终端`；若 `.app` 旁边有 `portable.flag`，数据放在 `.app` 旁边的 `data/`（与 Windows 便携版一致） |
| **系统代理** | Windows 版读注册表；macOS 的系统代理在 SystemConfiguration 里 | Meili.Data 新增读取 `scutil --proxy` 输出（HTTPS/HTTP/SOCKS，只认本机回环地址），再走原有的环境变量和常见端口探测 |
| **快捷键约定** | Cmd 代替 Ctrl；Cmd+Q 退出、Cmd+W 关窗、Cmd+, 设置、Cmd+M 最小化、Cmd+H 隐藏 [4] | 原生菜单 Gesture 用 `Meta+…`；Cmd+1…9 切页，Cmd+R 刷新（Mac 惯例，代替 F5），Cmd+F 打开代码搜索 |
| **关闭窗口的习惯** | Mac 上关最后一个窗口通常不退出，应用留在 Dock | 关闭窗口 = 隐藏窗口，应用继续运行（提醒照常检查）；点 Dock 图标恢复；Cmd+Q 才退出。不弹 Windows 版的“首次关闭”对话框 |

## 3. 代码结构

| 项目 | 平台 | 说明 |
|---|---|---|
| Meili.Core / Meili.Data / Meili.Analytics | net10.0 | 原有，两端共用 |
| **Meili.Shared**（新） | net10.0 | 从 Meili.App 移出的无 WPF 依赖服务（提醒监控、IV 取样、定价假设、文本格式化、策略存储等）、全部 ViewModel，以及界面抽象：`IUiPlatform`（界面线程投递、界面计时器、颜色资源、确认框、打开链接/文件夹、保存对话框、主题颜色变化事件）、`ILocalizer`（中英文字典，两端共用同一份字符串文件）、`IThemeController`、`IAlertNotifier` |
| Meili.App | net10.0-windows | WPF 界面，实现上述接口 |
| **Meili.Mac**（新） | net10.0（Avalonia 12） | macOS 界面，实现上述接口；9 个页面 |
| tests/Meili.Shared.Tests | net10.0 | 共用库测试（Linux 可跑） |
| tests/Meili.Mac.Tests | net10.0，xUnit v3 + Avalonia.Headless.XUnit 12.1.6 | 无头加载每个页面（Linux 可跑）[23] |

## 4. 防卡顿（彩虹转圈）、防卡死、防崩溃清单

| # | 规则 | 怎么落实 |
|---|---|---|
| 1 | 网络、JSON 解析、SQLite、文件读写都不在界面线程 | 数据层全部 async；大响应在线程池解析（沿用 Windows 版 C 系列改动）；导出 XLSX 在 `Task.Run` 里写 |
| 2 | 界面线程上禁止同步等待异步结果（`.Result` / `.Wait()` / `GetAwaiter().GetResult()`） | 共用库与 Mac 项目加单元测试，扫描源码禁止这几种写法 |
| 3 | 后台事件回到界面线程用“投递”（Post），不用同步 Invoke | `IUiPlatform.Post`：已在界面线程就直接执行，否则投递；Mac 端没有任何同步 `Dispatcher.Invoke` |
| 4 | 行情推送合批 | 报价泵按 1 秒节拍合并一批再更新界面（沿用 QuotePump），不是每个报价各自投递 |
| 5 | 大表格虚拟化 | 期权 T 型表、自选、新闻列表用虚拟化的 `ListBox`/`ItemsRepeater`，不用一次性生成全部行的面板 |
| 6 | 图表增量更新 + 重绘节流 | 只在数据或设置变化时重建图层；鼠标移动（十字线、画线）合并到每帧最多一次 `Refresh`（用 `DispatcherTimer` 16 ms 节流，与 ProMotion 无关地封顶） |
| 7 | 计时器与 App Nap | `QuotePump` 仍是界面线程计时器（不是后台 `PeriodicTimer`）：每拍一次合并请求，网络等待与解析在线程池，界面线程只做合批更新；重入保护（上一拍未完成就跳过）。为防 App Nap 推迟这个计时器，启动时声明用户活动、退出时结束（见第 2 节）。（更正：早先版本写成“提醒检查在后台 `PeriodicTimer`、仅有启用的提醒时声明活动”，与实现不符） |
| 8 | 事件订阅不泄漏 | 页面卸载时退订服务事件；单例 ViewModel 只订阅单例服务；报价订阅按页面 owner 管理（QuoteHub 已有） |
| 9 | 未处理异常 | `AppDomain.UnhandledException` 与 `TaskScheduler.UnobservedTaskException` 只写 crash.log；界面线程异常经 Avalonia `Dispatcher.UIThread.UnhandledException` 记录并标记已处理，**同一异常 1 秒内重复出现时不再弹提示**——避免 Windows 版 M7 那样“错误处理中再次触发布局→再报错”的重入（当时 1 秒内写了 2,632 行日志）；错误提示用应用内横幅，不弹模态框 |
| 10 | 子进程（osascript、scutil、open）不阻塞界面 | 全部 `Process` 异步启动，读输出加超时（2–5 秒），超时就结束子进程 |
| 11 | 渲染 | 默认 Metal；Metal 初始化失败时 Avalonia 自动退到 OpenGL / 软件渲染 |
| 12 | 启动 | 窗口先显示，数据异步加载；SQLite 原生库随 Microsoft.Data.Sqlite 自带 osx-arm64 / osx-x64 版本 |

## 5. 功能对照（Windows ↔ macOS）

9 个页面全部提供，复用同一套 ViewModel：概览、自选、K 线（指标 + 画线）、期权 T 型表（IV/希腊值）、策略盈亏、新闻与基本面、价格提醒、数据源、设置。中英文、深浅色（默认跟随系统）、红涨绿跌/绿涨红跌、CSV/Excel 导出（macOS 保存面板）。不提供：Windows 托盘图标（Mac 用 Dock + 菜单栏代替）、首次关闭对话框（Mac 惯例不需要）。

## 6. 打包

`build/pack-mac.sh`（在 Linux 开发机上跑；`RCODESIGN` 指向 rcodesign 0.29）：

1. `dotnet publish src/Meili.Mac -r osx-arm64`（及 `osx-x64`）`--self-contained`，不裁剪（Avalonia 反射较多），**单文件发布**（`PublishSingleFile`，原生库不打进单文件）。这样 `Contents/MacOS` 里只有 Mach-O：`MeiliMac` 与 4 个 `.dylib`。若把几百个 `.dll` 散放在 `Contents/MacOS`，签名工具会把它们当“嵌套代码”，签名只能写进扩展属性，zip 带不走，到 Mac 上就验不过。
2. `build/mac/bundle.py app` 组装 `美丽稻谷卖方绿泡群专用终端.app/Contents/{MacOS,Resources,Info.plist,PkgInfo}`：`CFBundleIdentifier = com.meilidaogu.terminal`；`CFBundleDisplayName` 全名、`CFBundleName`「美丽稻谷终端」；`LSMinimumSystemVersion = 26.0`；`NSHighResolutionCapable = true`；`LSApplicationCategoryType = public.app-category.finance`；可执行文件与 `.dylib` 权限 755。
3. 图标：`build/mac/make_icon.py` 按苹果图标网格（1024 画布、824 圆角方块、约 22% 圆角）重绘 Windows 图标，写出含 16–1024 各尺寸的 `AppIcon.icns`。
4. `rcodesign sign`（ad-hoc）。rcodesign 0.29 的 `verify` 会因 ad-hoc 签名里的空 CMS 块报错（苹果 codesign 生成的 ad-hoc 签名同样带空 CMS），不能用；改用 `build/mac/verify_sig.py` 独立校验：每个 Mach-O（含通用二进制的每个切片）的 CodeDirectory 是 ad-hoc、逐页 SHA-256 与文件一致、需求/Info.plist/CodeResources 特殊槽一致，`CodeResources` 里每个文件的哈希一致且没有漏封的文件。改动任一字节（dylib、Info.plist、图标）都能被它查出。
5. `build/mac/bundle.py zip`：用 Python zipfile 打包（开发机未必有 `zip` 命令），逐项写入 Unix 权限（`external_attr`），符号链接按链接保存，文件名带 UTF-8 标志；zip 内附 `使用说明.txt`。
6. 产物：`美丽稻谷卖方绿泡群专用终端-macOS.zip`（arm64，主包）与 `-macOS-x64.zip`（Intel），连同 `使用说明.txt` 放进 `Installers\3-macOS便携版`。打包脚本最后检查包内没有开发机个人路径。

## 7. 测试

- 共用库：从 Meili.App.Tests 迁过来的 ViewModel 测试改在 Linux 也能跑；新增 `scutil --proxy` 解析测试、禁止同步等待的源码扫描测试。
- Mac 界面：Avalonia 无头模式逐页加载（中/英 × 深/浅），检查没有绑定错误和未处理异常；检查最小字号 ≥ 12。

## 8. 能验证与不能验证的

- 能在 Linux 开发机验证：编译、共用库与无头界面测试、.app 目录结构与 Info.plist、`rcodesign verify` 签名校验、zip 中的可执行权限。
- **不能验证（没有 Mac）**：在 macOS 26 上实际启动、Metal 渲染效果、Gatekeeper 提示文字、系统通知显示、App Nap 实际表现、菜单栏与 Dock 行为、Intel 包在真机上运行。

## 来源

1. Avalonia Docs – Supported platforms（macOS 26 Tahoe Tier 1，ARM64 与 x64）https://docs.avaloniaui.net/docs/supported-platforms
2. NuGet – Avalonia 12.1.6（2026-11-09）https://www.nuget.org/packages/avalonia
3. NuGet – ScottPlot.Avalonia 5.1.59（依赖 Avalonia ≥ 12.0.0）https://www.nuget.org/packages/ScottPlot.Avalonia
4. Avalonia Docs – macOS 平台指南（原生后端、RenderingMode 默认 Metal/OpenGl/Software、CFBundleName 15 字符、NativeMenu、快捷键约定）https://docs.avaloniaui.net/docs/platform-specific-guides/macos
5. Tom's Hardware – macOS Tahoe is the last release for Intel Macs https://www.tomshardware.com/software/macos/macos-tahoe-is-the-last-release-for-intel-macs-apple-silicon-exclusivity-will-mark-end-of-hackintoshes
6. Apple Support – macOS Tahoe 26 is compatible with these computers https://support.apple.com/en-us/122867
7. Wikipedia – macOS Tahoe（后继版本只支持 Apple 芯片）https://en.wikipedia.org/wiki/MacOS_Tahoe
8. Glideshot – How to open an app that isn't notarized on macOS（Open Anyway 约 1 小时有效、xattr、用归档实用工具解压）https://glideshot.xyz/blog/open-apps-not-notarized-macos/
9. dotnet/sdk #34917 – Support macOS ad hoc signing on all platforms；Apple Codesign (rcodesign) 文档 https://github.com/dotnet/sdk/issues/34917 ，https://gregoryszorc.com/docs/apple-codesign/stable/
10. agent-notifications #59 – macOS Tahoe: notifications attributed to Script Editor（ad-hoc 签名）https://github.com/777genius/claude-notifications-go/issues/59
11. CodeWhale #4847 – macOS notifications are attributed to Script Editor; needs a real .app bundle https://github.com/Hmbown/CodeWhale/issues/4847
12. Qt Blog – Qt on macOS 26 Tahoe（UIDesignRequiresCompatibility）https://www.qt.io/blog/qt-on-macos-26-tahoe
13. Lapcat Software – macOS Tahoe windows have different corner radiuses http://lapcatsoftware.com/articles/2026/3/1.html
14. The Eclectic Light Company – Which apps for macOS Tahoe? https://eclecticlight.co/2025/09/14/last-quarter-on-my-mac-which-apps-for-macos-tahoe/
15. Apple Developer – Creating your app icon using Icon Composer https://developer.apple.com/documentation/xcode/creating-your-app-icon-using-icon-composer
16. d12frosted – Liquid Glass Icons for macOS Tahoe（只有 .icns 时的“icon jail”）https://www.d12frosted.io/posts/2026-01-08-emacs-plus-liquid-glass-icons
17. Successful Software – Updating application icons for macOS 26 Tahoe https://successfulsoftware.net/2025/09/26/updating-application-icons-for-macos-26-tahoe-and-liquid-glass/
18. Ars Technica – macOS 15 Sequoia makes you jump through more hoops to disable Gatekeeper https://arstechnica.com/gadgets/2024/08/macos-15-sequoia-makes-you-jump-through-more-hoops-to-disable-gatekeeper-app-checks/
19. AppleInsider – Gatekeeper changes in macOS Sequoia https://appleinsider.com/inside/macos-sequoia/tips/whats-changed-in-runtime-protection-for-macos-sequoia
20. Apple – Energy Efficiency Guide for Mac Apps: Extend App Nap https://developer.apple.com/library/archive/documentation/Performance/Conceptual/power_efficiency_guidelines_osx/AppNap.html
21. Apple – Energy Efficiency Guide: Prioritize Work at the App Level https://developer.apple.com/library/archive/documentation/Performance/Conceptual/power_efficiency_guidelines_osx/PrioritizeWorkAtTheAppLevel.html
22. Lapcat Software – Prevent App Nap Programmatically https://lapcatsoftware.com/articles/prevent-app-nap.html
23. NuGet – Avalonia.Headless.XUnit 12.1.6（Avalonia 12 起只支持 xUnit v3）https://www.nuget.org/packages/Avalonia.Headless.XUnit/
