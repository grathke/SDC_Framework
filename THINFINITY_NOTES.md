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

**No file filter reaches the browser (Glenn, 2026-09-24).** VirtualUI's upload dialog shows every
file whatever `OpenFileDialog.Filter` says, and `UploadFileEx` takes no filter at all. A page that
wants only certain files - the employee import wants CSV or JSON - checks the extension after the
upload, as `ImportSourceFormat` does, and says so plainly. Do not re-propose the filter as a fix.

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
surface rather than a physical screen (`FW_MainMenu.vb:130`, `FW_MainMenu.vb:182`). If the viewport can in
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

**Corrected 2026-09-11: it arrives with the Server environment.** This section first recorded the
file as absent from the installer and supplied by hand. That was wrong, and the correction matters
because the original reading was on its way to Cybele as a defect report.

The file is 14,257,920 bytes and dated 19 December 2025 like the rest of the product. Its
**creation** time on disk is 2026-09-10 08:20:50 - the same second as
`Thinfinity.VirtualUI.Settings.dll` beside it, and both differ from every other library in `bin64`,
which carries 19 December. Two files appearing together to the second is an installer writing them,
not somebody copying one in. What happened that morning was installing **both** environments,
Developer and Server, where only the Developer one had been installed before.

So the rule is: **a Developer-only install does not put the x64 SDK library in `bin64`; installing
the Server environment as well does.** Whether that is intended or an omission in the Developer
package is Cybele's to say, and not worth asking - installing both is the answer either way, and
the Server environment is what serves the application in a browser.

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

## 11.4 It was the URL reservations all along — 2026-09-11

**Fixed.** `http://localhost:6580/` returns 200 and serves VirtualUI's page. Nothing was wrong with
Cybele's software, and nothing was wrong with the licence, the profile, the install or the broker.

The cause is the fix from 11.2 turned inside out. Four reservations existed:

```
http://+:6580/   NUCBOX_EVO-T1\Glenn
http://*:6580/   NUCBOX_EVO-T1\Glenn
http://+:6080/   NUCBOX_EVO-T1\Glenn
http://*:6080/   \Everyone          delegate=yes
```

With those in place `http.sys` accepted connections on 6580 and 6080 and answered **503 to every
request without delivering it to the registered queue**. Delete all four and both ports deliver.
Add back a single `http://*:6580/` for the account the server runs as, and it both binds and
serves. The same for 6080.

**What proved it was not VirtualUI.** A plain .NET `HttpListener` - Microsoft's own code, nothing
of Cybele's involved - was registered on `http://*:6580/` with zero Thinfinity processes running
after a reboot. Requests to it came back 503 and the listener never received them, while another
`http.sys` application on port 8029 answered 200. `httperr` logged each one as
`503 - N/A`, so the refusal was the kernel's, not any user-mode server's.

Probing five ports at once is what located it: 6580 and 6080 refused delivery, 6581, 7580 and 9099
all delivered. A fault that follows two specific port numbers and nothing else points at
configuration attached to those numbers, which is what the reservations are.

**The rule to keep.** One reservation per port, the weak wildcard, granted to the account the
server runs as:

```
netsh http add urlacl url=http://*:6580/ user="<machine>\<user>"
netsh http add urlacl url=http://*:6080/ user="<machine>\<user>"
```

Do not add the `+` form as well - VirtualUI binds `*`, and the strong wildcard is a different URL
that it never asks for. Do not grant to `Everyone`, and do not set `delegate=yes`. Which of the
three extras did the damage was not isolated, because the working set is the minimum set and there
is no reason to reintroduce any of them.

This also retires findings 2, 3 and 4 of 11.2 as things to raise with Cybele. Finding 1 stands and
is theirs: `bin64\Thinfinity.VirtualUI.dll` is still absent from the installer.

## 11.5 Running it, day to day — 2026-09-11

Everything below was measured on this machine after 11.4 fixed the reservations.

### Two ports, two purposes

