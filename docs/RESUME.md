# Desktop Motion Studio — 暂停与恢复断点

## 当前断点 — F12 实现完成，待干净账户验收

当前分支 `codex/f12-installer-upgrade`，以 F11 完成提交 `0e4dcc9` 为基础。[F12 清单](../specs/013-installer-upgrade/tasks.md)的 T001–T008 已完成。自包含 Setup 0.12.3 和 0.12.4 已生成，两份实际负载在隔离 Windows 注册范围内通过安装、回滚、升级、卸载、重装。全量 Core 522 通过，Studio 67 通过。详情见 [F12 证据](../specs/013-installer-upgrade/evidence.md)。

下一步在干净、离线 Windows 11 x64 标准用户环境执行 T009–T011，再完成 T012 收敛并进入 F13。当前主机账户属于 Administrators，使用非提升令牌且有私有 .NET SDK，Windows Sandbox 和本地 VM 工具不可用。测试候选包和验收步骤见 [F12 验证指南](../specs/013-installer-upgrade/quickstart.md)。

## F11 已完成

F11 的 11 项任务已完成并通过独立审查和收敛。425 项 Core、67 项 Studio、25 项发布版键盘与 Windows Shell 验收通过。唯一跳过的是未配置 AUMID 的可选打包应用用例。Windows 配色已恢复到测试前状态。验证详情见 [F11 证据](../specs/012-settings-diagnostics/evidence.md)。

开发发布目录为 `artifacts/studio-f11`。21 个文件和 11 份最终日志已核对 SHA256，清单在 `artifacts/checkpoints/f11-20261106`。

## F10 已完成

2026-11-06 完成 F10 的 15 项任务。通过 395 项 Core、57 项 Studio 和 12 项发布版实际 UI 与 Windows Shell 检查。隔离入口已验证创建、修复、移除、关闭 Studio 后启动、换作品与原生降级。独立审查与 Converge 已完成。可复现说明和验证证据见 [F10 证据](../specs/011-managed-shortcuts/evidence.md)。

本地验证产物在 `artifacts/studio-f10`，校验清单与日志在 `artifacts/checkpoints/f10-20261106`。F10 分支为 `codex/f10-managed-shortcuts`。后续从 F11 设置与本地诊断开始，先完善独立规格。

## 上次暂停记录 — F09

**保存时间：** 2026-11-06，Asia/Shanghai  
**项目状态：** 按用户要求暂停，明天继续；无待审批事项。  
**分支：** `codex/f09-scene-preview-binding`  
**实现检查点：** `8182ecf` — `feat(f09): deliver verified Studio library preview and binding workflows`

## 当前完成位置

F00–F09 已完成。F09（作品库、预览与绑定 UI）31/31 任务完成，需求检查清单16/16通过；Spec/Plan/Tasks 一致性分析、独立审查与 Converge 已完成，无未关闭的 F09 必需工作。参见 [功能清单](../FEATURES.md)、[F09 进度](../specs/010-scene-preview-binding-ui/progress.md)、[验证证据](../specs/010-scene-preview-binding-ui/evidence.md)和[审查记录](../specs/010-scene-preview-binding-ui/review.md)。

已实现：作品库与安全封面/详情、无需启动目标应用的预览、Stop/Esc、已有应用选择与绑定/替换、主页联动，以及预览顺序控制、独立超时和子进程清理。profile schema2 / manifest1 保持兼容。预览与实际启动使用同一绘制实现。

**F10 尚未开始实施。** `specs/011-managed-shortcuts/spec.md` 只有初始独立规格，尚需完善，未生成正式 Plan/Tasks。当前 Spec Kit feature 上下文仍为 F09，恢复时按工作流切换。

## 已验证结果与限制

| 验证范围 | 结果 |
|---|---|
| 完整 Core 回归 | 321 PASS / 0 FAIL / 1 明确 SKIP |
| Studio 自动检查 | 53 PASS / 0 FAIL |
| 生产绘制/窗口检查 | 10 PASS / 0 FAIL |
| 最终真实播放/进程检查 | 15 PASS / 0 FAIL |
| F08 外部 UI 回归 | 16 PASS / 0 FAIL |
| 最终发布目录 F09 外部 UI | 16 PASS / 0 FAIL |
| 发布目录内置作品目录检查 | 2 PASS |
| Release 构建、分析器、格式检查 | 通过；相关构建0警告/0错误 |

实际桌面验证覆盖100%/125%/150%缩放，以及 Windows 动画效果关/开。原始设置 **125% / 动画效果开** 已恢复。真实场景交接240ms；真实渲染 UI 挂起由父进程1500ms期限触发，1518ms完成清理；真实崩溃清理11ms。

完整321/53/10检查点后仅加强了测试中挂起线程的身份校验，生产代码未变；修改后15项真实播放/进程检查通过。唯一 SKIP 是可选 packaged-instance 集成，原因是 `DMS_F05_TEST_AUMID` 未设置，不能将其视为通过或全 Windows 兼容性证明。

当前产物为依赖 .NET10 Windows Desktop x64 runtime 的开发发布目录。安装/升级/卸载仍是 F12；有授权的正式作品仍是 F13/F14。当前内置作品明确标记为开发内容，不能宣称最终产品已交付。

## 已保留的本地产物

- 已验证发布目录：`D:\GitHub_Repo\DesktopMotionStudio\artifacts\studio-f09`。
- 断点证据目录：`D:\GitHub_Repo\DesktopMotionStudio\artifacts\checkpoints\f09-20261106`。
- 发布目录全部19个文件已与最终实测源目录逐一比对 SHA256；清单为证据目录中的 `studio-f09.sha256`。
- 64份 `dms-f09*.log` 已从临时目录复制并核对哈希；包括最终结果、实际 UI、缩放、系统动画偏好以及历史 red/green 日志。`results.json` 记录最终关键日志的 PASS/FAIL/SKIP 计数。
- `artifacts/` 为本地保留目录，已加入 `.gitignore`；换机需重新构建。规格、代码、验证结论和恢复说明均由 Git 跟踪。
- 保存时未发现运行中的 DesktopMotionStudio/DMS 测试或宿主进程。

当前工作站 SDK：`C:\Users\AAA\.dotnet\dotnet.exe`，SDK10.0.403、runtime10.0.14；版本约束见 `global.json`。设置 `$env:DOTNET_ROOT='C:\Users\AAA\.dotnet'`，避免系统默认 dotnet 的版本差异。需要测试时使用独立 `--state-file` / `--catalog-root`，不触碰真实配置。具体命令见 F09 quickstart。

## 明天的恢复顺序

1. 读取本文件，检查 `git status --short`、当前分支和最新提交；保留用户可能新增的改动。
2. 阅读 Constitution、FEATURES.md、F09最终证据与 F10 基线；无需重做已完成的 F09。
3. 为 F10 建立独立 Specify → Clarify → Plan → Tasks → Analyze 产物，记录合理默认值和技术决策。
4. 优先验证 Windows 快捷方式所有权、冲突与失败恢复风险。不得根据文件名推定所有权，不覆盖用户快捷方式；修复/禁用/删除前重新验证身份和范围。
5. 风险验证通过后逐项 Implement → Test → 独立 Review → Converge；F10完成并通过质量检查后再进入 F11。

用户恢复口令可直接使用“继续工作”。暂停期间不启动下一 Feature，也不安排后台开发或提醒。
