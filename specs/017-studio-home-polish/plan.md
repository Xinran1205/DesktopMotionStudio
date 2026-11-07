# Implementation Plan — F16 Studio 首页与产品图标

**日期**　2026-11-07 · **规格**　[spec.md](spec.md) · **前置验收**　F13

## Summary

用「零界」和 Studio Pulse 的真实数据设计「我的应用」。重排左侧列表和右侧作品区，让用户在当前位置看懂下一步。设计一份多尺寸图标资源，进入 Studio、稳定 Windows 入口及 Setup。沿用原有绑定、启动、快捷方式与安装管线。

## Technical Context

| 项目 | 设计 |
| --- | --- |
| UI | 现有 WPF 和 .NET 10，继续用原生控件、主题资源、可访问名称、焦点与助记键。 |
| 布局 | 最小窗口保留 900×560 DIP。正常已绑定时，标题、封面、作品名、预览及试启动优先可见。维护区可滚动或展开，窄屏与长文字有换行。 |
| 视觉 | 深色稿建立按钮、排版、焦点与留白规则。浅色和高对比度由动态主题处理。保留已有系统偏好。 |
| 图标 | 版本管理一份可编辑源和多尺寸 ICO。Studio 窗口图标与应用资源共源。Studio、稳定 Entry 与 Setup 构建嵌入同一 ICO。 |
| 分发 | 开始菜单和已安装应用指向稳定 Studio Entry。用现有受验证升级事务替换稳定二进制，保持路径、链接、注册与回滚逻辑。 |
| 证据 | Studio/WPF 检查、真实窗口键鼠主题验证、安装路径与图标资源检查、候选包升级／入口冒烟。 |

## Constitution Check

UI 只展示和调用已有能力。配置、绑定、入口所有权、预览与真实试启动的授权边界保持。窗口、Setup 和稳定入口使用本地原创资源。安装更新沿用现有验证，禁止为图标强制重建链接或清理系统缓存。使用本机和可追溯的隔离数据验收，记录实际覆盖。

## Project Structure

| 范围 | 位置与职责 |
| --- | --- |
| F16 设计 | [research.md](research.md)、[data-model.md](data-model.md)、[首页合同](contracts/home.md)、[quickstart.md](quickstart.md)。 |
| 首页 | `src/DesktopMotionStudio.Studio/MainWindow.xaml`、`MainWindow.xaml.cs`、`Styles.xaml`。沿用 `MainWindow.TestLaunch.cs`、`MainWindow.Shortcuts.cs` 的业务边界。 |
| 图标 | `src/Branding/` 存原稿、ICO 和来源。Studio、Entry、Setup 项目引用此资源。保持现有安装脚本与固定目标路径。 |
| 验证 | `tests/DesktopMotionStudio.Studio.Checks/` 验证首页与已存在交互。安装集成沿用 `installer/Build-Setup.ps1` 与 Core 生命周期检查。 |

## Execution Phases

1. **设计第一屏。** 用空、未绑定和已绑定三种真实状态做同一套布局；确定一个醒目主操作与维护区位置。定图标轮廓，同时查看 16 像素。进入代码前检查焦点顺序和长中文。
2. **重排常用路径。** 更新首页 XAML 和样式，绑定真实封面及状态。现有处理程序、助记键和业务规则继续使用。走通空状态到实际试启动。
3. **校验例外与主题。** 用新布局核对停用、作品不可用、启动中切换、入口失效、只读等状态。对浅色／深色／系统／高对比度和 100／125／150% 缩放各至少覆盖一次，按受影响流程组合。
4. **接入图标并验证安装。** 用同一源资源连接窗口、应用和安装入口。生成实际候选包，验证新旧版本安装路径和图标，完成独立审查、收敛与阶段发版检查。

## Complexity Tracking

沿用当前 WPF 外壳和安装链路。页面重排复用现有控件与状态。只有内容布局、样式、图标资产和明确需要调整的可访问入口进入改动范围。
