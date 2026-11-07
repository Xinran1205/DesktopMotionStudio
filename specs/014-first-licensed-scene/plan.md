# Implementation Plan — F13「零界」与完整启动演出

**日期**　2026-11-07 · **规格**　[spec.md](spec.md) · **基线**　`6466123`

## Summary

先用一个短小样验证构图、早到交接和迟到收尾。再以独立作品资源、一个受控原生渲染器和同一预览／启动绘制路径进入产品。保持 F15 测试启动和 F10 受管入口走同一个 Launcher。F13 在现有 Studio 界面验收。

## Technical Context

| 项目 | 设计 |
| --- | --- |
| 平台 | Windows 11 x64 本机工作站，现有 .NET 10、WPF Studio、独立 WinForms MotionHost。 |
| 画面 | 原创矢量形状、短光带、两层框架及收束。无外部媒体与网络依赖，按时间归一化取景并共用绘制。 |
| 内容边界 | 先保持 manifest 1、展示／播放 sidecar schema 1 及 profile 2。新增编译在 MotionHost 的受控运行时；使用同一数据形状。 |
| 启动交接 | 目标就绪事件触发原生短淡出。正常播完、跳过、故障与超时分别记录。现有 IPC2 只允许预览正常播完。计划将 Launch 的正常结束扩展为显式 IPC3 合同，并验证版本不匹配时原生启动。 |
| 时间预算 | 创作约 2 秒，正常作品从首帧到退出含收尾不超过 2.5 秒，已有可见窗口收到就绪后的 300 毫秒关闭基线继续适用。硬性 watchdog 独立于作品时长。 |
| 证据 | Core 协议／进程、MotionHost 绘制、发布目录内容检查、真实 Windows 工作站；具体路径见 [quickstart.md](quickstart.md)。 |

## Constitution Check

先保证目标启动并保持单次分派。作品可绑定任意受支持应用。运行时按白名单与版本识别，渲染进程与目标之间无进程句柄、窗口像素或启动参数共享。跳过和故障仅结束演出。保持用户配置、旧作品和第三方文件，检查包内原创来源。所有路径用独立可复查的测试记录，满足后再进入 F16。

## Project Structure

| 范围 | 位置与职责 |
| --- | --- |
| F13 设计 | 本目录的 [research.md](research.md)、[data-model.md](data-model.md)、[播放合同](contracts/playback.md)、[quickstart.md](quickstart.md)。 |
| 作品包 | `src/DesktopMotionStudio.Studio/Scenes/zero-boundary/`，含清单、展示、播放、封面和来源记录。 |
| 播放 | `src/DesktopMotionStudio.MotionHost/SceneSurface.cs`、`MotionHostWindow.cs`、`HostProtocolClient.cs`，新增小型独立画面实现。 |
| 校验与协调 | `src/DesktopMotionStudio.Core/Scenes/ScenePlaybackDefinition.cs` 与 `src/DesktopMotionStudio.Core/MotionHost/`。Launcher 沿用启动分派，核对结果语义。 |
| Studio 预览 | `src/DesktopMotionStudio.Studio/PreviewPresenter.cs`、`SceneUiText.cs` 和现有预览入口。 |
| 验证 | `tests/DesktopMotionStudio.MotionHost.Checks/`、`tests/DesktopMotionStudio.Core.Checks/`、`tests/DesktopMotionStudio.Studio.Checks/` 及可控窗口夹具。 |

## Execution Phases

1. **分镜和真机小样。** 先记录旧作品与基础启动表现，再用同一时间轴展示两种画幅。核对时序、退出控件及真实窗口提前／迟到行为。记录屏幕、缩放、每种画幅和时间测量点。未证明短演出与无阻塞退场时，先修正小样。
2. **可单独预览的作品。** 交付原创新封面和编排，接入目录与统一绘制。预览正常结束、停止与 Esc 有自动及真机证据。比较画幅使用临时测试请求，正式入口使用作品推荐值。
3. **生产交接。** 新版本协议让新作品可以正常结束并与就绪、跳过、故障竞争，只产生一个终态。验证已就绪后不会迟到显示新遮罩。沿用 F06 隔离和 F15/F10 分派。支持减少动态效果。完整行为见 [播放合同](contracts/playback.md)。
4. **验收收敛。** 真机覆盖两个不同应用、现有 Studio Pulse 绑定、渲染故障、缩放与辅助退出。记录视觉与计时证据、授权来源、兼容范围，完成独立审查并提交。

### 小样分镜预算

| 时间 | 画面 |
| --- | --- |
| 0–0.35 秒 | 从石墨黑中扫出细光与主轮廓。 |
| 0.35–1.2 秒 | 两层错位边框归位，四角短亮一次。 |
| 1.2–2 秒 | 边框收束并柔和退场。若真实窗口先就绪，从当前帧提前交接。 |

这是预览与迟到用例的编排参考。进入快应用时无需等待三个镜头播齐。时间点以小样定稿和真机测量为准。

## Complexity Tracking

协议升级只用于可验证的正常启动演出终态，保留严格版本检查。演出编排不需要媒体解码器、新持久化格式或目标窗口融合能力。Studio Pulse 的运行时仍由原实现绘制。