| | 6580, the published profile | 6080, the development server |
|---|---|---|
| started by | `ThinfinityVUISvcMgr`, always up | `Start()` in the application itself |
| reached at | `http://localhost:6580/` then the **SDC Framework** tile | `http://localhost:6080/` |
| what launches the exe | the server, from the profile | the run script |
| profile settings - resolution, on-close | **applied** | **ignored** |
| use it for | how the application looks and scales | how the application behaves |

The split is worth keeping straight, because a sizing question answered on 6080 is answered
wrongly: dev mode shows the window at actual size with the page letterboxed around it, whatever
the profile says. `run TF` is for exercising behaviour, the tile on 6580 for judging appearance.

`run TF` also flashes "can't reach this page" on the way up: the SDK opens the browser before its
own development server has bound 6080, and the page recovers on retry. Cosmetic, dev-mode only.

### What the application does with the SDK

All of it in `Program.StartVirtualUI`, and all of it conditional on there being a session:

- **`DevMode` only for a run we started** - `run-with-db.ps1` passes `--tf-dev`; the process the
  server launches gets neither switch. Forced on, `Start()` never returned in a server-launched
  session: dev mode stands up its own server and waits for a browser on 6080 while the session
  that launched it waits at 6580, so the browser sat on "Initializing..." with the login screen
  never built.
- **`OPT_APPINVISIBLE`, after `Start()` succeeds** - otherwise the application appears twice, once
  in the tab and once on the desktop. After, not before: `Start()` returns False when nothing
  attached and the application then falls back to the desktop, where invisible means no interface.
- **`OPT_NOHTML_DRAG`** - see below.
- **`OnClose` ends the process** - `Application.Exit`, then a forced exit three seconds later,
  because Exit will not return while a modal dialog is up and a dead session has nobody to dismiss
  one.

### Dragging

VirtualUI treats a drag in the browser as an HTML5 drag, which is how a file is dragged from the
user's machine into the application - and it swallows the mouse-down, move and up that a drag
*inside* the application needs. Ribbon tiles did nothing at all in a browser until
`OPT_NOHTML_DRAG` turned that off. Grid column reordering is the same gesture and the same fix.

What it costs: `OnDragFile` no longer fires, so a file cannot be dragged in from the desktop.
Nothing accepts a dropped file, and **file transfer here is a picker and a click, not a drag** -
decided 2026-09-11, on the same reasoning as click-over-hover. `Options` is a runtime property, so
a page that ever wants a drop target can clear the flag and restore it; build that as a scope that
cannot leak rather than a pair of calls, because a page that exits oddly would otherwise leave the
whole application unable to rearrange tiles, and nobody would connect the two.

A drag inside a **server-side** dialog works, since that dialog is part of the streamed window.
Whether Thinfinity's own HTML file dialog is affected is untested.

Dragging also needed to look like dragging. A `FlowLayoutPanel` owns its children's positions, so
a tile only jumps between slots - snappy on a desktop, but coalesced mouse-moves make it sit still
and then teleport. `RibbonTileArrangementController` now draws a faded copy of the tile under the
pointer for the duration.

### Closing, and what does not detect it

Closing the browser ends the process, and **it now takes about four and a half seconds.**
Measured 2026-09-21 by closing the tab and watching `SDC.Framework.exe` leave Task Manager, with
`OnClose` reaching the application in the same moment.

**The setting is Reconnection timeout**, on the Application Profiles Editor's General tab, and the
SDC Framework profile reads **5 seconds**. Cybele support described it on 2026-09-21: a grace in
seconds that starts *after* the browser disconnects, during which the process is kept alive for
the same session to be reconnected to. Zero terminates the application the moment the browser goes
away; raise it to survive a page refresh or a brief network drop. Nothing else is involved -
`Thinfinity.VirtualUI.Server.ini` holds no keepalive, ping or interval key at all.

**The three-minute wait this section used to describe is gone, and why is not recoverable.**
It was measured twice on 2026-09-11, at 156 seconds and at about three and a half minutes, and was
taken to be a fixed grace nothing could reach. The profile value was never read at the time, so
whether it then held a large number, or something else entirely was happening, cannot now be
established. **Do not plan around three minutes.** A closed tab releases the exe in seconds, which
is what a `CLOSE THE APP` wait should now assume.

