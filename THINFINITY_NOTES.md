# Thinfinity VirtualUI — the delivery surface

**Reference, nothing built.** No VirtualUI code is in the application as at 2026-09-08. `Program.vb`
does not call the SDK, the project references only `Microsoft.Data.SqlClient`, and
`AttachViaThinfinity_Click` still reports that the feature is not configured. This document is what
was learned before writing any of it.

`CLAUDE.md` carries the two delivery rules that apply to every change — prefer click over hover, and
treat files, printing and the clipboard as server-side. This is the detail behind them: the API that
exists, what it is called, and which of our open questions it settles.

Read it before adding the SDK to the project, before changing the Help Desk attachment path, and
before writing anything that assumes a fixed viewport.

---

## 1. The SDK is a source file, not a package

There is no NuGet package. Cybele ship a single file that is **added to the project** and compiled
with it. It P/Invokes `Thinfinity.VirtualUI.DLL`, which the VirtualUI installation provides.

**Use the VB.NET one.** `Thinfinity.VirtualUI.vb`, namespace `Cybele.Thinfinity`. A C# equivalent
exists and is the one every tutorial shows, but this is a VB.NET project and the wrapper is offered
in both. On a machine with VirtualUI installed the file is under:

```
C:\Program Files\Thinfinity\VirtualUI\Dev\dotNet\
```

VirtualUI is **not** installed on the development machine as at 2026-09-08 — the installation is on
the server. A copy of the public VB wrapper was read to write this document, but the file that goes
into the project should come from the actual server install, because versions differ. Section 11
lists what to check when it arrives.

## 2. It is safe to add before the SDK is installed

This is the finding that decides the order of work, and it is not obvious from any tutorial.

`VirtualUILibrary`'s constructor looks up the DLL directory in the registry, trying in turn:

```
HKLM\SOFTWARE\Wow6432Node\Cybele Software\Setups\Thinfinity\VirtualUI\Dev
HKLM\SOFTWARE\Cybele Software\Setups\Thinfinity\VirtualUI\Dev
HKLM\SOFTWARE\Wow6432Node\Cybele Software\Setups\Thinfinity\VirtualUI
HKLM\SOFTWARE\Cybele Software\Setups\Thinfinity\VirtualUI
```

reading `TargetDir_x64` or `TargetDir_x86` by process bitness. An `OEM.ini` beside the executable can
override which key is read.

**When the DLL is not found, nothing fails.** `LibHandle` stays `IntPtr.Zero`, the COM instance is
never created, and every member is written defensively:

```vb
Public Function Start(timeout As Integer) As Boolean
    If m_VirtualUI IsNot Nothing Then
        Return m_VirtualUI.Start(timeout)
    End If
    Return False
End Function
```

Every method no-ops, `Start()` returns `False`, and `Active`, `Enabled` and `StdDialogs` all return
`False`. So the wrapper can go into the project and the `Start()` call into `Program.vb` **now**: on
a machine without VirtualUI the application runs exactly as it does today, on the desktop path, with
no conditional compilation, no `#If` blocks and no build break.

## 3. Startup

One line, and it must come first — before `EnableVisualStyles`, before any window exists:

```vb
Call New Cybele.Thinfinity.VirtualUI().Start()
```

In `000_FRAMEWORK\005_STARTUP\Program.vb` that belongs at the top of `Main`, ahead of
`Application.SetHighDpiMode`.

`Start()` defaults to a 60-second timeout; `Start(timeout)` sets it. `Stop()` ends the session.

Constructing `VirtualUI` also constructs a shared static instance the first time, which is what the
event handlers attach to. Construct it **once**. It is `IDisposable`, and disposing it releases the
COM object.

## 4. Session detection

```vb
Public ReadOnly Property Active() As Boolean      ' in a browser session
Public Property Enabled() As Boolean              ' can be turned off at runtime
Public Property DevMode() As Boolean              ' development server mode
```

`Active` is the one to build on: it is read-only, and it answers the only question the application
actually needs to ask. Per section 2 it returns `False` with no SDK present, which is the correct
answer on a developer's desktop.

**This belongs in one place.** A single owner — a `VirtualUISession` helper holding the instance and
exposing `IsBrowserSession` — not a `New VirtualUI()` at each call site. The Consolidation Guardrail
applies: the moment two pages ask this question independently, there are two answers to keep in step.

## 5. File dialogs — `StdDialogs` settles the attachment question

```vb
Public Property StdDialogs() As Boolean
```

| Value | Behaviour |
|---|---|
| `True` | standard Windows dialogs. In a browser session these browse the **server's** filesystem. Remote upload is disabled. |
| `False` | `OpenFileDialog` becomes VirtualUI's browser **upload** dialog; `SaveFileDialog` becomes its **download** dialog. |

**So the existing `WindowsHelpDeskAttachmentPicker` works in the browser unchanged, with
`StdDialogs = False`.** That is the answer to the question left open when `IHelpDeskAttachmentPicker`
was seamed: the second implementation may not be needed at all. The seam is not wasted — it is still
the right shape if a page ever needs the explicit API of section 6 — but the default path should be
the standard dialog with `StdDialogs` set correctly, not a parallel picker.

Uploads land by default in:

```
C:\Users\Public\Documents\Cybele Software\Thinfinity\VirtualUI\
```

Change that with the `OnGetUploadDir` event, which passes `Directory` and `Handled` by reference.

## 6. Explicit file transfer

