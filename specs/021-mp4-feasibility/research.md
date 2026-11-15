# F20 backend decisions

| Decision | Reason |
|---|---|
| Test Media Foundation Media Engine in frame-server mode | Its own media clock and first-frame event give the probe a native presentation point. The probe additionally requires a successful transfer into a WIC bitmap before showing the stage. |
| Use WIC and WinForms in the probe | An opaque stage can blend the decoded frame against a backdrop. This gives measurable fit, fill and fixed fades without binding the production code renderer to a video API. |
| Generate COM declarations with CsWin32 0.3.369 | The Windows bindings are reproducible and private to the probe build. Preserve HRESULT for `OnVideoStreamTick` to distinguish a new frame from `S_FALSE`. |
| Generate local geometry footage | Square-pixel H.264 test patterns expose aspect-ratio and crop problems. The 6-second horizontal test includes AAC so the probe can verify track detection and mute state. The FFmpeg tool only generates fixtures. |
| Defer the production renderer decision to the integration gate | Full-source WIC copies worked on the workstation. CPU cost, visible timing alignment, hardware variety and audio output require production checks before generalizing the result. |

Sources. [Media Engine creation modes](https://learn.microsoft.com/en-us/windows/win32/api/mfmediaengine/nf-mfmediaengine-imfmediaengineclassfactory-createinstance) · [Frame-transfer behavior](https://learn.microsoft.com/en-us/windows/win32/api/mfmediaengine/nf-mfmediaengine-imfmediaengine-transfervideoframe) · [Media Engine events](https://learn.microsoft.com/en-us/windows/win32/api/mfmediaengine/ne-mfmediaengine-mf_media_engine_event) · [CsWin32](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.369)