The consequence to keep in mind is that **the shutdown window is the Reconnection timeout, and
nothing else.** It is five seconds here because that is what the profile says; change the profile
and the application's entire budget for shutting down changes with it, silently, from outside the
codebase. That is why `Program.VirtualUISessionClosed` writes the session end and flushes
telemetry as its first two statements rather than relying on the message loop unwinding: the
forced-exit timer behind them is set for three seconds, which at the current five leaves under two
seconds of margin before VirtualUI takes the process down itself.

**Never set it to 0.** Zero terminates the process the instant the browser disconnects, which is
before the close handler can write anything - the `FW_Session` row would be left open and swept
later as a `Crash`, and queued telemetry would be lost with it. Raising it is safe in both
directions: it buys a user the chance to survive a page refresh, and it widens the shutdown budget
at the same time. **Anything below about four seconds starts eating into the three-second timer
and should be treated as a change to the application, not to the server.** Anything added to that handler has to fit in front
of that, and the log bears it out - a closed tab on 2026-09-21 produced
`VirtualUI session closed - exiting` and no line after it, neither `Main end` nor
`forcing exit`.

**Proved on 2026-09-21, not assumed.** A Thinfinity login closed by shutting the tab wrote
`FW_Session` row 1041 with `EndReason = 'Disconnect'` and a connected time of 16 seconds - the
write beat the kill, and the end is near-exact rather than late by minutes. `sql/151` used to warn
that a Disconnect end was approximate; it no longer is.

**There is no user-inactivity timer anywhere in VirtualUI.** A session lives as long as the browser
tab holds it open, whether or not anybody is typing or clicking, and no tab of the profile editor
offers such a setting. Sessions that drop while idle come from outside it: a reverse proxy, load
balancer or WAF closing an idle WebSocket at 60-300 seconds, or an RDS session policy. Neither
applies here. Nothing therefore has to keep a session awake, and a control that repaints to look
busy would be the worst way to attempt it - every repaint streams pixels to the browser for no
functional gain.

`Active` does **not** help. It stayed `True` for that entire period with no browser attached, so a
poll on it never counts down. A watch built on it was removed the same day: a safety net that
cannot fire reads like cover that is not there. `Program.InBrowserSession` is therefore set once
from `Start()`'s answer - it means "this process belongs to a session", not "somebody is looking".

Exiting normally - Cancel at the login screen - leaves the tab showing VirtualUI's own
"application closed" page. That is the server's behaviour; the profile can redirect instead.

### Counting who is connected

**There is no API for it.** Confirmed by Cybele support on 2026-09-21: the .NET SDK is scoped to
the session its own process is running in - it exposes that session's information and lifecycle
events rather than server-wide counters - and the published REST API covers administrative objects
rather than live session counts. Their recommended approach is to count it in the application
instead, since one VirtualUI session is one process: a row per session in your own database, or
`Process.GetProcessesByName` on the server, which is accurate while one process means one session
and covers only the local server under a load balancer.

`FW_Session` is the first of those and `SessionTracking.CloseAbandonedSessions`, reconciling on
`ProcessID`, is the second - both built before the question was asked. See
`sql/151_create_session.sql`. The one piece of that advice deliberately not taken is a per-session
heartbeat for robustness against crashes, and the same file says why: the process keeps running
through the disconnect grace with no browser attached, the heartbeat keeps beating, and it proves
nothing that `ProcessID` does not prove better.

### Sizing

**The shell is one size in a browser, and the browser scales it.** With *Resolution: Fit to browser
window* on the profile, resizing the tab enlarges the whole canvas in proportion and nothing
re-lays out. Letting the window resize instead leaves the pinned tiles anchored to a right edge
that has moved, and a gap opens across the middle of the ribbon.

So in a session the main menu hides both window boxes and pins `MaximumSize` to its opening size.
Both boxes, because Windows will not hide one alone - a form with a maximise box and no minimise
box draws minimise greyed rather than absent. `MaximumSize` as well as the box, because
double-clicking the caption or dragging the window to the top of the session also maximises.
Minimising is the one to be rid of: inside a tab there is no taskbar to bring the window back
from.

