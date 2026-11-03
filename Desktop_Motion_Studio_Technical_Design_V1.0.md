# Desktop Motion Studio｜技术设计文档（TDD V1.0）

> **文档类型**：Technical Design Document（技术架构与工程实施规范）  
> **文档版本**：1.0.0  
> **编制日期**：2026-11-02  
> **需求基线**：[Desktop Motion Studio｜PRD V1.0](./Desktop_Motion_Studio_PRD_V1.0.md)  
> **目标产品**：Windows 11 x64 本地优先的开源应用启动动效平台  
> **状态**：可用于技术评审与原型开发；含少量明确标识的待验证技术决策

---

## 0. 执行摘要

**推荐路线：** `.NET 10 LTS + WinUI 3（Studio）+ 独立轻量 Launcher（启动）+ 独立 MotionHost（渲染）+ Win32/Shell（应用与窗口管理）+ Composition/Win2D（高级效果）+ DWM（可选高级交接）`。

### V1 核心约束（与 PRD 保持一致）

- **内置精品动效**：首发动效由维护者编排、打包和验证；可包含视频、GPU 粒子及其他原生特效，**不统一强制为 MP4**。
- **跨应用自由绑定**：用户可把「GPT 龙娘」绑定给微信等受支持的应用；同一动效可复用；场景代码不得将实际启动目标写死。
- **管理器非必需常驻**：用户关闭 Studio，受管理快捷方式仍能打开 Launcher 完成启动。
- **已运行应用直接激活**：不重复播放动效，也尽可能避免重复启动进程。
- **跳过**：Esc / 可见跳过按钮终止动画；不得中断已发出的应用启动请求。
- **展示方式由作品决定**：全屏或居中悬浮，V1 用户不调节动效参数。
- **先保软件再保特效**：动画不能成为正常启动的单点故障；失败时执行降级或退出覆盖层。
- **V1 不支持**：用户动效导入/导出、AI 内置、桌宠、换肤、片段级自由组合、全局 Hook、替换系统 Shell。

### 决策标记

| 标记 | 含义 |
|---|---|
| **[需求确定]** | 已经与产品需求对齐，不应在开发时随意更改 |
| **[架构推荐]** | 当前推荐的实施选择，可经技术评审优化 |
| **[POC 待验证]** | 必须在 Windows 11 真机原型上验证，未经验证不得视为已实现 |

---

## 1. 技术目标与非功能性要求

| 类别 | 目标 / 验收方式 | 级别 |
|---|---|---|
| 可用性 | 一键绑定；首页可见应用—动效对应关系；单独预览与测试启动 | P0 |
| 启动可靠性 | 动效崩溃、解码失败或交接失败时，Launcher 能继续执行目标应用启动或给出清晰错误 | P0 |
| 隔离性 | Studio、Launcher、MotionHost 独立进程；渲染故障不应直接杀死目标应用 | P0 |
| 可维护性 | 新增 Scene 不修改通用应用启动/Shortcut 核心；新增 AppAdapter 不改 Scene | P0 |
| 安全性 | 不注入目标应用、不修改其安装包、不默认提权、不启动本地 HTTP/CDP 服务 | P0 |
| 兼容性 | 明确系统与应用支持矩阵；不根据程序名称猜测是否兼容 | P0 |
| 流畅度 | 在参考硬件上以 60 FPS 为优化目标；允许不同硬件降级，**不承诺所有环境固定帧率** | P1 |
| 启动开销 | Launcher 不要求 Studio 常驻；对比同一应用的原生启动建立性能基线 | P0 |
| 隐私 | 默认本地运行、无账号、无遥测；本地诊断日志不包含聊天内容和窗口画面 | P0 |
| 可访问性 | Esc 可跳过；键盘可操作；遵循系统减少动态效果偏好 | P0 |

> 建议先测量 Launcher 冷启动和 MotionHost 首帧延迟，再确定毫秒级性能门槛；在缺少真机基线前，不写虚假的绝对指标。

---

## 2. 运行环境与版本兼容策略（重点）

### 2.1 Windows 11 支持边界

**[架构推荐] V1 正式支持：** Windows 11 **25H2 x64（OS Build 26200 系列）**、**26H2 x64（OS Build 26300 系列）**，且处于微软支持周期内的累积更新版本。正式发布前需在两个分支分别进行真机或等价桌面环境验收。

| OS / 架构 | V1 支持策略 | 理由 / 处理方式 |
|---|---|---|
| Windows 11 25H2 x64 | **正式支持候选** | 目前仍在支持期；作为最低正式测试基线 |
| Windows 11 26H2 x64 | **正式支持候选** | 作为较新 Windows 11 分支进行回归测试 |
| Windows 11 24H2 x64 | 不列入 Home/Pro 正式支持；可做尽力兼容 | 24H2 Home/Pro 已于 2026-10 停止支持；企业版生命周期另算 |
| Windows 11 26H1 | V1 不作 x64 正式承诺 | 微软将该版本定位于部分新硬件；不应按普通 x64 升级分支假设 |
| Windows 10 | 不支持 | PRD 首发仅 Windows 11 |
| Windows on ARM（ARM64） | 不支持 | V1 仅构建与验证 win-x64；未来单独规划 |
| 企业策略限制环境 / 远程桌面 / 虚拟机 | 尽力兼容，必要时关闭高级转场 | GPU、窗口捕获、前台焦点和安全策略可能不同 |

**区别：** Windows SDK / App SDK 声称可在较旧 Windows 上运行，不等于 Desktop Motion Studio 向用户作出相同支持承诺。发布页须显示实际测试过的版本与构建号；OS 版本检测不能替代功能探测。

