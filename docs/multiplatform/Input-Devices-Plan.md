# Input Devices — analysis and plan

Plan document for carrying the reference front end's "Input Devices" capability into the Avalonia front end.
Collision rule: this plan never touches the CEC bus (`/dev/cecN`, libCEC) — that is and stays Stufe B of
[Hdmi-Cec-Plan.md](./Hdmi-Cec-Plan.md).

## 1. In one paragraph

The reference (WPF) ships six Windows-only projects that address gamepads, USB remotes and IR receivers
directly (raw input, SetupAPI, HID IOCTLs). The Avalonia front end ported **none** of them — and needs almost
none of it: whatever the operating system delivers as a keyboard or as media keys (every IR receiver with a
driver, every Bluetooth keyboard, every HDMI-CEC remote, the Android TV remote) already travels through our own
action layer `KeyBindings` (key → action, rebindable through `keybindings.json`; 69 places in the front end ask
actions now, never raw keys). The one input class we cannot reach today is the **gamepad** (Avalonia knows no
gamepad API on any platform). The plan therefore builds exactly one new component: a small gamepad reader whose
buttons enter through the same `KeyBindings` door every remote button already uses. Everything else in the
reference stack is either already covered or deliberately left behind.

## 2. What the reference's "Input Devices" was

Six projects under `MediaPortal/Source/UI/Input`, all `net10.0-windows`:

| project | native Windows calls | its job |
|---|---|---|
| `InputDeviceManager` | setupapi×6, kernel32×5, user32×4, hid×1 | plugin shell: registers for `WM_INPUT` (Windows raw input), forwards decoded HID events as `KeyPressed` |
| `HidInput` | SharpLibHid + setupapi | reads raw HID, decodes vendor collections (a Flytec remote registered as a mouse becomes keyboard codes), carries default maps for gamepads and the MCE remote |
| `InputDevices` | none | the mapping layer: device button → action; three executors: keystroke injection, workflow start, config-section open |
| `InputDevices.Common` | none | shared models (`Input`, `InputAction`, `InputDeviceInfo`) |
| `IrInput` | none (named pipes) | client for a separate IR server process (msi6700 hardware); learning/blasting IR commands |
| `MceRemoteReceiver` | hid, kernel32, setupapi, user32 | talks to eHome IR transceivers directly over HID |

The flow there: a listener decodes raw HID → system message with a `Handled` flag → `InputHandler` looks the
per-device mapping up → an executor acts, and the key never reaches the ordinary keyboard pipeline.

**Why it cannot be ported:** `WM_INPUT`, SetupAPI and the HID IOCTLs do not exist on Linux, macOS or Android,
and the vendor decoding is bound to specific chips. Porting here means: take the concept, not the code.

## 3. What the Avalonia front end already covers

`Settings/KeyBindings.cs` is the rebuilt, tested version of the mapping layer:

- A `KeyAction` enum in the viewer's language: `Back, Select, Menu, MediaMenu, Details, PlayPause, Stop, Find,
  Watched, View, Media, VolumeUp, VolumeDown, Mute`.
- Bindings rebindable through `keybindings.json`; a broken file repairs itself.
- A physical-key fallback for windowing systems that deliver no logical key (the X11 trick from the CEC plan).
- Every screen asks the action (`KeyBindings.Is(...)`), never the key.

**Everything the operating system turns into a key is therefore already covered:**

| device | route to a key | status |
|---|---|---|
| USB/BT keyboard, on-screen keyboard | ordinary | ✅ |
| IR remote with a driver (including the MCE transceiver: it registers as a virtual keyboard) | driver → key | ✅ |
| HDMI-CEC remote | kernel `RC_MAP_CEC` → `EV_KEY` → key (CEC plan Stufe A) | ✅ |
| Android TV remote, phone remote apps | Android → key | ✅ |
| Linux: rc-core-decoded USB IR receivers | evdev → key | ✅ |
| **gamepad / joystick** | **no route exists** | ❌ the only genuinely missing input class |
| exotic USB remote without driver and without an rc-core protocol | would need own decoding | ⚠️ edge case, see section 6 |
| IR blasting/learning (msi6700) | old Windows hardware | ➖ deliberately out |

## 4. How "Input Devices" will be built in the front end

One new assembly following the proven player seam (`MediaPortal.Frontend.Player` keeps libVLC out the same way):

```
MediaPortal/Source/Frontend/MediaPortal.Frontend.Gamepads/     ← the one new assembly
    IGamepadSource.cs          interface: Start(callback per button press), Stop()
    GamepadButton.cs           small normalized enum: DPadUp/Down/Left/Right, A, B, X, Y,
                               LeftShoulder, RightShoulder, Select, Start, Guide, LeftStick
    EvdevSource.cs             Linux: reads /dev/input/event*, recognizes gamepad EV_KEY bits
    XInputSource.cs            Windows: xinput1_4.dll, only XInputGetState on a poll timer
    NullSource.cs              macOS / Windows without a pad / Android until bring-up: does nothing
    FakeSource.cs              for the tests
