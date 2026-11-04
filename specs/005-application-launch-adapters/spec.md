# Feature Specification: 应用启动适配

**Feature Branch**: `main`
**Created**: 2026-11-03
**Status**: Ready for implementation
**Input**: PRD V1.0, TDD V1.0, FEATURES.md

## User Scenarios & Testing

### User Story 1 - 应用启动适配 (Priority: P1)

用户添加本机 EXE、Windows 快捷方式或已验证的 MSIX/AUMID 应用并可从 Studio/Launcher 发起启动。该 Feature 仅负责启动请求，不负责查找运行窗口或交接动画。

**Independent Test**: 在独立测试环境执行本功能，不依赖后续 UI 或发行功能。

**Acceptance Scenarios**:

1. **Given** 输入与依赖有效，**When** 用户执行本功能，**Then** 合法目标启动一次；失败有可诊断结果且不启动其他程序。
2. **Given** 输入不合法或依赖不可用，**When** 用户执行本功能，**Then** 不破坏现有配置、目标应用或其他用户文件，并给出可诊断结果。

### Edge Cases

- 目标移除、无权限、路径变更、包未安装。
- 并发或重复请求不得产生重复副作用；处理方式在 Plan 中确定。

## Requirements

### Functional Requirements

- **FR-001**: 使用 discriminated launch target 表示绝对 EXE 路径、`.lnk` 路径或 AUMID；不得接受任意 shell command 字符串。
- **FR-002**: EXE 路径须存在且为 `.exe` 文件；启动参数按独立元素传递，不自行拼接 shell 命令；工作目录必须明确或默认为 EXE 所在目录。
- **FR-003**: `.lnk` 必须先解析出有效 EXE 目标及快捷方式参数；快捷方式指向文档、URL、目录或无效目标时拒绝启动。
- **FR-004**: AUMID 必须格式有效并通过 Windows 激活 API 激活；激活失败返回 HRESULT/稳定错误分类。
- **FR-005**: 返回结构化结果（已发起、目标无效、拒绝、系统错误），进程 ID 可选；不保证目标主窗口已出现。
- **FR-006**: 不提权、不修改目标应用、不访问网络；每次请求最多发起一个目标启动。

### Business Rules, Inputs & Outputs

- **Input**: typed launch target、分离参数、可选工作目录。
- **Output**: started/activated result with optional PID, or structured failure code and native error code.
- **Rule**: V1 不加载用户提供的可执行场景代码，也不修改第三方应用文件。

## Success Criteria

### Measurable Outcomes

- **SC-001**: EXE、EXE-targeting `.lnk` 和已安装 AUMID 测试目标均可启动一次。
- **SC-002**: 无效路径、非 EXE 快捷方式、错误 AUMID 和无权限失败均返回明确错误且不启动其他程序。
- **SC-003**: 参数作为独立值保留空格/引号，不被 shell 重新解释。

## Assumptions

- 技术依赖：F01，对应 FEATURES.md 中的前置 Feature。
- 精确性能阈值在首个可运行版本的真机基线后确定，不把未经验证的数值作为发布承诺。

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline; refine before implementation. |
| 0.2.0 | 2026-11-04 | Separate launch request from instance detection; define typed target and safe failure behavior. |