### 2.2 依赖版本基线（截至 2026-11-02）

| 组件 | 推荐基线 | 锁定与升级策略 |
|---|---|---|
| 运行时 / 语言 | **.NET 10 LTS，C#** | 产品主版本内固定 `net10.0-windows...`；安全补丁随经测试的 servicing 更新 |
| Windows UI | **WinUI 3** | 随 Windows App SDK 稳定版；不使用 Preview / Experimental API 进入发布版 |
| Windows App SDK | **2.6.4 Stable**（2026-10-27 发布） | 在 `Directory.Packages.props` 精确锁定；每次升级跑完整测试矩阵 |
| Windows SDK | 推荐 10.0.26100 系列的稳定 SDK | 仅提供编译期 API；高版本 API 使用前执行运行时能力检查 |
| Win2D | `Microsoft.Graphics.Win2D`，**具体版本待 POC 锁定** | 确认与所选 Windows App SDK / WinUI 3 的二进制与资源生命周期兼容，再固定补丁版本 |
| 视频 / 音频 | Windows 媒体能力优先，必要时封装专用播放器 | 针对实际内置媒体逐个验证编解码、音轨和 GPU 资源清理 |
| Windows 原生 API | Win32 / Shell / DWM | 按 Windows OS 构建号、函数返回值与能力探测处理兼容，不单凭编译成功认定可用 |
| CI 构建工具 | Visual Studio 2026 稳定工具链、.NET 10 SDK | `global.json` 固定 SDK feature band；CI 与开发机按同一版本构建 |

**关键解释：** Windows **OS 版本**、**Windows SDK**、**Windows App SDK** 是彼此独立的版本体系。使用较新 Windows SDK 编译，并不代表较旧 OS 具备其全部 API。遇到新 API 时以 `ApiInformation`、系统版本及实际功能探测联合判断。详见 §17 官方资料。

### 2.3 第三方目标应用版本兼容（比框架版本更容易出问题）

ChatGPT、Codex、微信、VS Code 等都会更新安装位置、进程树、窗口结构以及单实例机制。**适配依据必须是能力和身份，而不是硬编码一串 EXE 名称。**

- 每种应用记录稳定标识（优先可执行文件路径 / 签名或安装包身份 / AUMID；按适配类型取值），应用显示名仅作 UI 展示。
- 普通 Win32 使用 `ShellExecuteEx` 或与其等价的 Shell 启动路径，并保留正确工作目录及参数；不得简单把用户路径拼进命令字符串交给 `cmd.exe`。
- 已安装的打包应用优先存储 **AUMID / Package Family Name** 等可重定位身份，按合适的 Shell / 应用激活 API 打开；不得持久化 WindowsApps 内部易变化的版本路径。
- 检测现有窗口时结合进程身份、进程创建时间、顶层窗口、可见性和窗口稳定时间；**窗口标题不得作为唯一身份依据**。
- 不允许将「发现一个同名进程」直接推导为「目标已经可用」；需区分托盘后台驻留与真实主窗口。
- 目标应用版本更新后：先尝试身份重新解析；找不到时在 Studio 显示「需重新定位」，已存在的安全启动方式可继续工作；禁止自动修改官方安装文件。
- 通过维护 `app-compatibility.json` 记录代表性目标应用**实测版本、启动方法、窗口检测规则、已知限制**。未经实测不得宣称全面兼容。

### 2.4 兼容性等级

| 等级 | 意义 | 用户呈现 |
|---|---|---|
| `Verified` | 已在具体 OS + 应用版本 + 场景组合完成测试 | 兼容 |
| `SupportedFallback` | 可以正常启动和播放，但高级 DWM 交接无法验证或不可用 | 可用，采用标准转场 |
| `Experimental` | 能定位应用，但窗口或单实例行为尚未充分测试 | 实验性支持 |
| `Unsupported` | 无法可靠完成启动、窗口管理或存在风险 | 不允许一键启用；说明原因 |

这属于**应用适配能力等级**，不是对 GPT 龙娘等内置动效艺术品质的评级。

---

## 3. 总体架构

```text
                         ┌──────────────────────────────────┐
                         │       Desktop Motion Studio      │
                         │      Studio.exe (WinUI 3)        │
                         │ 我的应用 / 动效库 / 预览 / 设置  │
                         └───────────────┬──────────────────┘
                                         │ 版本化本地配置 / 受限 IPC
                         ┌───────────────▼──────────────────┐
 Desktop managed .lnk ──▶│      Launcher.exe (.NET 10)      │
                         │ 绑定解析 / 运行检测 / 启动 / 兜底 │
                         └─────────┬──────────────┬─────────┘
                                   │              │
                       Shell/Win32 │              │ IPC (requestId)
                                   ▼              ▼
                         ┌────────────────┐ ┌───────────────────────┐
                         │ 真实目标应用   │ │ MotionHost.exe        │
                         │ GPT/微信等     │ │ Scene / GPU / Video   │
                         └────────────────┘ │ Skip / Fade / DWM      │
                                            └───────────┬───────────┘
                                                        │
                                            Scene Registry / Assets
                                            （只读可信随包资源）
```

### 3.1 核心工程约束

1. **Studio 不是运行时依赖**：关闭 Studio 后，Launcher 仍可独立读取已提交配置。
2. **Launcher 是故障控制面**：MotionHost 只负责动画和交接请求，不决定要启动什么可执行程序。
3. **MotionHost 是可销毁渲染面**：解码器、GPU、透明窗口问题只影响本次视觉演出，不阻断应用进程。
4. **绑定数据和动效实现解耦**：应用选择由 Binding 决定，场景只接收目标窗口和能力描述。
5. **接口版本化**：进程 IPC、Scene Manifest 和持久化状态分别管理版本，不能混用一个版本号。