**Superseded 2026-09-16: nothing is resizable by dragging any more, anywhere.** This section used to
end "pages are a different question and were left alone - a browse grid genuinely wants more width,
so `_B` and `_U` are better resizable than scaled", and the desktop was left sizable. Both reversed:
nearly every run is a session, and a window dragged wider fights both its own layout and the zoom.

Every form is now `FixedDialog` with no maximise box, the menu included and on the desktop too. Size
changes come from code only: F8/F9/F10 through `PageZoom`, and Hot Fields widening a browse page.
A browse page zooms by pausing `LayoutQbeSection` while the factor is not 1.0 and scaling a snapshot
of its settled layout instead - the two cannot run together, or the right-anchored buttons walk off
to the right on every press. The desktop keeps a minimise box, which is harmless there.

## 11.6 The console account — **planned, not applied** — 2026-09-12

`TF_Console` exists as a local account. **Nothing uses it, and nothing should be changed to use it
while the server works.** This section is the plan on the shelf, not a description of the running
system.

### Why a console account at all

11.2 records the fact that drives it: `ThinfinityVUISvcMgr` starts the listener **into the
interactive session as the logged-on user**, not as LocalSystem. A server with nobody logged in has
no VirtualUI server. On a real server that means somebody's session has to be up permanently, which
is an account that auto-logs on at the console and is never signed out.

### Why it was not applied

The server works. Moving it to a different identity means deleting and re-adding the URL
reservations and moving the `SDC_DB_*` environment variables to another profile — the exact ground
that cost 11.2 through 11.4. Deferred on 2026-09-12 for that reason, which is the right call: a
working server is worth more than a tidier account.

`TF_Console` was created on that day with `New-LocalUser` from an elevated prompt. **It may have
been added to Administrators before the reasoning below was worked out — check before assuming
not.** Nothing runs as it either way, so removing that membership cannot affect Thinfinity.

### It must not be an administrator

This follows from 11.4 rather than from general caution. The whole purpose of the `urlacl` is to let
a **non-elevated** process bind `http.sys`. Grant the reservation to the account and it needs no
elevation, so the one argument for admin rights is already answered. Against it: an auto-logon
account has a recoverable password stored on the machine, and the session sits signed in
permanently on a box reachable from a browser.

What it does need:

- membership in **Users**, and nothing further
- the two URL reservations, weak wildcard, granted to it by name
- read/execute on the application folder, and write to the profile's `Start in` folder, or
  `startup.log` never appears and a failed launch looks like a failed server

### Auto logon stores a password — put it in LSA secrets

`AutoAdminLogon` under `Winlogon` keeps `DefaultPassword` in **plaintext**, readable by anything
running as an administrator. Sysinternals Autologon writes the same configuration but puts the
password in LSA secrets:

```
Autologon64.exe TF_Console <machine> <password> /accepteula
```

Still recoverable by a local administrator — which is the second reason the account is not one.

### Two accounts, not one

| | the console account | the administrator |
|---|---|---|
| signs in | automatically, at the console, at boot | over RDP, when needed |
| rights | Users | Administrators |
| owns | the VirtualUI server session and the application | nothing that must keep running |

They cannot be the same account, because auto logon would then leave an administrator's password on
the machine.

On **Windows 11 Pro** there is one interactive session, so connecting over RDP as the administrator
**disconnects** the console session. Disconnect is not sign-out: the server and the running
application survive, and only the desktop is unreachable, which does not matter when browser users
arrive on 6580. Windows Server allows two sessions and the cost disappears.

### The two things keyed to the account's identity

This is what makes an identity change expensive, and neither failure names the account:

- the URL reservations — wrong account gives **503 on every path**, the fault of 11.4
- `SDC_DB_CONNECTION` or the `SDC_DB_*` variables, at that profile's user scope — absent, the
  application exits with **"Database Configuration Required"** before the login screen (11.2)

The environment is inherited at logon, so variables written into a profile only reach the server
after that session signs in again. Written from another session into
`HKEY_USERS\<SID>\Environment`, they need the profile to exist, which means the account has logged
on at least once. Sequence, if this is ever applied: reservations, auto logon, reboot, variables,
reboot again.

### If it is applied later

