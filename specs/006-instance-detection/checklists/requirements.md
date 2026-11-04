# Specification quality checklist: 运行实例检测与激活

**Created:** 2026-11-04 · **Feature:** [spec.md](../spec.md)

- [x] Existing-instance and first-launch journeys are independent and testable.
- [x] Exact process identity is required; display names, titles, and basenames cannot cause activation.
- [x] No-window, ambiguous-window, timeout, process-exit, PID reuse, and foreground-denial behavior is defined.
- [x] The wait deadline and concurrent request behavior are bounded and measurable.
- [x] The feature does not terminate target processes or capture user content.
- [x] Dependencies on F02 stable app IDs and F04 launch/identity contracts are explicit.
- [x] Windows API choices and platform limitations have primary-source research.
- [x] Unit/contract and real Windows desktop checks are defined.
