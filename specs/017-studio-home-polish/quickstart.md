# F16 本机验证

先用空、未绑定、已绑定和失效状态的隔离夹具验证首页。保留现有最小窗口，使用实际 Windows 主题与缩放检查控件触达，完成后恢复原设置。普通改动运行相关的 Studio 检查即可。

```powershell
dotnet run --project tests/DesktopMotionStudio.Studio.Checks -c Release
dotnet run --project tests/DesktopMotionStudio.Studio.Checks -c Release -- --ui
dotnet run --project tests/DesktopMotionStudio.Studio.Checks -c Release -- --f09-ui
```

外部 UI 检查使用构建或发布后的 Studio，按 [F09 验证前置](../010-scene-preview-binding-ui/quickstart.md) 设置测试目标。测试与手动流程各用自己拥有的目录和目标。

1. 在隔离夹具从空首页走到完成绑定和预览。真实 Alt+T 另用 [F15 受控本机路径](../016-test-launch/quickstart.md)，按实际键盘发送，核对打开的进程、冷／热状态和未创建桌面入口。手动或自有夹具确认已有绑定且停用／作品不可用时仍可走原生启动。
2. 核对键盘焦点、长中文名称、最小窗口，以及各主题和缩放。用改变主题或受影响状态的最小组合覆盖，不要求每种主题与缩放全排列。
3. 使用实际的新旧 Setup 候选，沿用 [F12 安装验证](../013-installer-upgrade/quickstart.md) 和 Core 的 `--f12-built-lifecycle <旧包目录> <新包目录>`，验证稳定入口换版及回滚。阶段候选只需一次真实安装路径冒烟。升级后启动原有测试入口，核对设置与绑定保留。按实际文件、开始菜单、任务栏和已安装应用核对图标，记录系统缓存是否影响显示。
4. 将截图、真实试启动、安装日志和覆盖限制记录在 `evidence.md`。阶段发布步骤见 [路线](../../docs/NEXT_STAGE.zh-CN.md)。