```

Inside the front end little changes:

```
App.axaml.cs                 compose the source at startup (as CoverReader/ClearArtReader already do)
Settings/GamepadGestures.cs  button → KeyAction table + dead-zone math for the left stick
Settings/KeyBindings.cs      gains one new entry point that answers a gamepad's finished KeyAction
                             through the very logic that answers KeyEventArgs today
```

The sentence that matters (Phase 0 writes it down, a test holds it): **one button press is answered exactly
once.** The gamepad reader injects no synthetic keystrokes into the operating system; it calls `KeyBindings`
with the finished `KeyAction`, and the active view answers it — the same road a remote's key takes after the
windowing system translates it. A button can therefore never fire twice (once from the device layer, once from
the key pipeline), which is what the reference had to guard against with its `Handled` flag.

### The button table (default, later overridable per device)

| gamepad | action | note |
|---|---|---|
| D-pad, left stick (with dead zone + repeat) | arrow keys | browsing the wall and the rails |
| A | `Select` | |
| B | `Back` | |
| X | `Menu` | |
| Y | `MediaMenu` | |
| Start | `Media` | bring up Now Playing |
| LB / RB | `VolumeDown` / `VolumeUp` | |
| Back, Guide | none | Guide would be a CEC-Stufe-B wish ("wake the television") — that goes exclusively through `ITvControl` from the CEC plan, never through this plan |

### The gamepad reader's platform matrix

| platform | backend | extra packages | note |
|---|---|---|---|
| Linux / Raspberry Pi | evdev (`open`/`read` on `/dev/input/event*`) | none | the same syscall surface the CEC plan's Stufe A already trusts |
| Windows | XInput (`xinput1_4.dll`) | none | covers Xbox-style pads; deliberately a far smaller footprint than HidInput was |
| macOS | `NullSource` | – | macOS is not a target box yet; IOKit/GCController only when needed |
| Android | `NullSource` for now | – | a paired BT pad already sends D-pad keys into the app today — navigation works without new code; a JNI shim comes with the Android bring-up |

Licensing: evdev and XInput are plain syscalls/exports, no third-party library — unlike libCEC in the CEC plan,
no licensing decision is needed here.

## 5. The settings screen — how the frontend implements it (this is the feature, not a read-only list)

The reference's "Input Devices" section is a **learning-style mapping editor**, and that is what we build.
Read from its skin (`InputDevices/Skin/default/screens/InputDevices-*.xaml`, `Models/MappingProxy.cs`,
`Models/MappableActionsProxy.cs`):

**What the reference does (screen by screen):**

| reference piece | what the user does there |
|---|---|
| `InputDeviceSettings` (settings store) | holds `List<InputDeviceMapping>` — one mapping table **per device**, persisted per user |
| Dialog *SelectDevice* ("press any button") | the user presses a button on the pad/remote they want to configure; the listener sniffs which device answered and opens that device's page |
| `InputDevices-SelectAction.xaml` (device page) | header shows the device's friendly name; a list of all mappable actions, each row = action icon + action name + current input; rows have two commands: `BeginMapping` and `DeleteMapping` ("Clear mapping" button, visible only when something is bound) |
| Dialog *MapAction* ("press the key now") | a bare dialog with a help line and a live label; the user presses the physical button/key; `MappingProxy.HandleInput` collects modifiers + one non-modifier key, marks the mapping complete, and shows it — then it is stored |
| Mappable sources | keys, global workflow actions, screens, config sections — i.e. a button can trigger an action, open a screen, or open a config page |

**How the Avalonia frontend will implement exactly this:**

```
Views/InputDevicesView.axaml          ← REPLACED (the current read-only listing was wrong; it goes away)
ViewModels/InputDevicesViewModel.cs   ← rewritten as the editor below
Input/InputSniffer.cs                 NEW: listens once for the next real input, whatever it is
```

1. **Device chooser** — the page opens with the reference's "press any button" step:
   *"Press a button on the device you want to configure."* While the page is open, `InputSniffer` records the
   next arriving input from **every** route at once:
   - Avalonia `KeyDown` on the window (keyboard, IR remotes that arrive as keys, CEC keys, Android remote),
   - the gamepad seam: `GamepadRemote` already polls the source and knows the pad's device name; it gains a
     one-shot "next button press" callback for the sniffer while the settings page is open.
   The first event names the device → the page switches to that device's mapping table. A second list shows
   devices already present in `keybindings.json` so they can be opened without pressing anything.

2. **Mapping table** — one row per `KeyAction` (the same actions every screen already asks):
   `[icon] Back — Escape, BrowserBack  [Change] [Clear]`
   D-pad/stick rows appear for pads. Focus ring works like everywhere else (DpadNavigation); OK/Enter starts
   re-binding, the Clear button removes just that action's binding for this device (reference's `DeleteMapping`).

3. **Learning dialog** — pressing *Change* opens the MapAction equivalent:
   *"Press the button that should do <action>. Esc cancels."* `InputSniffer` captures the next single input
   (modifiers allowed, one primary input — the reference's exact rule), echoes it live in the dialog, and on
   capture writes it and closes. Timeout/cancel costs nothing.

4. **Storage** — the dialog writes into the phase-2 structure that already exists and is tested:
   `keybindings.json → devices["<device id>"].bindings["<Action>"] = [...]`. Global bindings stay editable on
   the same page via a "All devices / defaults" entry at the top of the device list. No new file format, no
   second source of truth: the JSON stays the only store, the screen is its editor.

5. **Mappable targets**: v1 maps button → `KeyAction` only (that covers everything our UI answers). The
   reference's extra targets (open screen, open config section) would need a screen/config registry first —
   deferred, listed in section 6.

6. **Non-collision stays guaranteed**: the sniffer only *observes* while the settings page is open; it raises
   no synthetic keys, and the router rule (one press, one answer) is untouched.

### Phases

| phase | content | state |
|---|---|---|
| **0** | `KeyBindings` is THE single action router (documentation + test) | ✅ built |
| **1** | `MediaPortal.Frontend.Gamepads` assembly (evdev + XInput), `GamepadRemote`, composition in `App.axaml.cs` | ✅ built |
| **2** | `"devices"` section in `keybindings.json` (device override → global → defaults), old format loads unchanged | ✅ built |
| **3a** | the editor described above: device chooser → mapping table → learning dialog, replacing the read-only listing | **to build** |
| **3b** | extra mappable targets (screens, config sections) | deferred |

The file format from phase 2 on (only the new section is new):

```json
{ "Bindings": { "Back": ["Escape", "Back"] },
  "Devices": { "045e-028e-Xbox Wireless Controller": { "Bindings": { "Back": [] } } } }