Do it when the server is being rebuilt or moved, not to a working one. Verify in this order, and
stop at the first failure rather than reaching for the profile or the licence:

```
query session
Get-Process Thinfinity.VirtualUI.Server -IncludeUserName | Select-Object Id, UserName
```

then `http://localhost:6580/` for a 200, then the tile, then `startup.log` in the `Start in` folder.

## 13. Combo drop-downs appear on the desktop in dev mode — 2026-09-15

A combo's list is not part of the form. Windows creates it as its own **top-level popup window**,
owned by the desktop rather than parented to the form that opened it.

In `--tf-dev` the application really is running on the development machine's desktop, so that popup
is drawn there — it "bleeds through", landing at a corner of the screen instead of under the combo
that opened it. The form behind it is being streamed; the popup is not part of that surface.

**It is only combos**, and the reason is worth keeping: every other drop-down in this application
was built as a panel parented to the form. `TileDropDownController` opens the ribbon tile menus
that way, which is why TEST_CASES MENU-18 records the menu overlaying the regions rather than being
clipped at the ribbon's edge. The combo is the one control where the OS still owns the list.

**Confirmed in a real browser session — 2026-09-21.** It is worse there than misplacement. The
list opens where it should and can be picked from, but the **dismissal never arrives**: a click
outside it does nothing, and the session is stuck until a value is chosen — with no value meaning
"I did not want this". In a desktop window the same list dismisses normally, which is what points
at the relay rather than at the control.

### Settled the same day, through the real server — not dev mode, and not one combo

The paragraphs above first blamed the QBE lookup cell being **opened in code** (`DroppedDown =
True` as its editor appeared), on the reasoning that it was the only combo doing anything unusual.
Wrong. Tested through `http://localhost:6580/` with `DevMode=False`:

| Control | Wedges? | Why |
|---|---|---|
| QBE value cell (a `DataGridView` combo editor) | **yes** | native popup |
| Layout drop-down on the browse toolbar | **yes** | native popup |
| Columns manager | no | a checked list in a panel |
| Ribbon tile menus | no | panels, by `TileDropDownController` |

**Every real `ComboBox` has it. Every panel is fine.** It is the popup window, not the control and
not how it was opened.

**Escape closes it.** That is the whole difference between a quirk and a trap: keyboard events
relay where the dismissing click does not, so there is always a way out that is not "choose a
value you did not want" — which on the Layout drop-down would mean applying a layout and changing
what is on screen.

**Decision, 2026-09-21: accept it and teach Escape.** The panel-and-`ListBox` replacement stays in
reserve — a `ListBox` in a panel, shown and hidden by hand, inside the form's own window. It would
have to be a shared control rather than a change per page, because combos are everywhere and
`COMBO_CHECKLIST.md` asks for binding and validation to stay in shared patterns. That is a large
change with real regression risk for something a user adapts to in a day. **Revisit it if Escape
ever stops working, or if a combo appears somewhere Escape cannot reach.**

One thing did change: the QBE cell no longer forces its list open. Not because that caused the
wedge — it did not — but because auto-opening a list that can trap you made every click into a
lookup cell a trap, where now only a deliberate one is.

### The splitter repaint fails in a session too, and the control is healthy

Same day, same cause family. Opening a browse page through a real session threw
`ExternalException` — "a generic error occurred in GDI+" — from `Graphics.FillRectangle`, inside
`SplitContainer.RepaintSplitterRect`, inside WinForms' own `OnLayout`. Nothing of ours is on the
stack, and nothing outside the control can catch it, so it reached the user as an
unhandled-exception dialog on every browse page.

The obvious explanation is a degenerate rectangle, and it is wrong. `SafeSplitContainer` logs the
control's state when it catches, and the control is entirely healthy:

```
size=940x562  client=940x562  distance=150  width=6  min1=120  min2=120
collapsed1=False  visible=True  handle=True
```

It needs 246 pixels and has 562. So `CreateGraphics()` is failing on a sound control inside a
VirtualUI session — a device-context artefact of the session, not an application bug. There is
nothing in our geometry to fix, which is why the treatment is to swallow that one repaint and log
it. The next layout pass redraws it and nobody sees anything missing.