### 3.2 建议仓库布局

```text
DesktopMotionStudio/
├─ docs/
│  ├─ PRD.md
│  ├─ TDD.md
│  ├─ ADR/                         # 架构决策记录
│  └─ compatibility/               # 测试覆盖与已知问题
├─ src/
│  ├─ Studio/                      # WinUI 3 UI + MVVM
│  ├─ Launcher/                    # 轻量启动入口
│  ├─ MotionHost/                  # 渲染与动效窗口
│  ├─ Core/                        # 状态机、身份模型、接口
│  ├─ AppAdapters/                 # Win32 / Packaged / ShellLink
│  ├─ SceneRuntime/                # Scene / Sequence / Effect
│  ├─ EffectModules/               # Video / Composition / Win2D
│  ├─ Handoff/                     # Fade / DWM + fallback
│  ├─ Contracts/                   # IPC DTO / schema
│  └─ Infrastructure/              # 配置、日志、更新、快捷方式
├─ scenes/                         # 官方内置资源/作品清单
├─ tests/
│  ├─ Unit/
│  ├─ Integration/
│  ├─ Contract/
│  └─ WindowsE2E/
├─ packaging/
├─ .github/workflows/
├─ global.json
├─ Directory.Packages.props
└─ LICENSE / NOTICE / CONTRIBUTING.md
```

不要求每个文件夹必须成为独立程序集；**稳定的模块依赖方向和测试边界比项目数量更重要**。

---

## 4. Studio 管理客户端：实现规范

**[架构推荐]** WinUI 3 + MVVM（`CommunityToolkit.Mvvm` 仅作为可选依赖，需经过版本与许可确认）。使用系统主题、Mica/适配性背景；无模糊能力时自然回退为纯色背景。

### 4.1 页面与数据绑定

| 页面 | UI 主要内容 | 底层调用 |
|---|---|---|
| 我的应用（默认） | 应用图标、绑定作品、启用状态、快捷方式健康检查、预览/更换/测试 | ApplicationRepository、BindingService、ShortcutHealth |
| 动效库 | 官方作品封面、主题说明、播放预览、选择应用 | SceneRegistry、PreviewCoordinator |
| 设置 | 系统/深浅色、静音、快捷方式修复、日志与关于 | SettingsService、Diagnostics |

### 4.2 UX 规范

- 添加应用：发现已安装程序 + 搜索 + 手动选择；如果识别有歧义，展示路径/包身份供用户确认。
- 更换作品：先预览，后提交绑定；同一应用只能有一个当前 Scene；同一 Scene 可以绑定多个应用。
- 点击「启用」：先检查目标应用入口与 Scene 兼容性，再写绑定、创建快捷方式并回读验证；未成功则显示明确失败并回滚。
- 预览：不真实启动目标程序；使用 Mock/占位窗口模拟交接，避免预览时意外打开 Codex。
- 测试启动：走与桌面快捷方式相同的 Launcher 入口，只是不写快捷方式。
- 界面状态：加载、成功、失败、有待修复四种反馈；可恢复错误尽量提供单击修复入口。
- V1 不暴露转场参数、视频速度、粒子强度、窗口句柄等开发者概念。

### 4.3 真实资源的访问模式

Studio 不能把场景运行的 GPU Surface 直接传给 Launcher；预览由 `MotionHost --mode preview` 创建独立实例或者使用同一场景执行内核的嵌入式预览宿主（**选其一，先 POC**）。真实启动与预览必须复用同一 Scene 定义及渲染逻辑，减少“预览正常、实机不同”的偏差。

---

## 5. AppAdapter：目标应用启动与窗口识别

### 5.1 接口草案

```csharp
public interface IAppAdapter
{
    string AdapterId { get; }
    Task<AppResolveResult> ResolveAsync(ApplicationProfile app, CancellationToken ct);
    Task<ExistingWindowResult> FindExistingWindowAsync(ApplicationProfile app, CancellationToken ct);
    Task<LaunchDispatchResult> LaunchAsync(ApplicationProfile app, CancellationToken ct);
    Task<WindowReadyResult> WaitForWindowAsync(ApplicationProfile app, LaunchDispatchResult launch,
        TimeSpan timeout, CancellationToken ct);
    Task<ActivationResult> ActivateAsync(WindowTarget target, CancellationToken ct);
}
```

**注意**：这是职责示意，不是已经验证可编译的最终 SDK 接口；实际返回类型、线程与 COM 约束由 POC 后确定。

### 5.2 适配类型

1. `Win32ExecutableAdapter`：普通 EXE，使用绝对路径、工作目录、保留的启动参数和进程身份。
2. `ShellLinkAdapter`：`.lnk` 作为受支持的原始启动对象，正确解析目标与参数；不要只读取显示名。
3. `PackagedAppAdapter`：Microsoft Store/MSIX，使用包身份 / AUMID 等官方激活机制；不能依赖 WindowsApps 版本路径。
4. `KnownAppProfile`：面向 Codex、微信、VS Code 等代表性应用的识别和窗口行为专项测试配置，不以私有实现细节作为强依赖。

### 5.3 窗口选择与前台激活

