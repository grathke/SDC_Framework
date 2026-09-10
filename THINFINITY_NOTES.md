# Thinfinity VirtualUI — the delivery surface

**The SDK is in, as of 2026-09-10.** `Thinfinity.VirtualUI.vb` sits in
`000_FRAMEWORK\500_INFRASTRUCTURE\Thinfinity\` and `Program.Main` starts a session before anything
is drawn. Everything else here is still reference: `AttachViaThinfinity_Click` reports the feature
is not configured, nothing reads `Active`, and no file dialog has been moved to `StdDialogs`.

What works, measured rather than assumed: the library loads, the session registers, and the
development server starts on 6080. What does not is anything to do with **seeing a page** — both
VirtualUI servers refuse every request, which section 11.3 records and which is a fault inside
their product.

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

**Installed on the development machine on 2026-09-08** (developer install, not the server), so a
change is run and looked at in a browser here before it goes near the server. The local file is
dated 19 December 2025 and is the one to use; section 11 records how it differs from the public copy
this document was first written from.

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

**Running it locally.** With VirtualUI installed on the development machine, a debug run starts the
application on the desktop *and* serves it through the development server at `http://127.0.0.1:6080`
at the same time — the same code, both ways, side by side. That is how a change is checked in a
browser before it reaches the server, and it is the only way to see the delivery assumptions in
section 10 actually behave. `DevMode` and the `DevServer` property control it.

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

`UploadFileEx` returns the uploaded file's name through a `ByRef` parameter, in both the class and
the `IVirtualUI` interface:

```vb
Function UploadFileEx(ServerDirectory As String, ByRef FileName As String) As Boolean
```

The public demo copy declares that parameter **ByVal**, where a returned name cannot come back at
all. The installed December 2025 wrapper has it right. This is the reason to take the file from the
install rather than from anywhere else — and if an upload ever returns `True` with an empty name,
this is the first thing to check.

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

## 11. Checked against the installed wrapper, 2026-09-08

`C:\Program Files\Thinfinity\VirtualUI\Dev\dotNET\Thinfinity.VirtualUI.vb`, 19 December 2025.

1. `UploadFileEx` — **fixed here.** The parameter is `ByRef`. Section 6 is corrected.
2. `Options` — **identical**, value for value.
3. The missing-DLL guards — **identical**. Section 2 stands: `Start()` returns `False` and every
   method no-ops rather than throwing.

## 11.1 The x64 SDK library, and the eight days it was missing

This one is not in any documentation and would be invisible if hit, so it is worth the space.

The wrapper loads its DLL from the registry's `TargetDir_x64` when `IntPtr.Size = 8`, and looks for
this file:

```
C:\Program Files\Thinfinity\VirtualUI\bin64\Thinfinity.VirtualUI.DLL
```

**That file does not exist.** In this install `Thinfinity.VirtualUI.dll` ships only in `bin32`, and
it is the only one of the shipped libraries carrying the `DllGetInstance` entry point the wrapper
resolves. `bin64` holds `Thinfinity.VUILib.dll` and `Thinfinity.VuiExLib.dll`, neither of which
exports it.

So a 64-bit process gets **silence**: `LoadLibrary` fails, `LibHandle` stays zero, and by section 2
every call politely does nothing. `Active` returns `False`, the application runs on the desktop, and
nothing anywhere reports a problem. It would look exactly like an integration that had not been
wired up yet.

