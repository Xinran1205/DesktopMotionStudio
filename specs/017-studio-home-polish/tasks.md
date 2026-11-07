# Tasks — F16 Studio 首页与产品图标

**输入**　[规格](spec.md) · [计划](plan.md) · [首页合同](contracts/home.md) · [验证步骤](quickstart.md)

## Phase 1 — Setup

- [ ] T001 在 `specs/017-studio-home-polish/evidence.md` 记录 F13 交付、现有首页和 0.15.0 安装路径。准备测试配置、两套作品封面和新旧候选安装验证环境。

## Phase 2 — Foundational

- [ ] T002 在 `specs/017-studio-home-polish/design/` 产出最小窗口下的空、未绑定、已绑定首页稿，以及 16 像素图标方案。核对文字、焦点顺序和设置／诊断／入口管理的位置（FR-001、FR-002、FR-004、FR-005）。

## Phase 3 — US1 首页常用路径（P1）

- [ ] T003 [US1] 在 `tests/DesktopMotionStudio.Studio.Checks/F15TestLaunchChecks.cs` 与 `F09ViewChecks.cs` 覆盖主操作和当前作品状态、预览无分派、选择切换及测试启动无入口副作用（FR-001、FR-003）。
- [ ] T004 [US1] 在 `src/DesktopMotionStudio.Studio/MainWindow.xaml`、`MainWindow.xaml.cs` 和 `Styles.xaml` 重排应用、封面、绑定与试启动层级，沿用现有处理程序。走一遍隔离首页预览与受控真实试启动（FR-001、FR-002、FR-003，SC-001）。

## Phase 4 — US2 状态、键盘与主题（P1）

- [ ] T005 [US2] 在 `tests/DesktopMotionStudio.Studio.Checks/F09UiChecks.cs` 及相关检查保留快捷键和可访问名称。覆盖长文本、不可用／停用、入口恢复、无结果和进行中状态，确认未放开真实启动权限（FR-002、FR-003、FR-004）。
- [ ] T006 [US2] 在 `src/DesktopMotionStudio.Studio/MainWindow.xaml` 和 `Styles.xaml` 完善状态、焦点和主题样式。按 `quickstart.md` 验证实际键盘、900×560 DIP、100／125／150% 和四种主题路径（FR-002、FR-003、FR-004，SC-002）。

## Phase 5 — US3 图标与升级（P2）

- [ ] T007 [US3] 在 `src/Branding/` 保存原创图标源、ICO 和来源，接入 Studio 窗口与 Studio、Entry、Setup 三个项目文件。核对安装注册与开始菜单仍引用稳定路径，第三方目标图标规则保持（FR-005、FR-006）。
- [ ] T008 [US3] 使用 `installer/Build-Setup.ps1` 生成实际候选包。沿用 `tests/DesktopMotionStudio.Core.Checks/F12InstallChecks.cs` 验证 0.15.0 升级与回滚，实际查看 Windows 图标和已有受管入口（FR-005、FR-006，SC-003）。

## Phase 6 — Review and Converge

- [ ] T009 在 `specs/017-studio-home-polish/review.md` 做独立审查与收敛，记录完整阶段回归和候选包冒烟。验收后更新 `docs/USER_GUIDE.zh-CN.md`、`FEATURES.md` 与 `docs/RESUME.md`，提交可交付版本。

## Dependencies & Execution Order

F13 通过后执行 T001 → T002 → US1 → US2 → US3 → T009。US1 是第一个可独立体验增量。图标的外部可见性和升级验证在实际 Setup 构建之后。