- 使用 `EnumWindows`、`GetWindowThreadProcessId`、`IsWindowVisible`、DWM cloaked 属性及窗口边界等信号组合筛选。
- 同一个应用可能存在 Splash、登录、主窗口、隐藏托盘窗口；采用可扩展的「主窗口候选评分」机制。
- 不把进程 PID 当永久身份；PID 可能重用，关联启动实例时结合创建时间/进程身份。
- `SetForegroundWindow` 受 Windows 前台切换规则约束；被拒绝时不得通过激进 Hook、持续抢焦点实现。可根据 UX 采用任务栏提示并允许用户手动激活。
- 找到可显示窗口并不等于应用数据已完全加载；不能扫描或依赖用户会话、聊天内容。

---

## 6. Launcher：启动状态机与关键竞态

### 6.1 状态定义

```text
Requested
  ├─ ExistingAppFound ──▶ ActivateExisting ──▶ Completed
  └─ NotRunning
       ├─ ScenePrepared ─┐
       └─ AppDispatched ─┴─▶ WaitingForWindow
                                    ├─ WindowReady ─▶ Handoff ─▶ Completed
                                    ├─ UserSkipped ─▶ HideAnimation ─▶ ContinueAppWait/Complete
                                    ├─ SceneFailed ─▶ HideAnimation ─▶ ContinueAppWait/Complete
                                    └─ Timeout ─▶ Cleanup ─▶ ReportAppStatus
```

**关键：** 状态机不应被设计成强制“视频播放完才打开软件”。目标应用与动画准备可并行进行；退出动效窗口不等于取消目标应用的启动。

### 6.2 幂等与并发规则

- 每次点击生成一个 `launchRequestId`，用于关联 Launcher、MotionHost 和诊断日志。
- 对同一 `applicationId` 的近同时请求进行串行化或合并；避免连续双击产生多个动画与重复启动。
- 应用已经运行时绕过 MotionHost，直接尝试激活主窗口。
- 若已有一次该应用的启动请求尚在进行，后续请求应沿用同一状态或直接聚焦，不重复发起 `LaunchAsync`。
- 场景预热不能长时间阻塞应用启动；应有启动动画专用准备超时和不显示动效的回退路径。
- `Esc` 或跳过按钮仅影响 **AnimationCancellationToken**；真正应用启动 token 与其分离。

### 6.3 故障语义

| 事件 | 行为 | 日志事件 |
|---|---|---|
| 动效无法初始化 | 继续启动应用；不出现黑屏 | `scene.prepare.failed` |
| 视频解码 / GPU 设备丢失 | 尝试降级效果或 Fade；清理覆盖层 | `renderer.failed` |
| DWM 注册失败 | 采用普通淡出 | `handoff.fallback` |
| 用户跳过 | 立即隐藏动效；应用启动继续 | `scene.skipped` |
| App 启动请求失败 | 关闭动效；呈现明确提示/错误码 | `app.launch.failed` |
| 找不到应用主窗口 | 结束动画并向用户说明无法自动交接；不杀目标进程 | `app.window.timeout` |
| Studio 意外退出 | 不影响已有 Launcher / 目标应用 | `studio.exited` |

「动画失败仍启动应用」只适用于**原始应用启动请求可以合法完成**的情况；如果目标应用路径不存在或系统拒绝激活，应如实报错，不能虚称启动成功。

---

## 7. Scene Runtime：华丽动效与未来扩展

### 7.1 分层责任

```text
Scene（完整作品、用户 V1 的唯一选择单位）
 └─ Sequence（维护者固定编排的阶段）
     ├─ MediaLayer（视频 / 图片 / 音频等可信资源）
     ├─ CompositionEffect（位移 / 缩放 / 透明度 / 视觉过渡）
     ├─ Win2DEffect（GPU 粒子 / 光环 / 2D 特效）
     ├─ ShaderEffect（可选，专用版本锁定的内部代码）
     └─ HandoffCue（窗口准备好时的收尾锚点）
```

### 7.2 事件与时间轴

所有作品至少识别以下事件：

`scene.started`、`scene.ready_to_handoff`、`app.launch_dispatched`、`app.window_ready`、`scene.skip_requested`、`scene.finished`、`scene.failed`。

- 使用单调时钟推进视觉时间；不以 `DateTime.Now` 为动画帧时钟。
- 表演前半段可按时间轴运行；最终交接要能等待 `app.window_ready` 或超时事件。
- 每个 Scene 必须声明最迟自动退出时限，禁止无限期遮挡桌面。
- CPU/GPU 性能较低时允许跳帧、简化粒子、关闭可选视觉层；**功能上用户不能编辑作品参数，但引擎可做自动性能降级**。
- 内置作品应在屏幕比例、分辨率、DPI、系统文字缩放、多显示器条件下进行验证。

### 7.3 内部 Scene Manifest（示例协议，尚未发布）

```json
{
  "schemaVersion": 1,
  "sceneId": "dragon-girl-portal",
  "sceneVersion": "1.0.0",
  "title": "GPT 龙娘 · Portal",
  "originalInspiration": "Codex",
  "presentation": "fullscreen",
  "requiredCapabilities": ["video"],
  "optionalCapabilities": ["win2d", "dwm-thumbnail"],
  "handoff": { "preferred": "dwm-thumbnail", "fallback": "fade" },
  "entrySequence": "intro",
  "sequences": [
    { "id": "intro", "type": "trusted-built-in", "module": "DragonIntro" },
    { "id": "reveal", "type": "trusted-built-in", "module": "GenericReveal" }
  ]
}
```

**约束：** `sceneId` 稳定且跨版本不变；Scene 不包含固定目标 EXE 路径、硬编码 Codex 窗口句柄或目标进程启动权限。这里 `module` 只能引用产品已编译、已签名/打包的内置效果，**不能自动执行外部任意 DLL / JS / 脚本**。