```

Device identity: Windows `vid-pid-productname`, Linux evdev `NAME=` — the reference `InputDeviceInfo`'s
identity idea, kept as data rather than code. Unknown devices never match, so a hand-edited file stays safe.

## 6. Deliberately NOT planned

- Translating HidInput / InputDeviceManager / MceRemoteReceiver (Windows plumbing the OS decoders do today).
- Vendor decoding for specific USB remote brands (per brand inside the phase-1 assembly if a viewer reports a
  device the OS cannot name; on Linux rc-core covers most of them for free).
- IR blasting/learning (IrInput, msi6700).
- Anything touching the CEC bus (libCEC, `/dev/cecN`, power/input switching) — exclusively CEC plan Stufe B.
- Mappable targets beyond `KeyAction` (reference's screen-opening and config-section-opening executors) — need a
  screen/config registry in the frontend first; phase 3b, only if a viewer asks for it.
- Colour-key delivery (documented as platform-impossible in the CEC plan; no input-device layer lifts an X11
  keycode past 255).

## 7. Is it cross-platform? — the direct answer

Yes, and this is how: the only new code that has to know about platform differences is the small
`IGamepadSource` layer (evdev for Linux, XInput for Windows, null for macOS/Android initially).
Everything behind it — the gesture table, `KeyBindings`, the views — is one platform-neutral path.
All other input devices stay on the route their operating system builds for them (keyboard, media and CEC
keys), and that route is cross-platform by definition, because every platform produces those keys itself.
That is exactly the difference to the reference stack: it dug *under* the windowing system with Windows raw
input; this plan joins *above* it, where all platforms meet.