## 11.7 "Application ended, user still connected" is the reconnection timeout — 2026-09-22

A tab was left in the background. Coming back to it, the page said the application had ended while
also showing the user still logged in. It reads as an inactivity timeout with a broken message. It
is neither.

**What actually happens**, from the vendor:

1. The tab goes to the background and the browser suspends or discards it. Chrome's **Memory Saver**
   and Edge's **Sleeping Tabs** both do this, and it closes the WebSocket to the server.
2. VirtualUI sees a disconnected browser and starts the **Reconnection timeout** — five seconds on
   this profile.
3. Five seconds later the server terminates the application process.
4. Returning to the tab re-establishes the connection, finds no application running, and says so.
   The web session and its authentication are separate and still valid, which is why the same screen
   says the user is still signed in.

So the two messages are not contradicting each other. One is about the application process, the
other about the web session, and they have different lifetimes.

### What to change, and what it costs

- **Raise Reconnection timeout** in the Application Profiles Editor, General tab. Five seconds is
  the default and far too short for real use. 300 seconds is the vendor's suggested starting point;
  higher if tabs are parked for longer. The cost is that after a genuine disconnect the process, its
  memory and **its licence seat** stay held for that window — and with one seat, holding it for five
  minutes is not a footnote.
- **Check the idle timeout on anything in front of the server** — reverse proxy, load balancer,
  firewall. One of those closing idle WebSockets produces the same sequence with the tab in the
  foreground.
- **On a managed fleet**, exclude the site from Memory Saver / Sleeping Tabs by policy. That
  prevents the disconnect rather than surviving it.

### What it means for the session figures

This setting is load-bearing for `FW_Session` and the health page, and raising it degrades them.
Today a disconnect ends near-exactly, because the grace is five seconds: a closed tab releases its
session in about four and a half. At 300 seconds, "connected" means *connected, or gone for up to
five minutes* — the count on the health page becomes an upper bound rather than a fact, and a
one-seat licence can read as occupied by somebody who left.

That is a trade, not a bug: a user who loses their work to a backgrounded tab is a worse outcome
than a session count that lags. But the health page's wording and the architecture document both
say the end is near-exact, and both are keyed to the five seconds. **If the timeout is raised, say
so in the same breath** — the note in the health section, and the paragraph in the architecture
document that quotes the five seconds.

### Not ours to change from here

The profile lives in the Application Profiles Editor on the server. Server and Windows
configuration is not done through Claude — see the rule in `CLAUDE.md`, and 11.2 through 11.4 for
what that afternoon cost. Reading a pasted profile, explaining what a setting does and working out
the consequence for the session figures is the help that is actually useful.

### Stopping the browser suspending the tab

| | **Chrome** | **Edge** |
|---|---|---|
| The feature | Memory Saver | Sleeping Tabs |
| **One machine** | `chrome://settings/performance` → **Always keep these sites active** → Add the host | `edge://settings/system` → Optimize Performance → **Never put these sites to sleep** → Add the host |
| **Fleet, by policy** | `TabDiscardingExceptions` — URL patterns never discarded | `SleepingTabsBlockedForUrls` — the same list |
| Blunter options | `MemorySaverModeSavings` sets how aggressive it is, replacing the older `HighEfficiencyModeEnabled` | `SleepingTabsTimeout` delays sleep; `SleepingTabsEnabled` turns it off |

**The fleet column is the IS department's, not ours.** Browser policy is deployed through group policy or the browser management console and lands on every machine in the estate, so it goes through whoever owns that. What to hand them is short: the host name, which of the two policies applies to the browser they standardise on, and why — a suspended tab kills a running application session. Doing it per machine needs nobody's permission; doing it by policy needs theirs.

**On one machine, step by step.** Chrome: paste `chrome://settings/performance` into the address
bar; if Memory Saver is already off there is nothing to do; otherwise find **Always keep these sites
active** beneath it, press **Add**, and enter the host the application is served from — the host
alone, such as `wd-html5` or `wd-html5.yourdomain.com`, or `127.0.0.1` for a local test. Edge: paste
`edge://settings/system`, and under **Optimize Performance** find **Never put these sites to sleep**
→ **Add site** → the same host. While in Edge, raising **put inactive tabs to sleep after** from its
default to a few hours covers any site nobody remembered to list. Neither needs a restart.

