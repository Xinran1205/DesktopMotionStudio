# F19 implementation decisions

| Question | Decision and evidence |
|---|---|
| Where should playback run? | Run it inside the named app gate. The existing process check occurs first and the target gets one dispatch after preparation. The second scan covers independent opens during the new waiting interval. |
| How should the renderer report completion? | Use an explicit pre-launch request so it can run a finite timeline without target handoff authority. Existing launch and preview modes remain valid for their respective callers. |
| Is a longer Studio timeout needed? | The existing Test launch wait is 60 seconds. Its gate, handshake, supported scene duration, renderer cleanup and target window waits fit inside that budget. |
| How can the order be proven? | Record the real target dispatch time, whether the owned renderer has exited, and the count of target processes while the real animation window is visible. Repeat for natural completion and skip. |
