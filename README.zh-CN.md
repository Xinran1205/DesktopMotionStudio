# Desktop Motion Studio

[English](README.md) · **简体中文**

Desktop Motion Studio 可以为 Windows 11 x64 上的应用加一段启动动效。你可以先选一套内置作品，预览并试启动真实应用，满意后再创建桌面快捷方式。关闭 Studio 后，受管快捷方式也可以独立使用。

## 快速上手

1. 运行 `DesktopMotionStudio.Setup.exe`，从 Windows 开始菜单打开 **Desktop Motion Studio**。安装包自带运行时，按当前用户安装。本机候选包为未签名版本。
2. 在「我的应用」添加已安装应用、本地程序或 Windows 快捷方式，然后在作品库选择并绑定动效。
3. 先预览，再按 **Alt+T** 或点「测试启动」打开真实应用。确认效果后可创建桌面入口。
4. 打开「设置 → 界面语言」，选择 **简体中文** 或 **English**，保存后重新打开 Studio。应用名称仍按你保存的内容显示。
5. 在「设置 → 动效显示区域」选择作品推荐、居中小窗、居中大画幅或当前屏幕全屏，保存后下次播放生效。

**零界**是一套约两秒的原创纯科技动效，冰蓝光框会随演出收束。作品库也保留了开发作品 Studio Pulse。冷启动时先播放动效，画面关闭后再启动目标应用。按 **Esc** 可跳过并立即启动。应用已运行时可复用现有窗口，渲染异常时仍会尝试正常打开应用。

安装、快捷键和常见情况见 [中文版使用指南](docs/USER_GUIDE.zh-CN.md)。

## 从源码构建

使用 [global.json](global.json) 固定的 .NET SDK 和 Windows PowerShell。

```powershell
dotnet build .\src\DesktopMotionStudio.Studio -c Release -r win-x64
& .\src\DesktopMotionStudio.Studio\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.Studio.exe
```

构建自包含安装包时，指定一个新输出目录。 [安装与验证指南](specs/013-installer-upgrade/quickstart.md)

```powershell
& .\installer\Build-Setup.ps1 -Version 0.19.0 -OutputDirectory (Join-Path $PWD 'artifacts/local-0.19.0')
```

## 项目开发

项目已配置 Codex 和 PowerShell 版 Spec Kit 工作流。可以用 `specify version` 检查本机 CLI。 [Spec Kit 官方安装说明](https://github.com/github/spec-kit/blob/main/docs/installation.md)

- [功能清单](FEATURES.md)与 [Spec Kit 工程规范](.specify/memory/constitution.md)
- [初始产品需求](Desktop_Motion_Studio_PRD_V1.0.md)与[技术设计](Desktop_Motion_Studio_Technical_Design_V1.0.md)
- [架构决策](docs/DECISIONS.md)与[当前开发断点](docs/RESUME.md)
- [启动动效验收](specs/014-first-licensed-scene/evidence.md)与[双语客户端验收](specs/018-bilingual-client/evidence.md)
- [Studio 隔离检查](specs/009-application-management/quickstart.md)与[桌面入口检查](specs/011-managed-shortcuts/quickstart.md)