**To check it worked**, open the application, work in another tab for ten minutes, and come back.
The application still being there means the tab was not suspended.

Four things that apply to both:

- **The exception list beats the off switch.** Off is a whole-machine change somebody will undo; an
  exception for one host survives that and keeps the memory saving everywhere else.
- **The entry is per browser profile.** A second Chrome profile, or another Windows account on the
  same PC, needs its own.
- **The policy names have been renamed once already.** Settings addresses have been stable, policy
  names have not — check them against the current admin templates before a rollout.
- **Neither prevents a discard under genuine memory pressure**, and neither reaches an unmanaged
  machine or a proxy dropping an idle WebSocket. They remove the common cause; the reconnection
  timeout is what survives the cause being missed.

## 14. DownloadFile returns before the browser fetches — 2026-09-24

The Save button on the employee import's problems page produced the browser's "The resource you are
looking for might have been removed, had its name changed, or is temporarily unavailable" page in a
real session. `ThinfinityHelpDeskAttachmentDelivery` writes a staged copy, calls `DownloadFile`,
and deletes the copy in a `Finally` - on the stated belief that `DownloadFile` is synchronous. The
SDK's own `OnDownloadEnd` event says otherwise: the call hands the browser a file to fetch *later*,
and by then it has been deleted.

**Not yet proven by a measurement** - no local Thinfinity log recorded the 404 - but it is the only
explanation that fits both the message and the SDK surface.

Showing a document now goes through `BrowserDocument.Show` instead: `HTMLDoc.GetSafeUrl` for a
time-limited address to the file, and `OpenLinkDlg` to offer it as a link, because a browser blocks
a tab nobody clicked for. The staged copy lives in `ProgramData\SDC_Framework\Documents`, where the
Thinfinity server can read it whatever account it runs under, and is cleared after two hours.

**The attachment delivery follows, same day.** `ThinfinityHelpDeskAttachmentDelivery` - the
import's Download Results and every Help Desk attachment download - now stages through
`BrowserDocument.StageForSession` and no longer deletes its copy after `DownloadFile`. One folder,
one two-hour clean-up, for everything handed to a browser. `OnDownloadEnd` is still not used: what
its `Filename` carries has not been checked, and the clean-up does not need it.

**Nothing staged may stay - same day.** A staged file could outlive everything: the next sweep only
ran when somebody staged another file. Glenn's rule is that these files - the import's results hold
every new user's PIN - disappear with nobody doing anything. `BrowserDocument` now has five layers:
a 15-minute address; a five-minute timer while the process runs; `ClearThisSession` beside all three
`SessionTracking.End` calls in `Program.vb`, before the database round trip; `SweepAtStartup` at
the top of `Main`; and a hidden detached PowerShell per staged folder that sleeps 20 minutes and
deletes it, which outlives a killed process unless the whole tree is killed. The PowerShell command
was run with a 3-second sleep on 2026-09-24 and removed its folder.

**The fifth layer did not survive a real session.** Its first use in a browser session logged
`An error occurred trying to start process 'powershell.exe' ... Access is denied`
(`BrowserDocument.StartDetachedCleanup`, 2026-09-24 14:57). **A Thinfinity session cannot start
another program** - worth knowing well beyond this: anything that shells out, opens a file with its
default application or launches a helper will fail the same way in a session and work on the
desktop. The layer was removed; four remain. The case it covered - the process killed and nobody
signing in to that server again - is closed only by a scheduled task on the server, deleting
folders under `ProgramData\SDC_Framework\Documents` older than 20 minutes.

**Per-session folders - same day, Glenn's suggestion.** Staging moved to
`ProgramData\SDC_Framework\<area>\<pid>-<process start ticks>\`, areas `imports` and `attachments`.
A session's end deletes its folders whole; a start deletes whole every session folder whose process
is gone (the start ticks guard against a reused process id), and falls back to the 15-minute age
rule where another account's process cannot be inspected. The old `Documents` folder is swept by age.
