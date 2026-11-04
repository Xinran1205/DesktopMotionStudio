# Feature Specification: 本地配置存储

**Feature Branch**: `main`
**Created**: 2026-11-03
**Status**: Converged
**Input**: PRD V1.0, TDD V1.0, FEATURES.md

## User Scenarios & Testing

### User Story 1 - 本地配置存储 (Priority: P1)

用户配置应用与绑定后，重启软件仍能安全恢复。V1 存储首版不含迁移历史；未来 Schema 变更再新增显式迁移。

**Independent Test**: 在独立测试环境执行本功能，不依赖后续 UI 或发行功能。

**Acceptance Scenarios**:

1. **Given** 输入与依赖有效，**When** 用户执行本功能，**Then** 保存后重载字段一致；失败时上一份有效数据可恢复。
2. **Given** 输入不合法或依赖不可用，**When** 用户执行本功能，**Then** 不破坏现有配置、目标应用或其他用户文件，并给出可诊断结果。

### Edge Cases

- 磁盘满、断电、权限不足、损坏文件。
- 并发或重复请求不得产生重复副作用；处理方式在 Plan 中确定。

## Requirements

### Functional Requirements

- **FR-001**: 保存 `schemaVersion=1` 的用户状态，包含应用档案、应用与场景绑定及主题/静音设置；空白安装加载默认值。
- **FR-002**: 应用 ID 与绑定 ID 唯一；每个应用最多一个绑定；绑定只能引用存在的 appId，场景 ID 使用 F01 合法规则；字段长度和枚举值必须验证。
- **FR-003**: 数据保存于 `%LOCALAPPDATA%/DesktopMotionStudio/state/studio-state.json`；写入临时文件、flush 后在同卷替换，保留最近一次有效备份。
- **FR-004**: 超过 4 MiB 的数据、损坏 JSON、无效值和未知高版本返回明确错误，不覆盖原文件；将来的迁移必须在单独版本任务实现。
- **FR-005**: 仓库不启动应用、不创建快捷方式、不访问网络；不保存令牌或屏幕内容。

### Business Rules, Inputs & Outputs

- **Input**: 由 Studio 提交的应用档案、绑定和设置；磁盘上的版本化 JSON。
- **Output**: 不可变状态快照或稳定错误码；保存失败时保留之前有效文件。
- **Rule**: V1 不加载用户提供的可执行场景代码，也不修改第三方应用文件。

## Success Criteria

### Measurable Outcomes

- **SC-001**: 空安装返回默认状态；保存后重新加载逐字段一致。
- **SC-002**: 损坏和未来版本读取被拒绝且输入字节不变；保存成功以旧状态备份。
- **SC-003**: 大小限制、唯一性、引用完整性和枚举校验均由自动化测试覆盖。

## Assumptions

- 技术依赖：F01，对应 FEATURES.md 中的前置 Feature。
- 首版不存在历史用户配置，故不虚构 v0 迁移格式；后续 Schema 演进必须增加带备份、幂等性的迁移。
- 精确性能阈值在首个可运行版本的真机基线后确定，不把未经验证的数值作为发布承诺。

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline; refine before implementation. |
| 0.2.0 | 2026-11-04 | Define schema, ownership, atomic replacement, validation, backup, and error behavior. |
