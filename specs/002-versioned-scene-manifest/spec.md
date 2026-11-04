# Feature Specification: 版本化场景清单

**Feature Branch**: `main`
**Created**: 2026-11-03
**Status**: Converged
**Input**: PRD V1.0, TDD V1.0, FEATURES.md

## User Scenarios & Testing

### User Story 1 - 版本化场景清单 (Priority: P1)

维护者添加内置作品，用户能看到稳定的作品标识与兼容信息。F01 只定义和验证清单；资源读取与目录扫描属于 F03。

**Independent Test**: 在独立测试环境执行本功能，不依赖后续 UI 或发行功能。

**Acceptance Scenarios**:

1. **Given** 输入与依赖有效，**When** 用户执行本功能，**Then** 有效清单可读取；无效/未来版本返回明确错误且不执行资源。
2. **Given** 输入不合法或依赖不可用，**When** 用户执行本功能，**Then** 不破坏现有配置、目标应用或其他用户文件，并给出可诊断结果。

### Edge Cases

- 空目录、重复 ID、损坏 JSON、未来版本。
- 并发或重复请求不得产生重复副作用；处理方式在 Plan 中确定。

## Requirements

### Functional Requirements

- **FR-001**: JSON 清单含 `schemaVersion=1`、稳定 `sceneId`、SemVer `sceneVersion`、非空展示名称、`displayMode` (`full`/`centered`) 和资源相对路径列表。
- **FR-002**: `sceneId` 仅允许 3–64 位小写拉丁字母、数字及中划线，并以字母/数字开头；资源路径不得为绝对路径、含 `..` 段或重复。
- **FR-003**: 高于当前支持的 `schemaVersion` 必须以明确的 `UnsupportedSchema` 结果拒绝；损坏 JSON、缺字段及无效值返回不同错误，不修改输入。
- **FR-004**: 清单大小上限 1 MiB；解析不得执行清单内代码、访问网络或文件系统。
- **FR-005**: 解析结果是不可变的场景元数据，展示名称不作为绑定身份。

### Business Rules, Inputs & Outputs

- **Input**: UTF-8 JSON 字节流或文本，最大 1 MiB。
- **Output**: 有效的不可变 `SceneManifest` 或结构化错误码；本 Feature 无持久化副作用。
- **Rule**: V1 不加载用户提供的可执行场景代码，也不修改第三方应用文件。

## Success Criteria

### Measurable Outcomes

- **SC-001**: 有效清单可读取；无效、过大及未来版本返回明确错误且不执行资源。
- **SC-002**: 有效、损坏、缺字段、未来版本、路径穿越、重复资源、大小边界等场景均有自动化测试。

## Assumptions

- 技术依赖：F00，对应 FEATURES.md 中的前置 Feature。
- 精确性能阈值在首个可运行版本的真机基线后确定，不把未经验证的数值作为发布承诺。
- V1 场景清单只描述可信内置资源；运行时能力判定属于 F03/F06。

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline; refine before implementation. |
| 0.2.0 | 2026-11-03 | Fix schema fields, validation limits, and error contract for implementation. |
