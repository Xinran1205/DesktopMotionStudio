# Library / binding contract

- Catalog source is the trusted installation root; isolated test roots are explicit development inputs. No user-import scanning.
- Root/ancestor/sidecar/asset/cover validation happens before target reads; reject UNC/network, reparse components, traversal, absolute paths, URI/ADS and escaping paths. Safe relative assets remain within the selected scene.
- Base `scene.json` remains schema1. Optional presentation1 failure is fallback; required playback1 failure makes Preview/new binding unavailable. Future schema/runtime is not interpreted. Required playback may be listed in assets; optional artwork must not be.
- Reader limits: 256 directories, sidecar64KiB/depth8, snapshot4KiB, <=8 keyframes, cover4MiB/4096² with display decode<=512px. Text/numeric/identity limits are in data-model.md. Invalid members/duplicates reject the affected sidecar; other valid scenes continue.
- Enumerate direct directories only up to entry257 before reading sidecars or sorting content. Entry257 returns root-level TooManyScenes and no partial usable library. Never Take(256) then validate duplicates: the omitted entry may conflict with an earlier ID. UI retains home/retry. Tests cover exactly256,257 and a duplicate ID in entry257.
- UI presents name/cover/description/style/theme/availability; empty/error states retain home navigation. Theme is informational. No import, effect knobs, test launch or shortcut action.
- Transaction-time eligibility uses the current IsPlayable result, not an old row. Bind/replace/toggle/resolve reuse F07 with a default-compatible optional predicate. Existing unavailable bindings remain stored. Same-binding no-op stays unchanged and current availability remains visible.
- Preview/Replace on home select the current scene/app by ID. Success is shown after committed state refresh; post-commit read failure retains recovery status. Covers load only validated bounded bytes, with OnLoad/frozen images; bad cover yields fallback.