### 7.4 动效分辨率与视频资源

- 视频只作为可选视觉层；其资源编码在打包阶段校验，不由运行时猜测。
- 内置大型素材可以分包或压缩，但资源清单必须有内容哈希、大小、版权信息与最低引擎版本。
- 首次加载可能需要预热；未完成预热时禁止展示空白黑窗。
- 处理高刷新率显示器应按实际刷新时钟运行；60 FPS 是视觉目标，不应硬编码基于 60Hz 的帧数计时。

---

## 8. MotionHost：渲染与透明窗口方案

### 8.1 推荐技术分工

| 渲染能力 | 首选 | 风险 / 降级 |
|---|---|---|
| 简单变换与过渡 | Microsoft.UI.Composition / WinUI 3 Visual Layer | 必须验证与实际透明顶层窗口组合的限制 |
| 复杂 2D 与粒子 | Win2D | GPU 不可用或设备丢失时简化或取消效果 |
| 视频 / 音频 | Windows 媒体播放模块 | 为实际内置编码建立测试素材库，失败则跳过该层 |
| 高级 Shader | 可选 Direct3D 内置模块 | 不进入 V1 基础链路；按作品需要引入 |
| 窗口融合 | DWM Thumbnail | 不支持 / 失败时采用 Fade，绝不能成为必要条件 |
| 透明浮层 | **[POC 待验证]** 专用 Win32 顶层窗口 + 适配的合成宿主，或可行的 WinUI 3 宿主方案 | 优先遵循 Windows 支持的窗口组合方式，不强制统一 UI 技术 |

### 8.2 重要的技术边界

**WinUI 3 很适合 Studio 管理 UI，但不能直接假定 WinUI 3 原生窗口能够无成本实现所有“分层透明窗口 + 复杂 GPU 场景 + DWM 缩略图”组合。**

在正式设计 MotionHost UI 之前，要求先完成以下 POC：

1. 创建置顶、无边框、可 Esc 关闭的全屏 / 居中宿主窗口。
2. 验证有透明区域的复杂动画是否正确显示、是否能避免黑底/闪烁。
3. 验证视频、Composition、Win2D 能否按需在同一视觉树稳定渲染，或需要采用受控的分层合成。
4. 验证 MotionHost 与真实应用之间的焦点、输入穿透与任务栏行为。
5. 验证多显示器、窗口移动和 DPI 切换，以及 MotionHost 进程崩溃后的窗口清理。

**POC 失败时的可接受替代：** 仍然用 WinUI 3 构建 Studio；MotionHost 单独采用已验证的 Win32 + 合成技术或受约束的视频窗口，不因渲染宿主变化重写上层 Scene、Adapter、Binding。

### 8.3 DWM 高级交接限制

- `DwmRegisterThumbnail` + `DwmUpdateThumbnailProperties` 提供的是**目标窗口实时二维缩略预览**，可配置目标矩形、不透明度和可见性，不是可交互的完整应用控件。
- 真实目标窗口需要真实存在、可识别并满足 DWM 条件；窗口被保护、后台限制或 API 失败时回退 Fade。
- 不默认移动或缩放用户真实应用窗口来迎合视频分镜。若后续某个高级 Scene 需要该能力，必须单独评估并恢复原位置、尺寸和状态。
- 角色演出与最终交接分离：跨应用复用时使用当前绑定 APP 的窗口，不能把 Codex 的静态 UI 截图当成微信的真实窗口。

---

## 9. Shortcut 与安装/卸载策略

### 9.1 V1 快捷方式合同

- **用户明确点击「启用」才创建**受管理的 `.lnk`，不执行全盘扫描或替换既有快捷方式。
- Windows 桌面快捷方式目标为稳定的 `Launcher.exe`，参数仅包含经过校验的内部 `bindingId`，例如 `--binding <UUID>`。
- 应用目标和启动参数存于受版本管理的绑定记录内，**不把任意可执行命令嵌入可编辑快捷方式参数**。
- 设置合理的图标位置：尽量从官方应用或包标识解析；图标缺失时使用产品内明确的占位符。
- 创建操作采用“临时文件 → 备份冲突 → 保存 → 验证 → 原子性替换（能实现时）”的受控流程；禁止覆盖不是本工具创建的同名文件。
- Studio 记录本软件创建的实际快捷方式位置和校验信息；用户手动修改后不能擅自恢复覆盖。

### 9.2 安装方式：两阶段决策

**[架构推荐] 首发采用按用户安装的传统安装程序 + unpackaged WinUI 3 + self-contained 部署**。原因：Launcher 需要稳定路径、便于管理普通 `.lnk`，也不强制用户先独立安装 Windows App SDK 运行时。

代价是安装包更大，且没有 MSIX 包身份的自动更新/清理能力。要做好自己的安装、升级、卸载事务与系统“已安装应用”注册。发布工具（WiX / Inno Setup 等）经 POC 后定案，不在文档中硬指定未经验证的打包参数。

**备选：** 若 POC 表明 MSIX 或带外部位置的包能够更可靠地提供进程间交互、受管理快捷方式与升级能力，可在**维持 AppAdapter/Scene 合同不变**的前提下调整分发形式。Windows App SDK 运行时是否自包含，以及 MSIX 与否，是两项应独立决定的部署维度。

### 9.3 卸载与保留数据

