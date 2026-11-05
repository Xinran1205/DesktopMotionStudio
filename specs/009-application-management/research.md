# F08 Research Decisions

## Studio framework

**Decision:** WPF with the pinned .NET Desktop SDK, subject to T001 build/show/close spike.
**Rationale:** Mature XAML/forms/accessibility and shared .NET deployment suit this management UI without another runtime package. Microsoft recommends WinUI 3 for new apps; selecting WPF here is a project tradeoff, not a different statement of that recommendation.
**Alternatives:** WinUI 3 (extra Windows App SDK bootstrap/deployment), WinForms (less suitable for scalable styled management views).
**Sources:** [Microsoft Desktop SDK properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop), [Windows framework guidance](https://learn.microsoft.com/en-us/windows/apps/whats-new/coming-to-windows).

## Typed persistence and migration

**Decision:** Optional explicit target metadata in schema 2; preserve old raw text and project schema 1 only in memory. No write on read/no-op; first changed transaction backs up exact original bytes.
**Rationale:** Schema-1 text was never a typed union; guessing would violate F07. Validation of stored shape must not require installed targets. Dedicated planning review confirmed ID/text/settings preservation, exact future-version classification, elementwise duplicate comparison, and retaining Shortcut identity.
**Limit:** `.bak` rotates on later writes; this is a last-valid-state backup, not permanent downgrade history.

## Local discovery

**Decision:** Start Menu Programs/CommonPrograms .lnk paths plus explicitly verified packaged AppsFolder entries, in an owned STA worker with deadline/output/candidate limits.
**Evidence:** Read-only Windows build 26200 probe found 125 AppsFolder entries, 40 verified packaged entries; ParseApplicationUserModelId + GetPackagesByPackageFamily matched package metadata with zero differences. COM metadata enumeration took about 455ms. No discovered application was launched.
**Sources:** [AppsFolder/AUMID discovery](https://learn.microsoft.com/en-us/windows/configuration/store/find-aumid), [ParseApplicationUserModelId](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-parseapplicationusermodelid), [GetPackagesByPackageFamily](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagesbypackagefamily).

## Read-only shortcut handling

**Decision:** Remove IShellLink.Resolve from F04's reader; read stored path/arguments/working directory without search/tracking/update. Reject UNC/mapped-network locations and every reparse component along normalized local ancestry before file-existence checks or COM reads. Invalid/moved/advertised targets require explicit repair. Reparse-backed local apps may be registered using their actual local path; same-user concurrent filesystem attacks are not an isolation guarantee.
**Rationale:** SLR_NO_UI does not suppress distributed tracking or search. Discovery/registration must not cause network lookup or repair side effects.
**Source:** [IShellLink.Resolve behavior](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinka-resolve).

**Review evidence:** A real local junction fixture reproduced the ancestry bypass before T019 and rejected afterward. Windows GetFileAttributes inspects the final symbolic link itself, allowing each component to be rejected before descending: [Microsoft file attribute behavior](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileattributesw).
