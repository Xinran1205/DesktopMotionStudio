param()
$Fixture = Join-Path ([IO.Path]::GetTempPath()) ('dms-f10-proof-' + [Guid]::NewGuid().ToString('N'))
$ErrorActionPreference = 'Stop'
$Fixture = [IO.Path]::GetFullPath($Fixture)
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Fixture))
"ENV OS=$([Environment]::OSVersion.Version) x64=$([Environment]::Is64BitProcess) FS=$($drive.DriveFormat) ROOT=$Fixture"
if ($drive.DriveFormat -ne 'NTFS' -or -not [Environment]::Is64BitProcess) { throw 'This proof needs Windows x64 NTFS.' }
[IO.Directory]::CreateDirectory($Fixture) | Out-Null
Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class DmsF10NativeProof
{
    private const uint GenericRead = 0x80000000, Delete = 0x10000;
    private const uint ReadAttr = 0x80;
    private const uint OpenReparse = 0x00200000, BackupSemantics = 0x02000000;
    private const uint ReadShare = 1, WriteShare = 2, DeleteShare = 4;
    private const uint OpenExisting = 3;
    private const uint Normal = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    public struct FileInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint Volume, SizeHigh, SizeLow, Links, FileIndexHigh, FileIndexLow;
        public string Id { get { return string.Format("{0:X8}:{1:X8}{2:X8}", Volume, FileIndexHigh, FileIndexLow); } }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, IntPtr buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, IntPtr reserved);

    public static SafeFileHandle Open(string path, bool mutate)
    {
        return CheckedOpen(path, GenericRead | (mutate ? Delete : 0), ReadShare, OpenReparse);
    }

    public static SafeFileHandle PinDirectory(string path)
    {
        return CheckedOpen(path, 1, ReadShare | WriteShare, OpenReparse | BackupSemantics);
    }

    private static SafeFileHandle CheckedOpen(string path, uint access, uint share, uint flags)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    public static FileInfo Info(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }

    public static int Rename(SafeFileHandle file, string destination, SafeFileHandle parent = null)
    {
        byte[] chars = Encoding.Unicode.GetBytes(destination + "\0");
        const int fileNameOffset = 20;
        int size = fileNameOffset + chars.Length;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt64(buffer, 0, 0);
            Marshal.WriteIntPtr(buffer, 8, parent == null ? IntPtr.Zero : parent.DangerousGetHandle());
            Marshal.WriteInt32(buffer, 16, chars.Length - 2);
            Marshal.Copy(chars, 0, IntPtr.Add(buffer, fileNameOffset), chars.Length);
            return SetFileInformationByHandle(file, 3, buffer, (uint)size) ? 0 : Marshal.GetLastWin32Error();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static void DisposeFile(SafeFileHandle file)
    {
        IntPtr buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 1);
            if (!SetFileInformationByHandle(file, 4, buffer, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static int TryHardLink(string link, string source)
    {
        return CreateHardLinkW(link, source, IntPtr.Zero) ? 0 : Marshal.GetLastWin32Error();
    }

    public static int TryWriteOpen(string path)
    {
        using var h = CreateFileW(path, 0x40000000, ReadShare | WriteShare | DeleteShare, IntPtr.Zero, OpenExisting, Normal, IntPtr.Zero);
        return h.IsInvalid ? Marshal.GetLastWin32Error() : 0;
    }

    public static int TryDeleteOpen(string path)
    {
        using var h = CreateFileW(path, Delete, ReadShare | WriteShare | DeleteShare, IntPtr.Zero, OpenExisting, Normal, IntPtr.Zero);
        return h.IsInvalid ? Marshal.GetLastWin32Error() : 0;
    }
}
"@
function Assert-Proof([bool]$Condition, [string]$Name) {
  if (-not $Condition) { throw "FAIL $Name" }
  "PASS $Name"
}
$source = Join-Path $Fixture 'draft.tmp'
$destination = Join-Path $Fixture 'owned.lnk'
$unrelated = Join-Path $Fixture 'untouched.lnk'
$copy = Join-Path $Fixture 'copy.lnk'
$aliasPath = Join-Path $Fixture 'alias.lnk'
[IO.File]::WriteAllText($source, 'owned content')
[IO.File]::WriteAllText($destination, 'foreign collision')
[IO.File]::WriteAllText($unrelated, 'unrelated content')
$expectedDigest = (Get-FileHash -LiteralPath $source).Hash
$h = [DmsF10NativeProof]::Open($source, $true)
try {
  $before = [DmsF10NativeProof]::Info($h)
  $collision = [DmsF10NativeProof]::Rename($h, $destination)
  Assert-Proof ($collision -ne 0 -and [IO.File]::ReadAllText($destination) -ceq 'foreign collision' -and [IO.File]::Exists($source)) "no-overwrite rename (native=$collision)"
  [IO.File]::Move($destination, (Join-Path $Fixture 'foreign-preserved.lnk'))
  $publish = [DmsF10NativeProof]::Rename($h, $destination)
  Assert-Proof ($publish -eq 0 -and -not [IO.File]::Exists($source)) "same-volume publication (native=$publish)"
  $after = [DmsF10NativeProof]::Info($h)
  Assert-Proof ($before.Id -eq $after.Id -and $before.Links -eq 1 -and $after.Links -eq 1) "retained file identity on rename ($($after.Id))"
  $write = [DmsF10NativeProof]::TryWriteOpen($destination)
  $delete = [DmsF10NativeProof]::TryDeleteOpen($destination)
  Assert-Proof ($write -eq 32 -and $delete -eq 32) "retained handle blocks concurrent writable and delete opens (write=$write delete=$delete)"
  $createAliasWhilePinned = [DmsF10NativeProof]::TryHardLink($aliasPath, $destination)
  "OBSERVATION concurrent hard-link attempt while mutation handle pinned: native=$createAliasWhilePinned links=$([DmsF10NativeProof]::Info($h).Links)"
} finally { $h.Dispose() }
$reopened = [DmsF10NativeProof]::Open($destination, $false)
try { Assert-Proof ([DmsF10NativeProof]::Info($reopened).Id -eq $before.Id) 'reopened published path is original file' }
finally { $reopened.Dispose() }
[IO.File]::Copy($destination, $copy)
$copyHandle = [DmsF10NativeProof]::Open($copy, $false)
try {
  $copyIdentity = [DmsF10NativeProof]::Info($copyHandle).Id
} finally { $copyHandle.Dispose() }
Assert-Proof ((Get-FileHash -LiteralPath $copy).Hash -eq $expectedDigest -and $copyIdentity -ne $before.Id) 'same-byte copy rejected by file identity'
if (-not [IO.File]::Exists($aliasPath)) {
  Assert-Proof ([DmsF10NativeProof]::TryHardLink($aliasPath, $destination) -eq 0) 'hard-link fixture created'
}
$h = [DmsF10NativeProof]::Open($destination, $true)
try {
  $info = [DmsF10NativeProof]::Info($h)
  Assert-Proof ($info.Id -eq $before.Id -and $info.Links -eq 2) 'hard-link alias rejected by link count'
} finally { $h.Dispose() }
[IO.File]::Delete($aliasPath)
$editor = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
try {
  try {
    $h = [DmsF10NativeProof]::Open($destination, $true)
    $h.Dispose()
    throw 'FAIL existing write handle accepted'
  } catch {
    if ($_.Exception.GetBaseException() -isnot [ComponentModel.Win32Exception] -or $_.Exception.GetBaseException().NativeErrorCode -ne 32) { throw }
    'PASS existing write handle blocks verification/mutation (native=32)'
  }
} finally { $editor.Dispose() }

$h = [DmsF10NativeProof]::Open($destination, $true)
try {
  $info = [DmsF10NativeProof]::Info($h)
  $stream = [IO.FileStream]::new($h, [IO.FileAccess]::Read)
  try {
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
    Assert-Proof ($info.Id -eq $before.Id -and $info.Links -eq 1 -and $digest -eq $expectedDigest) 'ownership verification reads same handle that will mutate'
    $race = [DmsF10NativeProof]::TryDeleteOpen($destination)
    Assert-Proof ($race -eq 32) 'replacement requires incompatible delete access (native=32)'
    [DmsF10NativeProof]::DisposeFile($h)
  } finally { $stream.Dispose() }
} finally { $h.Dispose() }
Assert-Proof (-not [IO.File]::Exists($destination) -and [IO.File]::ReadAllText($unrelated) -ceq 'unrelated content' -and (Get-FileHash -LiteralPath $copy).Hash -eq $expectedDigest) 'delete by verified retained handle only removes its pathname'
$parentPath = Join-Path $Fixture 'pinned'
[IO.Directory]::CreateDirectory($parentPath) | Out-Null
$staging = Join-Path $Fixture 'new-staging.tmp'
[IO.File]::WriteAllText($staging, 'new publication')
$pinned = [DmsF10NativeProof]::PinDirectory($parentPath)
$h = [DmsF10NativeProof]::Open($staging, $true)
try {
  $pinnedFlags = [DmsF10NativeProof]::Info($pinned).Attributes
  $parentDeleteOpen = [DmsF10NativeProof]::TryDeleteOpen($parentPath)
  $relativeRename = [DmsF10NativeProof]::Rename($h, 'bound.lnk', $pinned)
  "OBSERVATION relative rename into retained directory: native=$relativeRename parentAttributes=$pinnedFlags deleteOpenWithoutBackupSemantics=$parentDeleteOpen"
  $publishAbsolute = [DmsF10NativeProof]::Rename($h, (Join-Path $parentPath 'bound.lnk'))
  Assert-Proof ($publishAbsolute -eq 0 -and [IO.File]::Exists((Join-Path $parentPath 'bound.lnk'))) "absolute publication into pinned directory (native=$publishAbsolute)"
  try { [IO.Directory]::Move($parentPath, (Join-Path $Fixture 'redirected')); throw 'FAIL parent path could be renamed' }
  catch [IO.IOException] { 'PASS pinned directory denies path rename' }
} finally { $h.Dispose(); $pinned.Dispose() }

$junction = Join-Path $Fixture 'junction'
New-Item -ItemType Junction -Path $junction -Target $parentPath | Out-Null
$junctionPin = [DmsF10NativeProof]::PinDirectory($junction)
try {
  $junctionAttrs = [DmsF10NativeProof]::Info($junctionPin).Attributes
  Assert-Proof (($junctionAttrs -band 0x410) -eq 0x410) "directory open-reparse returns junction itself (attributes=$junctionAttrs)"
  $junctionChild = [DmsF10NativeProof]::Open((Join-Path $junction 'bound.lnk'), $false)
  try {
    Assert-Proof (([DmsF10NativeProof]::Info($junctionChild).Attributes -band 0x400) -eq 0) 'final child flag does not guard against reparse ancestry'
  } finally { $junctionChild.Dispose() }
} finally { $junctionPin.Dispose() }

$pinned = [DmsF10NativeProof]::PinDirectory($parentPath)
$fixturePin = [DmsF10NativeProof]::PinDirectory($Fixture)
try {
  try { [IO.Directory]::Move($Fixture, ($Fixture + '-race')); throw 'FAIL ancestor path could be renamed' }
  catch [IO.IOException] { 'PASS pinned ancestor denies path rename' }
  $bound = Join-Path $parentPath 'bound.lnk'
  $alias = Join-Path $Fixture 'raced-alias.lnk'
  $boundHandle = [DmsF10NativeProof]::Open($bound, $true)
  try {
    Assert-Proof ([DmsF10NativeProof]::Info($boundHandle).Links -eq 1) 'bound file has one link before race'
    Assert-Proof ([DmsF10NativeProof]::TryHardLink($alias, $bound) -eq 0) 'hard link can race after link-count check'
    [DmsF10NativeProof]::DisposeFile($boundHandle)
  } finally { $boundHandle.Dispose() }
  Assert-Proof (-not [IO.File]::Exists($bound) -and [IO.File]::ReadAllText($alias) -ceq 'new publication') 'raced hard link bytes and pathname survive handle deletion'
} finally { $fixturePin.Dispose(); $pinned.Dispose() }

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

public static class DmsF10ShellLinkProof
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkCom { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath(StringBuilder path, int capacity, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr idList);
        [PreserveSig] int SetIDList(IntPtr idList);
        [PreserveSig] int GetDescription(StringBuilder description, int capacity);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int GetWorkingDirectory(StringBuilder path, int capacity);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        [PreserveSig] int GetArguments(StringBuilder arguments, int capacity);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation(StringBuilder path, int capacity, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStream
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load(IStream stream);
        void Save(IStream stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);
        void GetSizeMax(out long maxSize);
    }

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IStream stream);

    public static byte[] Build(string executable, string args)
    {
        object shellLink = new ShellLinkCom();
        IStream stream = null;
        try
        {
            var link = (IShellLinkW)shellLink;
            Marshal.ThrowExceptionForHR(link.SetPath(executable));
            Marshal.ThrowExceptionForHR(link.SetArguments(args));
            Marshal.ThrowExceptionForHR(link.SetWorkingDirectory(Path.GetDirectoryName(executable)));
            Marshal.ThrowExceptionForHR(link.SetDescription("DMS F10 isolated proof"));
            Marshal.ThrowExceptionForHR(link.SetIconLocation(executable, 0));
            Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(IntPtr.Zero, true, out stream));
            ((IPersistStream)shellLink).Save(stream, false);
            stream.Stat(out var stat, 1);
            if (stat.cbSize <= 0 || stat.cbSize > 65536) throw new InvalidDataException("Unexpected link size.");
            byte[] data = new byte[(int)stat.cbSize];
            stream.Seek(0, 0, IntPtr.Zero);
            stream.Read(data, data.Length, IntPtr.Zero);
            return data;
        }
        finally
        {
            if (stream != null) Marshal.FinalReleaseComObject(stream);
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    public static string[] Read(string path)
    {
        object shellLink = new ShellLinkCom();
        try
        {
            ((IPersistFile)shellLink).Load(path, 0);
            var link = (IShellLinkW)shellLink;
            var target = new StringBuilder(32768);
            var args = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            Marshal.ThrowExceptionForHR(link.GetPath(target, target.Capacity, IntPtr.Zero, 4));
            Marshal.ThrowExceptionForHR(link.GetArguments(args, args.Capacity));
            Marshal.ThrowExceptionForHR(link.GetIconLocation(icon, icon.Capacity, out int index));
            return new[] { target.ToString(), args.ToString(), icon.ToString(), index.ToString() };
        }
        finally { Marshal.FinalReleaseComObject(shellLink); }
    }
}
"@
$shellLinkStage = Join-Path $Fixture 'shell-link-staging.tmp'
$shellLinkFinal = Join-Path $Fixture 'shell-link-published.lnk'
$exe = (Get-Process -Id $PID).Path
$arg = '--binding 71886c20-dd55-40de-9986-47701096fd2c'
$linkBytes = [DmsF10ShellLinkProof]::Build($exe, $arg)
$linkStageWriter = [IO.File]::Open($shellLinkStage, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $linkStageWriter.Write($linkBytes, 0, $linkBytes.Length); $linkStageWriter.Flush($true) }
finally { $linkStageWriter.Dispose() }
$stageLink = [DmsF10ShellLinkProof]::Read($shellLinkStage)
Assert-Proof ($stageLink[0] -eq $exe -and $stageLink[1] -ceq $arg -and $stageLink[2] -eq $exe -and $stageLink[3] -eq '0') "IShellLinkW IPersistStream saves raw bytes readable from .tmp via IPersistFile (bytes=$($linkBytes.Length))"
$stagedHash = (Get-FileHash -LiteralPath $shellLinkStage).Hash
$stageHandle = [DmsF10NativeProof]::Open($shellLinkStage, $true)
try {
  $stageId = [DmsF10NativeProof]::Info($stageHandle).Id
  Assert-Proof ([DmsF10NativeProof]::Rename($stageHandle, $shellLinkFinal) -eq 0 -and [DmsF10NativeProof]::Info($stageHandle).Id -eq $stageId) 'actual staged Shell Link published by retained same-identity handle'
} finally { $stageHandle.Dispose() }
$finalLink = [DmsF10ShellLinkProof]::Read($shellLinkFinal)
Assert-Proof ($finalLink[0] -eq $exe -and $finalLink[1] -ceq $arg -and (Get-FileHash -LiteralPath $shellLinkFinal).Hash -eq $stagedHash) 'published .lnk COM readback preserves raw target and exact binding argument'
