# F13 本机验证

1. 使用当前 Windows 11 x64 工作站与固定 SDK。从隔离测试目录构建与发布。用会延迟创建窗口的自有测试目标覆盖宿主先显示、窗口先就绪和 2.5 秒后就绪；用第二个不同应用覆盖跨应用。受管入口只写隔离测试桌面。
2. 运行下列已有检查，加入新作品、自然结束、协议版本、并发终态、实际宿主关闭与旧作品保持的用例。新用例随 T003、T006 落地。

```powershell
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f09-checks
dotnet run --project tests/DesktopMotionStudio.MotionHost.Checks -c Release
dotnet run --project tests/DesktopMotionStudio.Studio.Checks -c Release
```

3. 真机小样对比居中和全屏，记录作品可见时间及就绪到关闭时间。用 Esc、鼠标退出及键盘焦点退出。另做一次实际 Windows 减少动态效果检查并恢复原值。
4. 使用发布目录与自有目标验证早到、晚到、已运行、跳过、资源损坏和宿主故障。走一次真实试启动与一次受管入口，并复测 Studio Pulse。遵循 [F15 受控真实启动](../016-test-launch/quickstart.md)。UI 隔离参数有意禁用真实试启动，用单独的受控本机步骤覆盖真实启动，不放开测试参数的边界。
5. 记录两种画幅证据、推荐尺寸、对比选择、故障和发布目录检查结果至 `evidence.md`。任何只有模拟证据的行为标明覆盖范围。

运行小样时，在交互式 Windows 会话执行以下命令。绘制图和日志可放在忽略的 `artifacts` 目录。将 `--check` 及输出目录换成 `--demo centered` 或 `--demo full` 可只看舞台。

```powershell
dotnet run --project specs/014-first-licensed-scene/poc -c Release -- --check (Join-Path (Get-Location) 'artifacts/f13/poc')
```