- 默认卸载受管理快捷方式、程序文件与非个人缓存；保留或删除用户配置需明确提示。
- 不修改第三方应用的安装路径、配置、用户数据或默认协议关联。
- 用户自己移动/重命名快捷方式后，避免按旧路径删除无关文件；检查本工具创建记录与目标信息。
- 安装/升级失败必须保证原有正式版可恢复运行；不得将所有快捷方式永久指向已删除版本目录。

---

## 10. 数据模型、持久化与协议版本

### 10.1 数据目录

建议（可根据安装模式调整）：

- 只读内置场景：安装目录 `assets/scenes/`。
- 用户状态：`%LOCALAPPDATA%\DesktopMotionStudio\state\`。
- 日志：`%LOCALAPPDATA%\DesktopMotionStudio\logs\`。
- 临时工作：`%LOCALAPPDATA%\DesktopMotionStudio\temp\`。

不要把用户状态写在安装目录或场景资源内部，以免更新覆盖。

### 10.2 Binding 结构示例

```json
{
  "schemaVersion": 1,
  "bindingId": "b484cabe-e8fc-4e63-958b-395d58faf593",
  "applicationId": "local-app-001",
  "adapterId": "packaged-app",
  "appIdentity": { "aumid": "example.package!App" },
  "sceneId": "dragon-girl-portal",
  "enabled": true,
  "managedShortcutId": "shortcut-001"
}
```

**说明：** AUMID 为示意数据，不代表真实 Codex 或微信应用身份；实际版本应先经系统 API 验证获得。

### 10.3 版本号分离原则

| 版本域 | 示例 | 变更原则 |
|---|---|---|
| Product Version | `1.0.0` | SemVer，发布版本 |
| Config Schema | `1` | 仅数据格式不兼容时升级 Major |
| Scene Schema | `1` | 资源清单解析合同独立维护 |
| Scene Version | `1.0.0` | 单个作品内容更新可独立增长 |
| IPC Protocol | `1.0` | Launcher / MotionHost 握手，主版本不兼容拒绝通信 |
| App Compatibility Data | 日期 / 修订号 | 目标 APP 适配规则可单独回归测试与更新 |

Scene ID 不能与展示名称耦合；用户绑定引用 `sceneId`，场景改名后仍然生效。移除场景必须提供迁移/回退或明确的“作品暂不可用”提示。

### 10.4 迁移和升级

1. 读取数据版本并验证；写入前在用户目录生成版本化备份。
2. `N → N+1` 迁移必须幂等；支持异常中断后安全重试。
3. 对高于当前程序支持的 Schema **只读拒绝或安全提示，不覆盖新版数据**。
4. 绑定写入使用临时文件 + Flush + 原子替换（可用时）；避免断电生成半截 JSON。
5. 程序降级可能无法理解更高 Schema；优先恢复备份并提示用户，不进行静默破坏式迁移。
6. 发布包更新应同步验证 Launcher、MotionHost、内置 Scene 的最小兼容版本。

---

## 11. 跨进程 IPC、安全与进程隔离

**[架构推荐]** Launcher ↔ MotionHost 使用当前用户可访问的 **Windows Named Pipe** 或等价受限本机 IPC；不提供对外 TCP 端口。

- IPC 请求字段：`protocolVersion`、`requestId`、`bindingId`、`sceneId`、`mode`、所需交接能力和必要的窗口标识；消息大小、长度、类型、超时均需限制。
- Named Pipe 配置当前用户范围的访问控制；不能仅依赖随机管道名作为鉴权。
- 拒绝任意 shell 命令、脚本路径、动态 DLL 加载请求；MotionHost 只能加载安装包内已登记的可信 Scene 模块。
- 不从未可信源接受窗口句柄作为唯一身份依据；使用时验证句柄仍存在、进程身份一致、没有被复用。
- Studio、Launcher 和 MotionHost 默认以普通用户权限运行，不请求管理员权限、不安装系统级驱动。
- 日志记录状态码、阶段耗时、适配器和版本；敏感路径必要时脱敏，不读取聊天正文、网络令牌或用户屏幕像素。

> 不使用目标程序的 Chrome DevTools Protocol (CDP) 进行 UI 注入。本项目只在**外部呈现启动动效**，这与应用内部换肤项目严格分离。

---

## 12. 可观测性与故障诊断

### 12.1 最小日志字段

`timestampUtc`、`requestId`、`productVersion`、`osBuild`、`adapterId`、`sceneId`、`stage`、`durationMs`、`resultCode`、`fallbackReason`。

日志不记录用户聊天内容、截图、完整命令行中的敏感参数；可从 UI 打开本地日志目录。采用轮换保留和空间上限，默认关闭任何网络上传。

### 12.2 诊断页面

- 受管理快捷方式健康状况。
- 当前 Studio、Launcher、MotionHost 的构建版本和 IPC 协议版本。
- GPU / 视频 / DWM 能力探测结果（仅用于说明兼容原因）。
- 最近启动失败摘要及可复制的脱敏诊断报告。
- 一键恢复受管理快捷方式。

---

## 13. 工程质量、依赖治理与开源发布

### 13.1 依赖治理

- 项目使用 `global.json` 锁定 .NET SDK feature band；集中管理 NuGet 版本，提交锁文件/启用可重复还原策略。
- 禁用未审查的 Preview / Experimental 依赖进入发布分支；新版本必须先在独立升级分支验证。
- 维护 `third-party-licenses.md`、`NOTICE`、素材授权清单；**代码 MIT 不代表角色、视频、音乐和商标素材自动可以再分发**。
- 每次升级 Windows App SDK、Win2D、渲染模块时生成依赖差异、兼容性回归记录并做真实窗口测试。
- GitHub Actions 构建产物提供 SHA-256；正式发行前评估可行的代码签名与 Windows SmartScreen 信誉问题。

### 13.2 建议 CI 阶段

```text
restore → format/lint → build (Release win-x64) → unit tests
        → contract tests → package validation → artifact hash
        → Windows GUI smoke tests（需交互桌面执行器）
        → 分支版本兼容回归 → 发布审批