```vb
Public Sub UploadFile()
Public Sub UploadFile(ServerDirectory As String)
Public Function UploadFileEx(FileName As String) As Boolean
Public Function UploadFileEx(ServerDirectory As String, FileName As String) As Boolean

Public Sub DownloadFile(LocalFilename As String)
Public Sub DownloadFile(LocalFilename As String, RemoteFilename As String)
Public Sub DownloadFile(LocalFilename As String, RemoteFilename As String, MimeType As String)
```

`UploadFile` works regardless of `StdDialogs`, and does nothing in desktop mode. `DownloadFile`
pushes a server-side file to the user's browser — the missing half of attachments, since saving one
back out is a download, not a file write.

`HTMLDoc.GetSafeUrl(filename, minutes)` gives a time-limited URL for a server file instead, which
suits anything the browser should fetch rather than be handed.

**`UploadFileEx` cannot return the filename in the shipped VB wrapper.** Both the class and the
`IVirtualUI` interface declare the parameter **ByVal**:

```vb
Function UploadFileEx(ServerDirectory As String, FileName As String) As Boolean
```

where the C# wrapper has `out string FileName`. A `ByVal String` cannot carry a value back, so the
name of the uploaded file is lost. Use the `OnUploadEnd(Filename)` event, or declare the parameter
`ByRef` in our copy — and check the installed version first, since this may already be fixed.

## 7. Printing

```vb
Public Sub PrintPdf(FileName As String)
Public Sub PreviewPdf(FileName As String)
```

Both send a PDF to the browser rather than to a server printer. Two `Options` flags go with them:
`OPT_NODEFAULT_PRINTER = 4` and `OPT_SUPRESS_PRINT_DIALOG = 512` (the vendor's spelling).

## 8. Clipboard, pointer, and the rest of `Options`

`Options` is a `UInteger` bitmask:

| Flag | Value | Meaning |
|---|---|---|
| `OPT_APPINVISIBLE` | 1 | |
| `OPT_IGNORE_MOUSEMOVE` | 2 | drop mouse-move entirely |
| `OPT_NODEFAULT_PRINTER` | 4 | |
| `OPT_NOHTML_DRAG` | 16 | |
| `OPT_JSRO_SYNCCALLS` | 32 | |
| `OPT_NOHTML_SIZE` | 64 | |
| `OPT_CLIPBOARD_LOCAL` | 256 | clipboard against the user's machine |
| `OPT_SUPRESS_PRINT_DIALOG` | 512 | |
| `OPT_AUTODOWNLOAD` | 4096 | |

`OPT_IGNORE_MOUSEMOVE` is worth noticing: the vendor ships a switch to discard mouse-move
altogether, which is their own acknowledgement of the cost behind the prefer-click rule. It is not a
switch to set casually — dragging grid columns and rearranging menu tiles both need mouse-move — but
it explains why hover-driven UI degrades.

`ClientSettings` tunes the pointer without going that far: `CursorVisible`, `MouseWheelStepValue`,
`MouseWheelDirection`, `MousePressAsRightButton`, `MouseMoveGestureStyle`, `MouseMoveGestureAction`.

## 9. `BrowserInfo` — who is actually connected

```
Username        IPAddress        UserAgent       UniqueBrowserId
Location        Orientation      ScreenResolution
ViewWidth / ViewHeight    BrowserWidth / Height    ScreenWidth / Height
GetCookie / SetCookie     CustomData     ExtraData / GetExtraDataValue
```

Two uses worth considering when the time comes, neither of them decided: the audit trail records an
actor but not where from, and `IPAddress` with `UniqueBrowserId` would fill that in; and the login
screen could recognise a returning browser. Both are proposals.

## 10. Events

```
OnBrowserResize    OnGetUploadDir    OnUploadEnd    OnDownloadEnd
OnClose            OnReceiveMessage  OnRecorderChanged
```

`OnBrowserResize` matters most to existing code. `HotFieldsPanel` notes that the viewport is fixed
once a session starts (`HotFieldsPanel.vb:290`), and `MainMenu` sizes itself against the session
surface rather than a physical screen (`MainMenu.vb:130`, `MainMenu.vb:182`). If the viewport can in
fact change, this event is where that is noticed — worth testing before relying on either
assumption.

`SendMessage` with `OnReceiveMessage`, plus the `JSObject` and `JSBinding` classes, carry messages
between the application and JavaScript in the page. Nothing here needs that yet.

## 11. What to check against the installed version

The wrapper read for this document came from Cybele's public demo repository, not from the server.
When the real file arrives, confirm:

1. `UploadFileEx` — is the filename parameter `ByRef`, or does section 6's problem stand?
2. The `Options` flag values, which are a bitmask and cheap to get subtly wrong.
3. That `Start()` and the properties still guard on a missing DLL as section 2 describes. Everything
   in this document about being safe to add early depends on that.

## 12. Where the documentation actually is

Recorded because a good deal of time went into finding out:

- **The SDK source is the reference.** Cybele's public repository, `cybelesoft/virtualui`, carries
  both wrappers; the VB one sits under
  `PrinterAgent Demos/RemotePrinterSDK.PrintFile.virtualui/demos/RemotePrinterPrintFileDemoVBNet/`.
- The knowledge-base articles at `kb.cybelesoft.com` cover **deployment**. The useful ones are the
  WinForms integration article and the two on managing and uploading files.
- The v3.6 manual at `cybelesoft.com/manuals/virtualui-3.6/` renders its pages client-side behind
  Cloudflare, so the body text cannot be fetched — only its navigation. Its .NET symbol reference is
  laid out one page per member under `virtualui-3.6-symbols/dotnet/thinfinity/virtualui/`.
- The old `files.cybelesoft.com` PDF guides now redirect to a support landing page, and the
  third-party mirrors of them block automated access.