The x64 setup of 3.6.1.106 is what is here — the product sits in `C:\Program Files\`, and its
server components in `bin64` are 64-bit. Read from the PE headers and export tables, not inferred
from folder names:

| Library | Arch | Exports |
|---|---|---|
| `bin32\Thinfinity.VirtualUI.dll` | x86 | `DllGetInstance`, `DllCreateObject`, `DllCreateJSObject`, `DllRegisterServer`, `DllAutoRun` … — **the SDK** |
| `bin64\Thinfinity.VUILib.dll` | x64 | `DllNewScraper`, `DllGetScraperId`, `DllSetDevMode`, `DllSetProductId` … |
| `bin64\Thinfinity.VuiExLib.dll` | x64 | the same scraper set |

The 64-bit libraries are not a 64-bit build of the SDK. They are a **different surface** — the
scraper engine behind publishing an application that was never modified.

**It was absent from the installer, and Cybele supplied it on 2026-09-10.** The file now in `bin64`
is 14,257,920 bytes, dated 19 December 2025 like the rest of the product, and was copied in by hand.
A full reinstall of the x64 MSI that same morning did **not** replace it, so it is still not in the
package - it simply is not delivered.

**It is the real thing.** Read from the PE header and export table rather than trusted:

```
Arch   : x64 (PE32+)
Exports: DllGetInstance, DllCreateObject, DllCreateJSObject, DllAutoRun, DllRegisterServer, …
```

and loading it exactly as the wrapper does - registry `TargetDir_x64`, `LoadLibrary`,
`GetProcAddress("DllGetInstance")` - succeeds in a 64-bit process. **So the application stays
AnyCPU.** Everything below about building 32-bit is the reasoning as it stood before the file
arrived, kept because the shape of the problem is worth remembering, not because a choice remains.

Three facts established that it was missing rather than mislocated, and they still hold:

1. Their own installer writes both target directories into the registry, under
   `HKLM\SOFTWARE\Cybele Software\Setups\Thinfinity\VirtualUI\Dev`:
   `TargetDir_x86 = …\bin32` and `TargetDir_x64 = …\bin64`. `GetDLLDir()` in the wrapper picks
   between them on `IntPtr.Size` and appends `\Thinfinity.VirtualUI.DLL`. The x64 path is
   registered; the file at the end of it was never installed.
2. Cybele support, asked directly, replied: "Depending on the platform of your application (32-bit
   or 64-bit), it will look for the corresponding DLL: the 32-bit version in the bin32 folder or the
   64-bit version in the bin64 folder." That is the mechanism above, described as working.
3. **Neither a repair nor a reinstall produces it.** The repair rewrote a single type library
   (`ibin\XpsToPdf.tlb`) and left `bin64` untouched; the reinstall on 2026-09-10 left the
   hand-copied file with its own copy timestamp. The file is absent from the package, not deleted
   from the disk.

So this was never "the SDK is 32-bit only". It was a product that expects a file its installer does
not deliver.

`SDC.Framework.vbproj` sets no `PlatformTarget`, builds AnyCPU and runs 64-bit here.

**Why their documentation never mentions this.** "Compiling and Testing a WinForms Application" in
the 3.6 manual states no bitness requirement at all. The likely reason is that it never used to be
one: its example is a .NET Framework project, and a WinForms executable built AnyCPU in that era had
**Prefer 32-bit** enabled by default, so it ran as a 32-bit process whether or not anyone chose
that. A 64-bit SDK library was never needed. That setting does not exist in .NET 5 and later, where
AnyCPU means 64-bit — so this project is the case their SDK never had to handle, and the guide has
no reason to warn about it.

**The application is not to be built 32-bit — decided 2026-09-08.** 32-bit caps the process at 4GB
and binds every dependency along with it, and none of that is worth paying for a delivery mechanism.
That rules out the `PlatformTarget` fix and leaves two routes, neither yet tested:

1. **Get the 64-bit SDK library from Cybele.** No longer a question of whether one exists — their
   support says the loader looks in `bin64` for it, and their installer registers that path. The ask
   is for the file their package omits. If it arrives, nothing else in this document changes.
2. **Publish through VirtualUI Server without the SDK.** VirtualUI 3.x publishes applications with
   no code modification, and the 64-bit `injectlib` in `bin64` is what makes that possible for a
   64-bit process. This needs **no change to the application at all** — no wrapper, no `Start()`, no
   platform target.

Route 2 costs the SDK surface: no `Active`, so nothing can ask whether it is in a browser session;
no `StdDialogs`, which is what section 5 relies on to make the ordinary file dialogs work; no
`UploadFile`/`DownloadFile`, no `PrintPdf`, no `BrowserInfo`. Some of that may be replaceable by
settings on the server's application profile — that is the thing to find out, because attachments
and printing are the two places this application actually touches the boundary.

## 11.2 Running it on the development machine — 2026-09-09

The developer install is not a bare SDK. It carries the whole server: `Thinfinity.VirtualUI.Server`,
`Broker`, `Gateway`, `SvcMgr`, `WAG`, a web root of 222 files, and a licence. So the app can be seen
in a browser here, without a server elsewhere — which is the point of doing it on this machine.

**Two ports, and they are not interchangeable.** The Manager's General tab shows one binding, 6580.
The configuration also holds `[IIS.BindingsDev] Binding0=…6080`, and 6080 is what the VB.NET
tutorial tells you to open: it is the *Development Server*, where a project run from the IDE appears
inside the Development Lab with a Virtual Path panel and a live jsRO inspector. 6580 is where a
registered application profile is served. Reaching for the wrong one looks like a dead server.

Where things live, none of it obvious:

| What | Where |
|---|---|
| Server configuration | `C:\ProgramData\Cybele Software\Thinfinity\VirtualUI\DB\Thinfinity.VirtualUI.Server.ini` |
| Application profiles | `profiles.bin`, beside it — **not** in the `.ini`, which has no applications section at all |
| Server log | `C:\Users\Public\Documents\Cybele Software\Thinfinity\VirtualUI\Thinfinity.VirtualUI.Server.log` |
| The Manager GUI | `bin64\Thinfinity.VirtualUI.Server.exe /broker` — the same executable as the server |
| The listener | `bin64\Thinfinity.VirtualUI.Server.exe /start`, started by `ThinfinityVUISvcMgr` **into the interactive session as the logged-on user**, not as LocalSystem |

### The URL reservation, and why `+` is not `*`

The server would not start. Its log said only:

```
Thinfinity.VirtualUI.Server.exe  Binding port:6580 with error:5
```

Error 5 is access denied. The port was free — a plain `TcpListener` bound it without complaint — so
the refusal came from `http.sys`, which the server uses and which requires a URL reservation for a
process that is not elevated. The installer creates none.

The trap is which reservation. `http.sys` treats the two wildcards as **different** URLs:

- `+` — the strong wildcard, matches any host name
- `*` — the weak wildcard, used only when nothing else matches

VirtualUI's binding is `*` (Host Name `*`, IP `*` on the General tab), so it asks for
`http://*:6580/`. Reserving `http://+:6580/` changes nothing and looks like the fix failing:

