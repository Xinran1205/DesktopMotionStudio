# Tasks — F13「零界」与完整启动演出

**输入**　[规格](spec.md) · [计划](plan.md) · [播放合同](contracts/playback.md) · [验证步骤](quickstart.md)

## Phase 1 — Setup

- [ ] T001 在 `specs/014-first-licensed-scene/evidence.md` 记录 0.15.0 旧作品、配置与真实启动基线，准备隔离发布目录、受管入口和两种测试目标。

## Phase 2 — Foundational

- [ ] T002 在 `specs/014-first-licensed-scene/poc/` 做短分镜与窗口时序小样。记录居中／单屏全屏、提前／迟到、Esc、淡出时长与减少动态效果。通过 FR-004 的构图比较和无阻塞退场后再进入正式实现。

## Phase 3 — US1 完整预览（P1）

- [ ] T003 [US1] 在 `tests/DesktopMotionStudio.Core.Checks/F09Checks.cs` 和 `tests/DesktopMotionStudio.MotionHost.Checks/Program.cs` 添加新旧运行时、未知版本拒绝、三个分镜与两种画幅的检查（FR-001、FR-002、FR-004）。
- [ ] T004 [US1] 在 `src/DesktopMotionStudio.Studio/Scenes/zero-boundary/` 添加约 2000 毫秒作品、原创新封面与来源。扩展 `src/DesktopMotionStudio.Core/Scenes/ScenePlaybackDefinition.cs` 白名单及 `src/DesktopMotionStudio.MotionHost/SceneSurface.cs` 的分发，保留 `vector-orbit` v1（FR-001、FR-002、FR-007）。
- [ ] T005 [US1] 在 `src/DesktopMotionStudio.MotionHost/MotionHostWindow.cs` 实现新作品的无常驻文字舞台、指针和键盘退出、静态／极简画面。让 `src/DesktopMotionStudio.Studio/SceneUiText.cs` 和 `PreviewPresenter.cs` 呈现模拟交接说明，结束时恢复到预览触发处。验证同一画面源与单屏取景（FR-002、FR-003、FR-004、FR-006，SC-001）。

## Phase 4 — US2 启动与交接（P1）

- [ ] T006 [US2] 在 `tests/DesktopMotionStudio.Core.Checks/F06Checks.cs`、`F09PlaybackProcessChecks.cs` 与自有窗口夹具添加版本错配、早到、迟到、Skip／Completed／Handoff 竞态、故障降级与一次分派的覆盖（FR-005、FR-006、FR-007）。
- [ ] T007 [US2] 按合同更新 `src/DesktopMotionStudio.Core/MotionHost/MotionHostContracts.cs`、`MotionHostFrameCodec.cs`、`WindowsMotionHostProcessFactory.cs` 与 `MotionHostLifecycleCoordinator.cs`，实现有限演出自然结束及目标先就绪时的关闭。匹配更新 `src/DesktopMotionStudio.MotionHost/HostProtocolClient.cs` 和 `MotionHostWindow.cs`（FR-003、FR-005、FR-006）。
- [ ] T008 [US2] 核对 `src/DesktopMotionStudio.Launcher/Program.cs` 对 Completed、跳过、失败及超时的真实结果，验证 F15 试启动与受管入口共用分派、旧 Studio Pulse 继续可用（FR-005、FR-006、FR-007）。
- [ ] T009 [US2] 执行 `specs/014-first-licensed-scene/quickstart.md` 的发布目录与本机验证。跨两个测试目标记录冷启动、暖复用、早／迟到、真实输入退出、失败与减少动态效果。比较旧配置与入口并留证（SC-002、SC-003、SC-004）。

## Phase 5 — Review and Converge

- [ ] T010 在 `specs/014-first-licensed-scene/review.md` 独立审查绘制、时序、版本、退出清理和素材来源。逐条收敛验收，登记实际覆盖、更新 `FEATURES.md`，提交后进入 F16。

## Dependencies & Execution Order

T001 → T002 → US1 → US2 → T010。可预览作品是第一个可验收增量，完整 F13 还需真实启动、旧作品回归和收敛。尺寸比较仅用于确定作品推荐画幅。