```

普通无交互 CI 不能替代 Windows 桌面合成、置顶焦点、GPU 视频渲染真机测试。需要为 `WindowsE2E` 准备可用的交互登录会话或专用测试机。

### 13.3 发布通道

- `stable`：正式支持环境、版本锁定、可回滚。
- `preview`：新效果和兼容测试；允许用户主动安装，不自动替换正式版。
- 源码 `main` 与发布 Tag 关联；每个发布说明列出新增场景、最低 Windows、已验证应用版本、已知问题、升级/卸载注意事项。

---

## 14. 测试矩阵与验收用例

### 14.1 测试维度

| 维度 | 必测集合 |
|---|---|
| OS | Windows 11 25H2 x64、26H2 x64；Home/Pro 为首发主要对象 |
| GPU | Intel / NVIDIA / AMD 至少覆盖两种不同图形驱动家族；低功耗设备做降级测试 |
| DPI | 100%、125%、150%、200%，包括双屏不同 DPI |
| 显示 | 单屏 / 双屏、60Hz / 高刷新率、窗口跨屏、任务栏自动隐藏 |
| 应用 | 普通 EXE、原始 `.lnk`、已验证 MSIX/AUMID、后台托盘驻留程序 |
| 启动状态 | 冷启动、已运行、首次启动需登录、进程启动但主窗口迟到、目标不存在 |
| 视觉 | 全屏 Scene、居中 Scene、视频层、Win2D、DWM 成功及回退 |
| 失效 | Esc 跳过、连续双击、GPU 设备丢失、MotionHost 被终止、应用超时 |
| 升级 | 原位更新、失败回滚、配置 Schema 迁移、快捷方式存在/被用户修改 |

### 14.2 发布阻断验收（Release Gates）

- **G1 启动正确性：** 已运行程序不重复启动；连续双击不重复创建动效/目标进程。
- **G2 绑定自由性：** 相同 `sceneId` 绑定 Codex 与微信时，场景实际交接对象是各自的目标窗口。
- **G3 跳过正确性：** 任何视觉阶段 Esc 都能退出；目标应用启动不会取消或重复。
- **G4 降级可靠性：** DWM 或视频失败时仍能启动应用，并清理动画覆盖窗口。
- **G5 快捷方式安全：** 只管理本软件创建的快捷方式；卸载不会删掉用户自行创建或修改的无关文件。
- **G6 升级兼容：** 从上一稳定版升级后绑定、快捷方式、Scene ID 不丢失；未知高版本 Schema 不被旧程序覆盖。
- **G7 性能：** 场景的 GPU、内存占用及相对原生启动的延迟均有真机基线和可接受阈值；发布前再固定门槛。
- **G8 开源合规：** 源码许可证、所有内置素材版权、第三方依赖许可证核查通过。

---

## 15. 开发顺序与 POC 风险门禁

**不建议先做大量 UI，再发现透明 GPU 渲染窗口或 MSIX 启动在目标设备不可行。**

| 阶段 | 交付目标 | 必须通过的门禁 |
|---|---|---|
| POC-A：系统兼容 | .NET 10 + WinUI 3/Windows App SDK 2.6.4 最小工程 | 25H2 / 26H2 正常启动与打包 |
| POC-B：透明宿主 | 全屏与居中 MotionHost；Composition/Win2D/视频演示 | 透明正确、无黑屏、Esc 可退出、跨 DPI 能显示 |
| POC-C：启动器 | Win32 + MSIX/AUMID 两种应用适配；已运行直接激活 | 目标窗口身份可靠、不重复发起启动 |
| POC-D：交接 | Fade 默认、DWM 可选、失败降级 | 不遮挡桌面、不丢窗口、不杀应用 |
| Alpha：功能闭环 | Studio 首页/动效库/绑定/快捷方式/测试启动 | 完成 `应用 → Scene → 一键启用 → 桌面启动` |
| Beta：首发作品 | GPT 龙娘 + 微信两套内部作品 | 高质量视觉，跨应用互绑，全部 G1–G6 通过 |
| Stable：发布 | Windows E2E、诊断、安装升级、许可证 | 全部 Release Gates 验收 |

### 15.1 高风险清单

| 风险 | 级别 | 对策 |
|---|---|---|
| WinUI 3、透明顶层窗口、Win2D、DWM 的实际组合兼容 | 高 | POC-B 先行；MotionHost 允许独立实现替换 |
| 第三方 App 单实例与 MSIX/AUMID 识别 | 高 | Adapter 分层、真实应用矩阵，窗口检测失败也要放行正常启动 |
| 窗口焦点被 Windows 拒绝 | 中高 | 遵守前台规则，采用提醒/用户点击，不全局 Hook 抢焦点 |
| Scene 太依赖 Codex 特定 UI | 高 | 演出与 Handoff 分离，所有 Scene 必须跑跨应用绑定测试 |
| 大型 GPU/视频素材导致冷启动变慢 | 中高 | 原生启动并行、预热上限、按能力降级，建立真实延迟基线 |
| 安装升级后快捷方式失效 | 高 | 稳定安装路径、原子更新、快捷方式健康检测与回滚 |
| 内置角色素材再分发侵权 | 高 | 逐文件来源及许可核查，无授权素材不纳入发行包 |

---

## 16. 版本兼容与变更管理策略（长期维护合同）

### 16.1 四类需要同时管理的版本

1. **运行平台版本**：Windows Build / Windows App SDK / .NET 10 Patch / Win2D / GPU 驱动。
2. **目标应用版本**：微信、Codex 等的安装包身份、进程行为、主窗口行为。
3. **产品组件版本**：Studio / Launcher / MotionHost / IPC / 配置 Schema。
4. **作品资源版本**：Scene Manifest / Scene Module / Asset Bundle / 动效媒体编码。

**不能用“Windows 11 都兼容”或“最新版 App SDK 都兼容”代替这些版本合同。**

### 16.2 产品版本升级规则

- `PATCH`：修复故障、安全更新或兼容性修复，原则上不破坏配置格式。
- `MINOR`：向后兼容地增加效果模块、官方 Scene 或应用适配能力。
- `MAJOR`：可能涉及不兼容 IPC / Scene Schema / 配置升级，需迁移工具和发布说明。
- 依赖升级单独登记为 ADR（Architecture Decision Record），包含旧/新版本、变更理由、回归测试、回滚策略。

### 16.3 跨组件兼容合同（示例）

| Launcher | MotionHost | IPC Major | 结果 |
|---|---|---|---|
| 1.x | 1.x | 1 | 可握手，再检查 Minor capability |
| 1.x | 2.x | 1 ↔ 2 | 不兼容；Launcher 跳过动画继续启动原应用 |
| 2.x | 2.x | 2 | 正常匹配 |

此表为**协议策略示例**，不是宣称已发布这些组件版本。正式产品应严格按照协议握手结果执行，不能靠两个 EXE 文件名相同猜测兼容。

### 16.4 第三方升级监控和修复流程

1. 记录复现所用 OS Build / App 版本 / Adapter / Scene / GPU 及日志事件。
2. 分清是 App 定位失败、主窗口检测失败、渲染失效还是交接失效。
3. 遵守最小修改原则：优先更新适配规则，不修改 Scene 或目标 App 本体。
4. 使用兼容性回归测试验证，不破坏另一 APP 的同一动效绑定。
5. 将新增已知问题与解决方案写入 Release Notes；不能复现时标注为待核实。

---

## 17. 官方资料与参考实现（截至 2026-11-02）

> 下列资料作为**版本与 API 事实依据**；实际开发必须以锁定版本的 Release Notes 与 POC 结果为准。链接为文档资源，不代表仓库已采用对应实现。

1. [.NET 官方支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy) — .NET 10 LTS 支持周期与 servicing 策略。
2. [Windows App SDK 当前稳定下载](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) — 2.6.4 Stable（2026-10-27）；发布前重新检查。
3. [Windows App SDK 2.0 系列发布说明](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0) — 2.6.4 变更。
4. [Windows App SDK 与 Windows SDK 的版本关系](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview) — 编译期与运行期差异。
5. [Windows 11 Home/Pro 生命周期](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro) — 24H2、25H2、26H2 支持期限。
6. [Windows 11 发布与构建信息](https://learn.microsoft.com/en-au/windows/release-health/windows11-release-information) — OS Build 及支持状态。
7. [Unpackaged WinUI 3 发行](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app) — 运行时部署、自包含与 Single-file 限制。
8. [Windows App SDK 部署架构](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview) — 打包/非打包与运行时初始化。
9. [Win2D 官方入门](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/quick-start) — `Microsoft.Graphics.Win2D`、绘制与动画。
10. [Composition 视觉层](https://learn.microsoft.com/en-us/windows/apps/develop/composition/visual-layer) — 视觉层 GPU 合成能力。
11. [DWM Thumbnail Properties](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_thumbnail_properties) — DWM 缩略图位置/透明度边界。
12. [DwmUpdateThumbnailProperties](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmupdatethumbnailproperties) — 缩略图更新 API。
13. [Windows 应用激活 IApplicationActivationManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication) — AUMID 激活机制。
14. [Version Adaptive Apps](https://learn.microsoft.com/en-us/windows/apps/develop/testing/version-adaptive-apps) — API 可用性检查。
15. [Dragon Codex Boot（参考实现）](https://github.com/yushua0808-cpu/dragon-codex-boot) — Windows WPF/DWM 转场及快捷方式管理设计，复用需遵守代码和媒体授权。

---

## 18. 工程实施前的结论

**建议批准的技术决策：**

1. `.NET 10 + WinUI 3` 作为管理客户端主技术栈；只使用稳定、锁定并经过真实桌面测试的 SDK 版本。
2. `Studio / Launcher / MotionHost` 解耦，Loader 只读取受控内置场景与配置。
3. 内置作品支持复杂视频、Composition、Win2D 等混合渲染，但 **MotionHost 透明宿主与 DWM 组合必须先通过 POC**。
4. 以 Windows 11 25H2 / 26H2 x64 为初始正式支持候选，不对过时 Windows 版本作没有测试的承诺。
5. 默认 Fade 交接，DWM 是可选增强；维护者可以制作高度华丽的视觉内容，但目标 App 启动与窗口匹配永远由统一引擎处理。
6. 用户第一版只能绑定完整内置 Scene，不可导入、调参或拼接；底层预留 Sequence/Effect 扩展合同。
7. 产品发布前必须通过**冷/热启动、跨应用互绑、Esc 跳过、异常兜底、安装升级与卸载恢复**的真机测试。

> **一句话：面向用户保持极简，面向开发采用稳定、可测试、可降级的现代 Windows 原生架构。**