```
netsh http add urlacl url=http://*:6580/ user="<machine>\<user>"
netsh http add urlacl url=http://*:6080/ user="<machine>\<user>"
```

After that the server binds on its own at boot. **This will be needed again on the real server** —
unless it runs elevated or as LocalSystem, where the reservation is unnecessary. Remove one with
`netsh http delete urlacl url=http://*:6580/`.

### Where it stopped

Listening, and answering **503 to every request**, including static files. The web layer is provably
healthy — `netsh http show servicestate` shows the queue with one process attached and eight
registered URLs (`/`, `/__SERVER__/`, `/__BROWSER__/`, `/__CHANNEL__/`, `/__TUNNEL__/`,
`/__PROTO__/`, `/__MESSAGING__/`, `/__WVPN__/`) — and the 503 is VirtualUI's own answer, not the
`http.sys` stock page, since the web root ships no `503.html`.

The unexplained part: Broker, Gateway and TLS Tunnel are ticked as enabled services, and after a
clean boot no process for any of them exists. `SvcMgr` runs as LocalSystem and starts only the
server. Starting a broker by hand did not clear the 503.

Parked there, with Cybele. Four findings, in the order they will care about:

1. `bin64\Thinfinity.VirtualUI.dll` absent, and a repair does not produce it — section 11.1
2. no `http.sys` reservation created for the server's own `*` binding
3. `ThinfinityVUISvcMgr` starts none of its enabled services
4. 503 on every path after a clean boot with a saved, default application profile

### Publishing an application, when it works

Server Manager → **Applications** → **Add**, which opens the Application Profiles Editor:

- **Virtual Path** becomes the URL segment; **Default application** makes it answer at `/`
- **Home Page** blank gives VirtualUI's own view; it is for a custom page wrapping the app
- **Program file name** and **Start in** — set both. This application writes `startup.log` to its
  working directory, which is how a launch is confirmed
