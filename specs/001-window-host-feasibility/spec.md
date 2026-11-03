# Feature Specification: Windows 动效宿主可行性

**Feature Branch**: `main`  
**Created**: 2026-11-03  
**Status**: In progress  
**Input**: PRD V1.0 §5–6; TDD V1.0 §8.2, §15

## User Scenarios & Testing

### User Story 1 - 安全展示与退出 (Priority: P1)

作为后续启动流程的使用者，我希望演出宿主能显示在桌面上并随时关闭，避免遮挡真实应用。

**Independent Test**: 单独运行宿主，不启动任何目标应用；在全屏与居中模式按 Esc 或点击关闭，并检查窗口和进程退出。

**Acceptance Scenarios**:

1. **Given** 正常交互桌面，**When** 以全屏模式启动，**Then** 宿主覆盖目标显示器的工作区域，显示明确的原型画面和关闭入口。
2. **Given** 宿主正在显示，**When** 按 Esc 或点击关闭，**Then** 窗口关闭、进程正常退出，不残留顶层遮挡。
3. **Given** 居中模式，**When** 启动宿主，**Then** 窗口在目标显示器工作区域居中且完整可见。

### User Story 2 - 兼容及异常边界 (Priority: P2)

作为工程维护者，我需要知道透明顶层窗口和多屏 DPI 的实测表现，以便确定 MotionHost 实现方案。

**Independent Test**: 在 Windows 11 交互桌面切换显示器/DPI，检查透明边缘、焦点、任务栏、退出和进程清理。

**Acceptance Scenarios**:

1. **Given** 指定显示器有效，**When** 宿主启动，**Then** 窗口出现在该显示器的工作区域。
2. **Given** 指定显示器无效，**When** 宿主启动，**Then** 使用主显示器并给出可诊断提示。
3. **Given** 缺少交互桌面或窗口创建失败，**When** 启动宿主，**Then** 非零退出且不留下顶层窗口。

### Edge Cases

- 显示器在运行中断开；窗口应关闭或移回有效显示器。
- DPI 为 100% 至 200%；窗口几何应按实际显示器像素计算。
- 参数非法、重复快速启动、焦点被系统拒绝、RDP/VM 无 GPU。

## Requirements

### Functional Requirements

- **FR-001**: 接受 `full` 或 `centered` 模式及可选显示器索引；无效输入以明确错误退出。
- **FR-002**: 窗口显示可辨识原型内容、关闭按钮、Esc 退出，不启动其他应用。
- **FR-003**: 窗口应保持在普通桌面顶层，但不得使用全局 Hook、注入或管理员权限。
- **FR-004**: 透明区域应允许看到桌面，不出现持续黑底；实际合成可靠性必须通过交互桌面验收。
- **FR-005**: 退出时销毁窗口并释放 GDI/Win32 资源。
- **FR-006**: 结果必须区分静态/自动构建通过与真实桌面验证通过。

### Inputs & Outputs

- **Input**: 模式、显示器索引、用户键盘/鼠标退出动作。
- **Output**: 顶层原型窗口与退出码；无用户配置写入。

## Success Criteria

### Measurable Outcomes

- **SC-001**: 两种模式均可在目标 Windows 11 x64 上启动；Esc 与关闭按钮均能使窗口在 2 秒内消失。
- **SC-002**: 在本机至少两种不同 DPI 缩放和双屏布局中，居中窗口完整可见，无残留遮挡；100%/150%/200% 发布兼容矩阵继续由后续产品验收覆盖。
- **SC-003**: 非法参数以非零码退出且无窗口残留。
- **SC-004**: 实测报告记录 OS build、显示器/DPI、成功与失败、所选后续宿主架构。

## Assumptions

- 本 Feature 是隔离的最小可运行原型；正式作品、视频、粒子、DWM 缩略图和应用交接属于后续 Feature。
- F00 允许先用纯 Win32 宿主验证窗口行为；Studio 的 WinUI 3 技术选择独立保留。
- 机器有 Windows 11 交互桌面；无交互会话不能声称视觉验收通过。

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Define bounded Windows host feasibility gate. |
| 0.1.1 | 2026-11-03 | Make F00 feasibility gate use two real DPI scales; retain full DPI matrix for release validation. |
