# Feature Specification: 内置场景库

**Feature Branch**: `main`
**Created**: 2026-11-03
**Status**: Converged
**Input**: PRD V1.0, TDD V1.0, FEATURES.md

## User Scenarios & Testing

### User Story 1 - 内置场景库 (Priority: P1)

用户浏览所有随软件提供的完整作品。Catalog 只扫描安装包登记目录的一层子目录，读清单并核对资源存在性；不得从用户可写目录加载可执行内容。

**Independent Test**: 在独立测试环境执行本功能，不依赖后续 UI 或发行功能。

**Acceptance Scenarios**:

1. **Given** 输入与依赖有效，**When** 用户执行本功能，**Then** 有效作品可列举；损坏作品隔离并提示。
2. **Given** 输入不合法或依赖不可用，**When** 用户执行本功能，**Then** 不破坏现有配置、目标应用或其他用户文件，并给出可诊断结果。

### Edge Cases

- 缺失封面、重复作品、损坏资源。
- 并发或重复请求不得产生重复副作用；处理方式在 Plan 中确定。

## Requirements

### Functional Requirements

- **FR-001**: 从指定只读安装根目录枚举直接子目录，每个场景目录读取 `scene.json` 并用 F01 解析。
- **FR-002**: 清单引用的每个资源必须存在、是普通文件且规范化路径保持在该场景目录内；不跟随 reparse point/symlink。
- **FR-003**: 重复 `sceneId` 的所有条目均标为无效；损坏或缺失清单只影响对应条目并返回诊断码。
- **FR-004**: 只读、不启动场景代码、不扫描用户导入目录、不访问网络；场景目录按名称稳定排序。

### Business Rules, Inputs & Outputs

- **Input**: 由应用安装器确定的场景根目录。
- **Output**: 按目录排序的有效清单和逐目录诊断项，不修改输入文件。
- **Rule**: V1 不加载用户提供的可执行场景代码，也不修改第三方应用文件。

## Success Criteria

### Measurable Outcomes

- **SC-001**: 有效场景及资源返回一次且顺序稳定；缺失资源和清单产生定位到场景目录的诊断。
- **SC-002**: 所有重复 ID 的场景均不作为可用作品返回；不访问场景目录以外的文件。
- **SC-003**: 异常目录不会中止其他有效场景扫描，也不会执行清单中任何内容。

## Assumptions

- 技术依赖：F01，对应 FEATURES.md 中的前置 Feature。
- 精确性能阈值在首个可运行版本的真机基线后确定，不把未经验证的数值作为发布承诺。

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline; refine before implementation. |
| 0.2.0 | 2026-11-04 | Define trusted root, direct-folder layout, resource containment, duplicate IDs, and diagnostics. |