- **Resolution: Fit to browser window** is the auto setting, the one the main-menu sizing question
  was parked on
- **Credentials → Use server's account** runs it as the account the server runs as

**The database credentials have to reach that process.** `DataAccess.BuildConnectionString` reads
`SDC_DB_*` from the environment and `run-local.ps1` sets them per-process, so a VirtualUI-launched
exe sees none of them and `Program.vb` exits with "Database Configuration Required" before the login
screen. Set them at **user scope** for the account the server runs as — one place, inherited by
every launch path: F5, `dotnet run`, the exe, VirtualUI, and each project of a multi-project
solution. Machine scope also works and puts the password in the registry for every account on the
box; a launcher script works too, but VirtualUI attaches to the window of the process it starts, so
an intermediate script is a risk that user-scope variables avoid entirely.

## 11.3 The SDK works and the server does not — 2026-09-10

Everything on this side of the boundary works, and was measured rather than assumed:

| Step | Result |
|---|---|
| `LoadLibrary` on `bin64\Thinfinity.VirtualUI.DLL` in a 64-bit process | succeeds |
| `GetProcAddress("DllGetInstance")` | resolves |
| `New VirtualUI()` and `Start(5000)` from `Program.Main` | runs, returns `False` |
| `DevServer.Enabled` / `DevServer.Port` | `True` / `6080` |
| A second `Server.exe /dev` process | started by `Start()`, binds 6080 |
| The application afterwards | opens on the desktop as normal |

`Start()` returning `False` means no browser attached within the timeout, which is expected when
nobody is waiting. `Active` is `False` for the same reason.

### Why no page can be seen

**Both VirtualUI servers register their URLs with `http.sys` and then never service them.** The 503
is not VirtualUI's page - it comes back with `Server: Microsoft-HTTPAPI/2.0`, which is the Windows
kernel HTTP driver answering because nothing collected the request. `netsh http show servicestate`
shows the fault clearly, and shows that the registration side is healthy:

```
Request queue ff00000b10000009   State: Active   processes attached: 1
    8 registered URLs, including HTTP://*:6580/          State: Active
Request queue ff00000c1000005b   State: Active   processes attached: 1
    8 registered URLs, including HTTP://*:6080/          State: Active
```

Attached, active, registered - and silent. This is true of `Server.exe /start` on 6580 and
`Server.exe /dev` on 6080 alike, the second of which their own SDK started.

What has been ruled out, so it is not retried:

- **IIS.** Not installed at all - no `W3SVC`, `WAS` or `IISADMIN`. `http.sys` is a kernel component
  that IIS also happens to use; VirtualUI uses it directly, which is why its config section is
  called `[IIS.Bindings]` and why its URLs appear in `netsh http`. Nothing on 80 or 8080.
- **The URL reservations from 11.2.** They are what lets the server bind at all, and the Windows
  event log records each registration succeeding with `Status: 0x0`.
- **A missing application profile.** `profiles.bin` survived the reinstall and holds the entry,
  marked as the default application.
- **A stale install.** Full reinstall of `Thinfinity_VirtualUI_Legacy_v3.6_Setup_x64.msi` on
  2026-09-10, reported successful by the installer.
- **The licence, as far as can be checked from outside.** Re-applied through the Manager. Worth
  noting anyway: `[license]` holds `serial` and `email` but has no `product=` line, which it did
  have before the credentials were first applied on 2026-09-09, and re-applying did not bring it
  back. A server that has started but does not believe it may serve would behave exactly like this.
- **Their own logging.** `[Logger] Active=true` and `[Broker] LogEnabled=true` produce nothing
  beyond `Starting....` and `Server started. Listening`, even for a request that 503s. The original
  config is kept at `Thinfinity.VirtualUI.Server.ini.backup-before-logging`.

Also still true from 11.2: `ThinfinityVUISvcMgr` runs as LocalSystem and starts none of its enabled
services - no Broker, Gateway or TLS Tunnel process exists at any point.

**Parked with Cybele.** Nothing here is blocked on the application, and nothing further can be
learned from outside their process.

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
