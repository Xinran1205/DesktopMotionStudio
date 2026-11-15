# Feature Specification: 第二套授权内置场景

**Feature Branch**: `main`
**Created**: 2026-11-03
**Status**: Draft baseline
**Input**: PRD V1.0 §3, §6, §11; TDD V1.0 §14–15

## User Scenarios & Testing

### User Story 1 - 选择第二套独立作品 (Priority: P1)

用户可在动效库选择与首套作品不同的完整作品，并绑定任意受支持应用。

**Independent Test**: 将第二作品分别绑定两个测试应用，预览并执行冷启动与已运行启动。

**Acceptance Scenarios**:

1. **Given** 两个有效目标应用，**When** 分别绑定第二作品，**Then** 场景按作品预设形式播放，并交接各自正确的目标窗口。
2. **Given** 媒体缺失或设备不支持高级效果，**When** 启动，**Then** 安全降级且真实应用继续启动。

### Edge Cases

- 作品资源缺失、静音偏好、减少动态效果偏好、DPI/多屏布局、目标窗口迟到。
- 用户快速跳过或同时启动两个绑定。

## Requirements

### Functional Requirements

- **FR-001**: 第二作品必须有独立稳定 `sceneId`、版本、封面、说明及完整固定编排。
- **FR-002**: 场景不得硬编码最初创作对象为实际启动目标。
- **FR-003**: 逐项登记并核查角色、商标、音乐、视频和其他素材的再分发权。
- **FR-004**: 跳过、故障与不兼容高级效果时必须复用通用安全交接。

### Business Rules, Inputs & Outputs

- **Input**: 有效绑定、内置场景资源、运行时能力及用户全局静音/减少动态效果偏好。
- **Output**: 完整演出或安全降级结果，真实应用启动结果，脱敏诊断状态。
- **Rule**: 作品主题可以指向某个应用，但跨应用绑定不受主题限制。

## Success Criteria

### Measurable Outcomes

- **SC-001**: 第二作品可在至少两个不同受支持应用上正确交接，且已运行场景不重复启动。
- **SC-002**: 所有随包资源具有可核查授权记录；故障/跳过无持续遮挡。
- **SC-003**: Windows 交互桌面完成冷/热启动、DPI、静音和跳过验收。

## Assumptions

- 技术依赖：F13 作品管线与 F03/F06/F07 合同。
- 具体创意主题、素材和性能预算在该 Feature 开始前以授权和真机原型确定。

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Add second scene required for intended V1 launch scope. |
