# Программная архитектура G-code sender для FluidNC/GRBL

_15 items researched._

## Table of Contents

1. [bCNC](#bcnc) — License: GPL-2.0 for the main application. The repository also bundles some… | Stack: Python (Python 3; legacy Python 2 compatibility). GUI built on… | Platforms: Desktop-only, from a single Python/Tkinter codebase: Windows, Linux,…
2. [Candle](#candle) — License: GPL-3.0 (GNU General Public License v3.0). Strong copyleft — safe to… | Stack: C++ with the Qt framework (Qt Widgets, not QML/QtQuick). Uses… | Bluetooth: No dedicated Bluetooth support of any kind. Candle has no BLE… | Platforms: Desktop only: Windows, Linux (Ubuntu), macOS, and Raspberry Pi OS…
3. [ChiliPeppr](#chilipeppr) — Stack: Two-tier architecture. (1) Browser front-end: a cloud-hosted… | Bluetooth: No native Bluetooth stack in either tier. Bluetooth is only reachable… | Platforms: Front-end runs in any modern desktop browser as a cloud-hosted page…
4. [CNCjs](#cncjs) — License: MIT | Stack: Node.js (Express) server backend + React/Redux web frontend, bundled… | Bluetooth: No native Bluetooth stack in CNCjs. Bluetooth is only reachable as… | Platforms: Server runs on any Node.js host — Windows, macOS, Linux, and…
5. [ESP3D-WebUI (FluidNC WebUI)](#esp3d-webui-fluidnc-webui) — License: GPL-3.0 (package.json declares "(ISC OR GPL-3.0)"); several… | Stack: JavaScript single-page app that runs in the operator's browser, not a… | Bluetooth: Not applicable to the WebUI: ESP3D-WebUI cannot be reached over… | Platforms: Universal via the browser and zero-install. Because the UI is served…
6. [Fluid-controller (gjkrediet)](#fluid-controller-gjkrediet) — License: GPL-3.0 (LICENSE file in repo, GitHub spdx_id GPL-3.0). | Stack: Arduino/C++ firmware for the ESP32, written as a single-file sketch… | Bluetooth: Bluetooth Classic SPP (RFCOMM), via the ESP-IDF esp_spp / Arduino… | Platforms: Single fixed hardware target: the LilyGO TTGO T-Display (original…
7. [Grbl-Plotter](#grbl-plotter) — License: GPL-3.0 (GNU General Public License v3.0, per the GitHub repo license… | Stack: C# on .NET Framework (project self-describes as 'DotNET 4.0', built… | Bluetooth: No Bluetooth support of any kind (no BLE/GATT/Nordic-UART, no… | Platforms: Windows desktop only — a WinForms/.NET Framework application with…
8. [grblHAL](#grblhal) — License: GNU GPL v3 (grblHAL core COPYING file is GPLv3, inherited from GRBL).… | Stack: C (embedded), fork/32-bit successor of GRBL 1.1f. A shared portable… | Platforms: Firmware portability: one shared core runs across 15+ 32-bit MCU…
9. [gSender](#gsender) — License: GNU GPLv3 (free software, provided as-is). | Stack: JavaScript/TypeScript. Electron desktop app: React front-end… | Bluetooth: Not supported. gSender has no Bluetooth transport of any kind… | Platforms: Desktop only, but broad within desktop: prebuilt for Windows (x64),…
10. [ioSender](#iosender) — License: BSD-3-Clause (permissive; code/ideas can be reused with attribution… | Stack: C# / WPF (Windows Presentation Foundation), .NET Framework 4.6.2,… | Platforms: Windows-only. Single WPF / .NET Framework 4.6.2 desktop codebase — no…
11. [LaserGRBL](#lasergrbl) — License: GPL-3.0 (GNU General Public License v3.0). Strong copyleft — safe to… | Stack: C# on the classic .NET Framework (3.5+, current builds target .NET… | Bluetooth: No dedicated Bluetooth support of any kind — no BLE (GATT/Nordic… | Platforms: Windows-only. Built on .NET Framework WinForms, so it runs natively…
12. [LightBurn](#lightburn) — License: Commercial, closed-source, paid perpetual-with-updates license… | Stack: Native desktop application built in C++ on the Qt framework… | Platforms: Desktop only: Windows (10+), macOS (10.13+) and Linux (Ubuntu-based,…
13. [OpenBuilds CONTROL](#openbuilds-control) — License: AGPL-3.0 (declared in package.json). Note: the GitHub repo badge… | Stack: Electron desktop app in JavaScript. A single Electron process is BOTH… | Bluetooth: No Bluetooth support of any kind. The codebase has no… | Platforms: Desktop via Electron: Windows, macOS (x64 + arm64), Linux; plus…
14. [OpenCNCPilot](#opencncpilot) — License: MIT (permissive; code and ideas can be reused with… | Stack: C# / WPF (Windows Presentation Foundation), .NET Framework (classic… | Platforms: Windows-only. Single WPF / .NET Framework desktop codebase; the…
15. [Universal Gcode Sender (UGS)](#universal-gcode-sender-ugs) — License: GPL-3.0 (GNU General Public License v3.0). Copyleft — code/idea reuse… | Stack: Java 17, built with Maven. Two editions sharing one headless core… | Bluetooth: No dedicated Bluetooth code path. UGS treats Bluetooth exclusively as… | Platforms: Desktop-only: Windows (x64), macOS (x64 and ARM64), Linux (x64, ARM,…

---

## 1. bCNC <a id="bcnc"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Python (Python 3; legacy Python 2 compatibility). GUI built on Tkinter (the standard-library widget toolkit). Single cross-platform desktop codebase; runs even on slow hardware such as Raspberry Pi.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GPL-2.0 for the main application. The repository also bundles some third-party components under BSD-3 and MIT.
- **repository** _Ссылка на исходный код_: https://github.com/vlachoudis/bCNC
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Actively maintained rolling 'master' (no recent formal tagged releases; distributed as pip package 'bCNC'). Last push 2026-04-15, repo metadata updated 2026-07-23; ~2260 commits, ~513 open issues. Note: maintainers warn macOS support may be broken in current dev.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Primary transport is serial/USB via pyserial. Because the port is opened with pyserial serial_for_url(), network URLs are also accepted transparently: socket://host:port, telnet://host:port and rfc2217:// — this is the only 'WiFi' path (raw TCP to a serial bridge), there is no dedicated WebSocket client. No native Bluetooth stack: BT works only if the OS exposes a Bluetooth-Classic SPP device as a virtual serial (COM/rfcomm) port. bCNC additionally runs its own HTTP 'pendant' server (default port 8080) for remote browser control, but that is a front-end to the desktop app, not a controller transport.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Weak transport abstraction. The link is hardwired to a single pyserial handle (self.serial = serial.serial_for_url(...)) inside Sender.py; there is no dedicated pluggable ITransport interface decoupled from the streaming loop. bCNC's real abstraction line is at the PROTOCOL/DIALECT level (the Controllers/ plugin classes), not the transport level. serial_for_url gives free socket/telnet support but transport and protocol are not cleanly separated. For ArctZ this is only a partial reference: dialect-vs-protocol separation is good, transport-vs-protocol separation is not.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting flow control (the advanced technique this project is known for). Constant RX_BUFFER_SIZE = 128; it keeps a running list `cline` of the byte length of each line already pushed into the controller's RX buffer, and only sends the next line while sum(cline) < RX_BUFFER_SIZE, popping an entry each time an 'ok'/'error' response arrives. This keeps the GRBL serial RX buffer as full as possible without overflowing — significantly faster than naive send-line/wait-for-ok. Current fill is exposed via _sumcline / getBufferFill().
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Fixed constant RX_BUFFER_SIZE = 128 bytes, matching classic AVR GRBL's serial RX buffer. It is NOT auto-negotiated per firmware, so on FluidNC/grblHAL (which have larger buffers) bCNC under-utilizes the buffer but remains safe. Status is polled on a SERIAL_POLL = 0.125 s (~8 Hz) timer.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Single unified outbound queue (self.queue) drained by one serial I/O thread; there is NO feeder/sender split as in CNCjs. Manual commands (jog, MDI) and file-stream lines share the same queue and the same character-counting loop. The only exception is real-time single-byte commands (see realtime_bypass_path).
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time / immediate commands (?, !, ~, and immediate strings starting with $ ( @ {) are detected in executeGcode() and written straight to the serial port, outside the character-counting accounting, so a full RX buffer does not block them. However there is NO dedicated priority queue, and 0x18 soft-reset is a separate hardResetGrbl() path rather than a queued byte. So a bypass exists for status/hold/resume/reset, but it is not a clean generalized real-time byte channel (contrast with senders that maintain a separate realtime write path).

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses GRBL status reports of the form <State|MPos:x,y,z|WPos:...|FS:feed,spindle|...> via the controller plugin (parseLine in _GenericGRBL / GRBL1). Extracts machine and work positions, feed and spindle. Both GRBL 0.9 and 1.1 report grammars are handled through the separate GRBL0 / GRBL1 dialect classes.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Tracks the GRBL-reported states Idle / Run / Hold / Jog / Alarm / Door / Check / Home / Sleep; the UI reflects the state (label/colour). State is taken directly from the status-report string rather than reconstructed independently.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active polling: bCNC sends '?' on a timer (SERIAL_POLL = 0.125 s, ~8 Hz) rather than relying on firmware auto-push. A slower G_POLL = 10 s timer refreshes modal/state info when idle. This constant ~8 Hz poll rate would be relatively heavy for a BLE channel.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Supports work coordinate systems G54–G59, displays and switches between machine (MPos) and work (WPos) coordinates, and sets WCS/zero offsets (e.g. G10 L20, G92).

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Not MVVM/MVC. A Tkinter widget-based, event-driven procedural GUI organised around large god-classes (Sender, CNCCanvas, ribbon/page plugins). Logic and UI are intertwined; there is no data-binding layer comparable to CommunityToolkit.Mvvm.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Partial separation. The communication/streaming core lives in Sender.py plus the Controllers/ plugins and is logically distinct from the Tkinter widgets — the same core also backs the headless pendant HTTP server and the command line, proving some reusability. But there is no clean interface boundary: Sender is also the central app object and reaches back into GUI/state, so reusing the core outside Tkinter is possible yet not cleanly modularized. Moderate reference value for ArctZ's core-vs-head split.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Detects GRBL ALARM state and error/alarm messages from the response stream, maps numeric GRBL error/alarm codes to human-readable text, surfaces them to the user and halts streaming; offers unlock ($X) and soft-reset actions.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > Minimal. On serial errors the connection is closed and streaming aborts; reconnection is a manual Open/Close toggle. No automatic retry/backoff and no Bluetooth-specific recovery.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes — a rich 2D toolpath preview on a custom Tkinter Canvas (CNCCanvas) with pan/zoom and live tool-position marker, plus a separate OpenGL 3D viewer plugin.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Full G-code interpreter (CNC.py) tracks modal state (motion G0/1/2/3, plane, units, active WCS) and tessellates arcs (G2/G3 via I/J or R) into line segments for both preview and the autoleveler/editor.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Desktop-only, from a single Python/Tkinter codebase: Windows, Linux, macOS (macOS flagged as possibly broken in current dev) and Raspberry Pi. There is NO native mobile (Android/iOS) or web application. 'Remote' access is provided by the built-in pendant HTTP server (port 8080) which serves a lightweight, mobile-friendly web UI proxied to the running desktop instance — a remote control surface, not a true cross-platform client. This contrasts sharply with ArctZ's Avalonia approach of shipping desktop + mobile + web heads from shared UI code.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > GRBL $$ settings model: reads and writes $$ number=value settings, presents them in an editable settings table, and issues $ commands. FluidNC's YAML config upload/edit is not a first-class feature (FluidNC is driven as a GRBL-1.1-compatible controller). grblHAL extended settings are surfaced generically.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One controller serial connection at a time. The pendant HTTP server can serve several browser clients simultaneously, but they all funnel through the single controller link.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > Controller-plugin abstraction in bCNC/controllers/: _GenericController (base), _GenericGRBL, GRBL0 (0.9), GRBL1 (1.1), SMOOTHIE (Smoothieware), G2Core (and historically TinyG). Classic GRBL and grblHAL (driven through the GRBL1 dialect) are the primary targets; FluidNC is used as a GRBL-1.1-compatible controller. The active dialect is chosen by the user in configuration; welcome-string/$I parsing informs identification rather than fully automatic switching.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Strong and a flagship feature. Native G38.2 probing plus a full autoleveler: probes a grid to build a height map and applies per-segment Z compensation to the toolpath. Also supports single-point/tool-length probing and camera-based alignment.

### Прочая информация
- **type**: sender
- **stack**: Python / Tkinter
- **note**: Advanced GRBL sender with character-counting buffer streaming, autoleveler and G-code editor

### Неопределённые поля (uncertain)
- bt_profile_type
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- bt_reconnect_behavior
- bt_pairing_model
- os_bt_api
- jog_mode_types
- jog_cancel_mechanism
- mode_mutual_exclusion
- jog_latency_budget

---

## 2. Candle <a id="candle"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > C++ with the Qt framework (Qt Widgets, not QML/QtQuick). Uses QSerialPort, QTcpSocket and QWebSocket from Qt, QScriptEngine for in-command scripting, and OpenGL for the 3D visualizer. UI is built with Qt Designer (.ui forms). Desktop-class GUI application, no separate headless core.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GPL-3.0 (GNU General Public License v3.0). Strong copyleft — safe to study for design patterns, but source cannot be copied into a MIT/closed ArctZ. The classic Candle codebase is a well-known lightweight reference for the GRBL character-counting streaming protocol.
- **repository** _Ссылка на исходный код_:
  > https://github.com/Denvi/Candle (author Denis Hayrullin / 'Denvi'). Notable fork: https://github.com/Schildkroet/Candle2 (adds GRBL-Advanced support). SourceForge mirror: https://sourceforge.net/projects/candle.mirror/
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Active. GitHub API shows master pushed 2026-07-13, repo updated 2026-07-20, ~18 open issues. Recent tagged releases: v11.2 (2026-02-09), v10.12 (2025-12-27), v10.11.1 (2025-11-10), v10.10.4 (2025-10-04), plus a 'nightly' tag (2025-08-19). README lists current targets including Raspberry Pi OS (Trixie), confirming ongoing maintenance. The master branch has recently gained a network-transport abstraction absent from the older single-serial-port versions.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Three transports in the current master: (1) Serial/USB via QSerialPort (SerialPortConnection); (2) Telnet / raw TCP via QTcpSocket (TelnetConnection, address+port) — the path to FluidNC/ESP32 over WiFi; (3) WebSocket via QWebSocket (WebSocketConnection, with a binary/text mode flag) — the path to the ESP32 WebUI websocket. Selected via a ConnectionType enum {SerialPort, Telnet, WebSocket}. No native Bluetooth stack. Note: classic/older Candle builds were serial-only; the Telnet/WebSocket connections are a newer addition to the connections/ layer.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Partially mature — the transport layer is cleanly abstracted but the protocol/streaming layer is NOT separated from the UI. There is an abstract 'Connection' base class (src/candle/connections/connection.h) with a minimal, protocol-agnostic interface: connect()/disconnect()/isConnected()/send(QByteArray|QString|char*) plus Qt signals dataReceived(QString), errorOccurred(QString), connected(), disconnected(). Three concrete implementations (SerialPortConnection, TelnetConnection, WebSocketConnection) are interchangeable behind a single 'm_currentConnection' pointer, so swapping serial for TCP/WebSocket touches only which Connection is instantiated. HOWEVER all streaming, buffering, response parsing and state logic live in the ~6100-line frmMain UI class (frmmain.cpp) and call m_currentConnection->send() directly. So transport is decoupled from the rest, but the command/streaming protocol is fused into the main window — the opposite of the clean core/transport/protocol three-tier split ArctZ wants. Good reference for the Connection interface shape; poor reference for protocol reuse.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No dedicated Bluetooth support of any kind. Candle has no BLE (GATT/Nordic UART) and no explicit Bluetooth Classic SPP code. The only way to use Bluetooth is if the OS exposes a Classic-SPP device as a virtual serial COM port, which Candle's SerialPortConnection would then open like any other serial port. Provides no reusable BLE design for ArctZ.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Candle uses no native Bluetooth API on any platform — no WinRT BLE, no Android BluetoothLeGatt, no iOS CoreBluetooth, no Web Bluetooth (Qt has QtBluetooth but Candle does not use it). It is desktop-only (Windows/Linux/macOS/RPi) and reaches a controller only through QSerialPort, QTcpSocket or QWebSocket. None of ArctZ's mobile/web BT APIs are exercised by Candle.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Classic GRBL character-counting streaming (send-ahead), and Candle is one of the simplest readable references for it. Constant BUFFERLENGTH = 127 (frmmain.h). Two lists in frmMain: m_commands (QList<CommandAttributes> — commands already sent to GRBL, awaiting 'ok'/error) and m_queue (QList<CommandQueue> — commands that did not fit, waiting for room). bufferLength() sums the byte length of every in-flight command (each command's length is stored as command.length()+1 to count the trailing newline). sendCommand() appends to m_queue if (bufferLength() + command.length() + 1) > BUFFERLENGTH, otherwise appends to m_commands and calls m_currentConnection->send(). On each GRBL response the head of m_commands is popped with takeFirst(), freeing buffer space, and pending m_queue items / next file lines are pushed. sendNextFileCommands() loops while (bufferLength() + nextLen + 1) <= BUFFERLENGTH, streaming ahead. This is neither naive wait-for-ok nor blind buffered send — it is true character counting.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Modeled with a single hard-coded constant: BUFFERLENGTH = 127 (one byte of safety margin under the GRBL 128-byte serial RX buffer). It is NOT negotiated or firmware-specific — every controller is assumed to have the GRBL 128-byte buffer. A source TODO ('Store firmware version, features, buffer size on $I command' / '[OPT:VL,15,128]') notes the intent to read the real buffer size from $I but this is not implemented, so FluidNC/grblHAL controllers with different RX buffer sizes are still driven with the fixed 127-byte assumption.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Single unified pipeline, NOT the separate feeder/sender pair of CNCjs. Everything flows through sendCommand(command, tableIndex, ...): file lines, UI commands, jog commands and utility commands all share the same two structures — m_commands (in-flight, sent, awaiting ok) and m_queue (overflow, not yet sent). The 'tableIndex' argument tags a command's origin (0..n = g-code program line, -1 = UI command, -2/-3 = utility/internal commands such as $#, $G, $$), which is used for routing response side-effects, but they are not physically separate queues. Jog and file streaming therefore compete for the same 127-byte budget. Real-time single bytes are the one exception (see realtime_bypass_path).
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time single-byte commands are written straight to the transport via m_currentConnection->send(...), completely bypassing sendCommand(), m_commands, m_queue and the character-count accounting. Observed in source: status query send("?") (onTimerStateQuery), feed-hold/resume send(checked ? "!" : "~"), soft-reset send("\x18") (0x18 / Ctrl+X, in grblReset()), and jog-cancel send("\x85") (0x85, in jogContinuous()). Because these go directly to the Connection, they are delivered regardless of buffer fullness — exactly the immediate-dispatch mechanism ArctZ needs for a responsive joystick and e-stop. (Override bytes 0x90+ are driven from the slider controls; the raw real-time bytes above are the clearest examples.)
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Two modes, both using the GRBL 1.1 '$J=' jog protocol with G91 relative distance and G20/G21 units, optionally including a 4th (A) axis. (1) Step/incremental jog: jogStep() sends one '$J=G21G91X..Y..Z..F..' of the fixed step selected in cboJogStep. (2) Continuous / hold-to-move jog: when the step combo value is 0, jogContinuous() computes the distance from the current machine position to the soft-limit boundary along the pressed direction and sends a single large '$J=' toward that bound; releasing or changing direction cancels it. There is no separate absolute-jog mode — all jogging is relative via G91.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > Uses the GRBL 1.1 real-time jog-cancel byte 0x85, sent as m_currentConnection->send("\x85"). In continuous mode, when the jog vector changes (direction change or release) jogContinuous() sends 0x85 and then BUSY-WAITS: 'while (m_deviceState == DeviceJog && t.elapsed() < 5000) qApp->processEvents();' — i.e. it spins the event loop up to 5 seconds until the machine reports it has left the Jog state before issuing the next jog. Runaway protection: because each continuous jog is pre-sized to stop exactly at the machine soft-limit boundary (bounds/soft-limit math in jogContinuous), a lost key-up still halts at the limit rather than running forever. The blocking 5s busy-wait for jog-cancel confirmation is a notable design smell and a direct cautionary lesson for ArctZ's VirtualJoystick: on a slow/high-latency link this wait can freeze the UI.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses GRBL 1.1 '<...>' status reports with QRegExp patterns. Machine position: mpx = 'MPos:([^,]*),([^,]*),([^,>|]*)(?:,([^,|]*))*' (supports X,Y,Z and optional 4th/A axis). Work position is derived from the work-coordinate offset: wpx = 'WCO:([^,]*),...' and WPos is computed as MPos - WCO (ui->txtWPosX = MPos - workOffset). Spindle speed and parser modal info are obtained separately from the '$G' report; feed/override values drive the slider controls.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > DeviceState enum (frmmain.h) covering DeviceUnknown(-1), DeviceIdle(1), DeviceAlarm(2), DeviceRun(3), DeviceHome(4), DeviceHold0(5), DeviceHold1(6), DeviceQueue(7)-ish, DeviceCheck(8), DeviceDoor0..3, DeviceJog, DeviceSleep. The state token from each '<...>' status report sets m_deviceState, which drives control enable/disable, status-bar caption and color, and jog gating.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active host-driven polling, not push. A QTimer (m_timerStateQuery) fires at the configured queryStateTime() interval and, when connected and ready, sends the real-time '?' byte. It is throttled by an m_statusReceived flag: onTimerStateQuery only sends the next '?' if the previous status has already been received (m_statusReceived reset to false on send, set true on report), preventing a backlog of queries. The interval is temporarily raised (e.g. 1000ms) in certain states. Relevant to ArctZ: on a bandwidth-limited BLE link this fixed-interval '?' polling competes with jog traffic and would need tuning.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Supports work coordinate systems G54-G59: the active WCS is parsed from the '$G' parser-state report (QRegExp 'G5[4-9]') and stored in m_storedVars. Distinguishes machine coordinates (MPos, parsed directly) from work coordinates (WPos = MPos - WCO offset). Offsets are refreshed via the '$#' report (storeOffsetsVars), and '$#' is auto-requested after G92/G10 commands.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Not MVVM and not a clean MVC — a Qt Widgets application centered on a single ~6100-line frmMain god-object (the main window) built from a Qt Designer .ui form. Business logic, streaming protocol, response parsing, jog logic and widget updates are all methods of frmMain. This is the strongest divergence from ArctZ's CommunityToolkit.Mvvm view-model approach: there are no view-models and no data-binding layer, just direct widget manipulation from the form class.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Poor — a cautionary anti-pattern for ArctZ's reusable-core goal. Only the transport is separated (the Connection classes). All streaming/buffering, GRBL response handling, status parsing, state machine and jog logic live inside the frmMain UI class and directly manipulate widgets (ui->txtMPosX->setValue, ui->txtConsole->appendPlainText, etc.). There is no headless core that could be reused across platforms or driven without the GUI, unlike UGS's BackendAPI. ArctZ should emulate Candle's clean Connection interface but explicitly avoid Candle's fusion of protocol logic into the window class.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Alarm handling is basic/string-based, not an enumerated code table. DeviceAlarm is a state; 'floating' terminal messages are matched literally in dataIsFloating(): 'Reset to continue', "'$H'|'$X' to unlock", 'ALARM: Soft limit', 'ALARM: Hard limit', 'Check Door'. Recovery is via user-issued $X (unlock) / $H (home) / soft reset (Ctrl+X). No mapping of numeric ALARM:n / error:n codes to descriptions like UGS/FluidNC provide.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > Minimal. The Connection layer emits disconnected() and errorOccurred(QString) signals; on soft-reset or disconnect Candle clears its in-flight/queue lists (m_commands.clear(); m_queue.clear()) and resets sender/device state. Reconnection is user-initiated (reopen the connection). There is no automatic reconnect timer and no mid-job resume of a partially streamed program after a drop — an interrupted job must be restarted. Adequate for stable USB serial; insufficient for ArctZ's flaky-BLE case, which would need stronger auto-reconnect than Candle offers.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes — a full 3D OpenGL toolpath visualizer (glwVisualizer / GLWidget) renders the loaded G-code plus live tool position, buffer state and parser status overlay. Requires an OpenGL 2.0 GPU and SSE2 CPU per the README. A core, prominent feature of Candle.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — GcodeViewParse together with GcodePreprocessorUtils parse the program, tracking modal state and expanding G2/G3 arcs into segments for the visualizer; also used for height-map Z-offsetting of the program. Comment removal (GcodePreprocessorUtils::removeComment) is applied before command inspection.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Desktop only: Windows, Linux (Ubuntu), macOS, and Raspberry Pi OS (Trixie), all as native Qt Widgets builds. No mobile (Android/iOS) and no browser/web build. This is a key divergence from ArctZ, whose Avalonia stack additionally targets Android/iOS/WASM; Candle offers no reference for mobile/web transport or touch UI, and its Qt-Widgets-on-desktop model does not translate to ArctZ's cross-platform-core + thin-heads structure.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > GRBL-centric. Reads/writes GRBL '$$' numbered settings (processSettingsResponse parses the '$$' report, and settings are written as '$x=val'); reads offsets via '$#', parser modal state via '$G', and intends to read version/features via '$I' (partially TODO). There is no FluidNC YAML config-file upload/download workflow and no grblHAL-specific settings path — configuration is the classic GRBL '$'-settings model only.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One. A single m_currentConnection is active at a time; Candle drives exactly one controller per application instance. No multi-connection support.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > Primarily GRBL — v1.1 (current) and v0.9-and-below (legacy), per the README. There is no active welcome-string/dialect auto-detection routine (a TODO exists to store firmware version/features/buffer size from the '$I' [VER:]/[OPT:] response, e.g. '[VER:1.1d.20161014:...]', but it is not implemented). FluidNC and grblHAL work only insofar as they are GRBL-1.1-compatible; there is no FluidNC- or grblHAL-specific handling. The Candle2 fork extends dialect support to GRBL-Advanced.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes — G38.2 straight-probe is handled (PRB response parsed via QRegExp 'PRB:...' into probe coordinates), and Candle includes a height-map / auto-leveling mode (m_heightMapMode, a dedicated heightmap model and program-transform that offsets each line's Z from a probed surface mesh). A solid probing + surface-autolevel workflow.

### Прочая информация
- **type**: sender
- **research_date**: 2026-07-24

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- bt_reconnect_behavior
- bt_pairing_model
- mode_mutual_exclusion
- jog_latency_budget

---

## 3. ChiliPeppr <a id="chilipeppr"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Two-tier architecture. (1) Browser front-end: a cloud-hosted JavaScript single-page app at chilipeppr.com built on jQuery, a require.js-like module system (cpdefine()/cprequire()), amplify.js pub/sub (chilipeppr.subscribe()/publish()), jsPlumb for wiring widgets, and Three.js/WebGL for the 3D viewer. The whole UI is a composition of independent 'widgets' (plugins). (2) Local companion: Serial Port JSON Server (SPJS), a single compiled binary written in Go that exposes a WebSocket server (ws://localhost:8989/ws) and owns the physical serial connection to the controller. The browser never touches the serial port directly; it sends JSON commands to SPJS over WebSocket.
- **repository** _Ссылка на исходный код_:
  > Org: https://github.com/chilipeppr . SPJS: https://github.com/chilipeppr/serial-port-json-server (also the historical fork https://github.com/johnlauer/serial-port-json-server). Grbl workspace: https://github.com/chilipeppr-grbl/workspace-grbl and widget https://github.com/chilipeppr-grbl/widget-grbl . 3D viewer: https://github.com/chilipeppr/widget-3dviewer .
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Largely dormant. The Grbl workspace (chilipeppr-grbl/workspace-grbl) last saw commits around December 2017; front-end activity peaked 2015-2017. SPJS reached ~v1.96 and has ~328 stars / ~339 commits but is no longer actively developed by the original author (John Lauer); community forks (e.g. realthunder, felipeerazoeld) carry more recent tweaks. Effectively a legacy/reference project as of 2026, predating FluidNC entirely.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Two links must be distinguished. Browser<->SPJS: WebSocket only (ws://localhost:8989/ws; SPJS can also run on a remote host and be reached over the network/TCP, so the browser can be on a different machine than the serial port). SPJS<->controller: serial/USB is the native and effectively only controller transport. There is NO native WiFi-Telnet, WebSocket-to-controller, or BLE client inside SPJS. 'Wireless' operation is achieved by running the SPJS binary on a machine (e.g. Raspberry Pi/BeagleBone) physically wired to the controller and reaching that SPJS over the network. Bluetooth reaches the controller only as an OS-level Bluetooth Classic SPP virtual COM port that SPJS opens like any serial device.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Distinctive two-tier separation but not a polymorphic controller-transport interface. The browser widgets are fully transport-agnostic: they only publish/subscribe JSON messages (e.g. a 'send <port> <data>' command) over one WebSocket to SPJS and never know how the bytes reach the controller. SPJS in turn abstracts the OS serial subsystem behind a port name and layers per-controller 'bufferflow' algorithms (grbl/tinyg/marlin) on top. So there is a clean UI/comm boundary and a clean protocol/serial boundary, BUT the physical controller transport inside SPJS is essentially always serial (network = SPJS-on-a-remote-host, not a controller-side WiFi/Telnet/BLE driver). For ArctZ this is a strong example of decoupling UI from the comm core via a message bus, yet it does NOT provide a reusable multi-backend transport interface (serial vs BLE vs WiFi-telnet) the way ArctZ needs.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No native Bluetooth stack in either tier. Bluetooth is only reachable as Bluetooth Classic SPP (RFCOMM) exposed by the OS as a virtual serial/COM port, which SPJS then opens as an ordinary serial device. No BLE / GATT / Nordic UART Service (NUS) support exists. Because ChiliPeppr predates ESP32-class BLE FluidNC controllers, it is a weak Bluetooth reference for ArctZ, whose primary target is BLE-only FluidNC.
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > OS-level pairing/bonding is required to create the Bluetooth Classic SPP virtual serial port; SPJS performs no pairing itself and simply opens the resulting COM/rfcomm device. From ChiliPeppr's perspective it is purely a serial-port-open model.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > SPJS implements pluggable per-controller 'bufferflow' algorithms selected by buffer type. For Grbl the algorithm is character-counting: it counts each queued G-code line's bytes against a 127-byte window and keeps multiple lines in flight, only pausing when the count would exceed the controller RX buffer, resuming as 'ok'/'error' acknowledgments arrive. For TinyG it uses a queue-report (qr) / ~4-slot buffered model driven by JSON r:{} responses. A Marlin algorithm and simpler send-response/no-buffer modes also exist. So the default Grbl behavior is buffered char-counting rather than strict one-line send-and-wait.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > For Grbl, SPJS tracks a 127-byte line-counting window (the classic Grbl serial RX buffer size) and withholds the next line until an ok/error frees space. For TinyG it instead relies on queue reports (qr values) reflecting the controller's planner slots rather than raw byte counts. FluidNC's larger RX buffer would be under-utilized under the fixed 127-byte grbl assumption unless SPJS were reconfigured.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Single ordered send queue per serial port inside SPJS (it can hold very large programs, e.g. 25,000+ queued lines), NOT the dual feeder/sender split seen in CNCjs. Both streamed file G-code and manual/jog commands from the browser widgets are funneled into the same SPJS port queue; differentiation happens at the widget level (jog widgets emit small moves; the gcode-list widget streams the file). Real-time bytes are the one thing that bypasses ordering (see realtime_bypass_path). This is simpler than CNCjs's separated queues and means jog and file traffic share one FIFO.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > SPJS classifies certain bytes as priority/real-time commands that jump ahead of everything already queued for the port: ? (status), ! (feed hold), ~ (cycle resume), % (buffer wipe), and Ctrl-X / 0x18 (soft reset). These are pushed to the front and written immediately rather than waiting behind up to tens of thousands of queued G-code lines. This mirrors Grbl's own ISR-level interception of real-time bytes and is essential given SPJS's single deep queue. (Note: an early issue, #42, dealt with a stray '\n' being appended to real-time commands causing buffer overflow — a caution that real-time bytes must be sent without a trailing newline.)
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both incremental (fixed step-size buttons/keys) and continuous/hold jogging are offered by the XYZ jog widgets. Jogging uses the older pre-Grbl-1.1 style: relative G0/G1 moves (G91) streamed as motion commands, rather than Grbl 1.1's dedicated $J= jog command. This predates the $J= design, which matters for behavior and cancel semantics (below).
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > ChiliPeppr uses a crude, pre-1.1 cancel: the XYZ widget subscribes to a 'jogdone' event and, on jog release, fires an exclamation point '!' (feed hold) to make Grbl drop/stop its planner buffer and halt the jog immediately, sometimes followed by '%' (buffer wipe) or a soft reset (Ctrl-X). It does NOT use the modern real-time jog-cancel byte 0x85 or the $J= jog framework (both introduced in Grbl 1.1 after ChiliPeppr's core jog code was written). Because rapid relative jog moves can overrun the buffer, runaway/overflow was a real problem (see tinyg issue #6: 'the mill will ignore commands after a few jog moves', worked around with a Ctrl-X soft reset). For ArctZ this is a cautionary contrast: the '!'-feedhold approach is inferior to Grbl 1.1's $J= + 0x85 jog-cancel that CNCjs/gSender/UGS use for VirtualJoystick-style release handling.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > For Grbl, SPJS/widgets parse the classic angle-bracket status report (e.g. <Idle,MPos:...,WPos:...>) into machine state plus machine (MPos) and work (WPos) coordinates, feed and (later) spindle fields. The grbl widget also periodically issues $G to read the active modal/parser state (work coordinate system, units). Parsing targets Grbl 0.9/1.1-era formats. For TinyG, status/position arrives as JSON (sr/qr reports) pushed on demand.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Recognizes the Grbl state set surfaced in status reports: Idle, Run, Hold, Alarm (with lock/unlock handling), plus Home/Check on later firmware. UI reflects these (e.g. alarm shows a pink background and requires $X). It is a display/gating model rather than a formal internal state machine.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active polling for Grbl: SPJS emits the '?' real-time status query roughly every 250 ms so position streams back continuously (Grbl does not auto-push status). The grbl widget separately polls $G (modal state) about every 2 seconds, throttling during file playback. TinyG differs: it pushes position/queue reports (sr/qr) on demand rather than being polled. The 250 ms '?' cadence over a BLE notification channel would be a load concern — a relevant caveat for ArctZ.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Recognizes work coordinate systems G54-G59 and G20/G21 unit modes, distinguishes machine (MPos) vs work (WPos) coordinates from status reports, and exposes zeroing/go-to actions in the XYZ widget.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Plugin/widget architecture with a global publish/subscribe event bus (amplify.js via chilipeppr.subscribe()/publish()) and a require.js-style module loader (cpdefine()/cprequire()); widgets are wired visually with jsPlumb. This is NOT MVVM and does not map 1:1 to ArctZ's CommunityToolkit.Mvvm, but the event-bus decoupling between widgets is conceptually comparable to a message-driven ViewModel layer. Everything is jQuery/DOM-centric browser code.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Strong and architecturally the most instructive aspect for ArctZ. ALL serial communication, buffering, char-counting/queue-report flow control, and real-time byte prioritization live in the separate Go SPJS process; the browser is a pure UI of loosely-coupled widgets that only exchange JSON over one WebSocket and via the pub/sub bus. That comm core is fully reusable independent of the UI (any web app can drive SPJS). Caveat: the UI widgets themselves are tightly bound to ChiliPeppr's cpdefine/amplify/jQuery framework and are not portable to a native (e.g. Avalonia) shell, so it is the SPJS-as-comm-core boundary, not the widget layer, that ArctZ should emulate.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Grbl alarm/lock conditions are detected and surfaced (hard/soft limit violations, probe failure, and the locked state needing $X to unlock), with visual feedback (pink background) and halting of G-code execution until cleared.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > On serial port close/error SPJS notifies clients that the port is closed and does not auto-reconnect the controller link (operator re-opens it). The browser auto-reconnects its WebSocket to SPJS. In-flight streaming in the SPJS queue is stranded on link loss. See bt_reconnect_behavior for the Bluetooth-as-serial specifics.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes — a full Three.js/WebGL 3D viewer is the centerpiece widget (widget-3dviewer). It parses the loaded G-code into a 3D toolpath, shows live tool position, and includes a toolpath simulator; other widgets can inject 3D objects via pub/sub. A lighter tablet workspace exists without the 3D viewer.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > The 3D viewer parses G-code including arc moves (G2/G3) and tracks modal state (plane, units G20/G21, absolute/relative) to render and simulate the toolpath.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Front-end runs in any modern desktop browser as a cloud-hosted page (chilipeppr.com); a lighter 'tablet' workspace exists for touch devices. It requires the local SPJS binary, which runs on Windows, Mac, Linux, Raspberry Pi, and BeagleBone Black. Because the UI is browser-based and SPJS can run remotely, multiple browsers/devices can attach to one machine. However there is no native mobile app and no in-app Bluetooth — mobile use is browser-to-SPJS, with the serial/BT link terminating at the SPJS host. Contrast with ArctZ/Avalonia: ChiliPeppr achieves cross-platform reach via browser + a local Go server rather than a compiled native shared UI, and offers no native BLE path.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Reads/writes Grbl settings via the $ / $$ dump and edits them through a settings modal using $n=value syntax (parsing lines like '$0=755.906 (x, step/mm)'); $G reads parser/modal state. There is no FluidNC YAML-config upload path (FluidNC's YAML is handled by its own ESP32 WebUI, not ChiliPeppr). TinyG configuration is done via its JSON parameter set instead.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > SPJS is multi-client: several WebSocket clients can connect simultaneously and it broadcasts serial traffic to all of them, while they share the single underlying serial port to the controller.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > Separate controller-specific workspaces rather than auto-detection: workspace-grbl (Grbl 0.9/1.1) and workspace-tinyg (TinyG/g2core), plus Marlin support in SPJS's bufferflow. The user selects the controller by choosing the matching workspace/buffer type; there is no welcome-string/$I auto-identification of the dialect. FluidNC and grblHAL are not explicitly targeted (FluidNC would fall back to the Grbl workspace since it is Grbl-1.1-protocol compatible, but ChiliPeppr's jog/status code is 0.9/1.1-era).

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes — G38.2 touch-probe workflows are supported, and community auto-level/height-map widgets exist for surface probing (e.g. PCB leveling). Not a priority for ArctZ but confirms sender-grade probing capability.

### Прочая информация
- **type**: sender

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- os_bt_api
- mode_mutual_exclusion
- jog_latency_budget

---

## 4. CNCjs <a id="cncjs"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Node.js (Express) server backend + React/Redux web frontend, bundled with Webpack. Distributed as a headless Node server and as an Electron desktop app (Windows/macOS/Linux); also runs well on Raspberry Pi. Server and browser client communicate over WebSocket (socket.io).
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_: MIT
- **repository** _Ссылка на исходный код_: https://github.com/cncjs/cncjs
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Actively maintained. Latest release v1.11.2 (2026-06-30); most recent master commit 2026-07-01. Long-lived project (4,400+ commits, ~2.6k stars).

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Server-to-controller link is serial/USB only natively (Node.js `serialport` library via `SerialConnection.js`). WiFi and Bluetooth reach the controller only through an external serial bridge that presents a virtual COM/RFCOMM port to the OS (OS-level Bluetooth SPP, or an ESP8266/ESP32 serial-to-WiFi/Telnet bridge). There is NO native WebSocket, Telnet, or BLE transport to the controller inside the server. Separately, the browser client always talks to the CNCjs server over WebSocket (socket.io) — that WebSocket is the client<->server link, not the controller link.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Partial. `SerialConnection` is a thin wrapper over Node `serialport` exposing `open()/close()/write()` plus `data`/`close`/`error` events; each controller (GrblController, MarlinController, SmoothieController, TinyGController) holds a `this.connection` and calls `this.connection.write()`. Protocol/controller logic is cleanly decoupled from the connection object, BUT only one connection implementation (serial) exists — there is no polymorphic multi-backend transport interface (no `SocketConnection.js` in `src/server/lib`). So command-protocol/transport separation is good in principle, yet the transport is effectively single (serial). This is a useful but not fully generalized model for ArctZ's multi-transport (BLE/serial/WiFi) needs.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No native Bluetooth stack in CNCjs. Bluetooth is only reachable as Bluetooth Classic SPP (RFCOMM) exposed by the operating system as a virtual serial port (e.g. /dev/rfcomm0 or a Windows COM port), which CNCjs then opens like any other serial device. No BLE / GATT / Nordic UART Service (NUS) support exists in the codebase. This makes CNCjs a weak Bluetooth reference for ArctZ, whose primary target (ESP32-S3/C3 class FluidNC) is BLE-only.
- **bt_reconnect_behavior** _Поведение при разрыве связи — автопереподключение, feed hold/alarm станка_:
  > No Bluetooth-specific handling. When the underlying serial port drops (including a BT-SPP disconnect), `SerialConnection` emits a `close`/`error` event; the controller tears down and broadcasts `serialport:close` to all connected web clients. There is no automatic controller-link reconnect built into the server — the operator must reopen the port; any in-progress file stream (Sender workflow) is stopped/paused. The machine itself is not automatically feed-held by CNCjs on disconnect (GRBL keeps running its planner buffer until it empties). [uncertain on exact post-drop machine state — depends on firmware].
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > OS-level pairing/bonding is required to create the SPP virtual serial port; CNCjs itself performs no pairing and only opens the resulting COM/rfcomm device. Purely a serial-port-open model from CNCjs's perspective.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > The `Sender` class implements two selectable protocols: SP_TYPE_SEND_RESPONSE (0) — send one line, wait for `ok`/`error` before sending the next (synchronous) — and SP_TYPE_CHAR_COUNTING (1) — keep multiple lines in flight, tracking outstanding character counts against the controller RX buffer. The GrblController is hard-coded to CHAR_COUNTING (issue #252 requested runtime switching to send-response for troubleshooting). The separate `Feeder` class (manual/immediate commands) effectively uses a send-one-wait-for-`ok` model with its own pending flag.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > In char-counting mode an internal SPCharCounting helper maintains `bufferSize` (default 128 bytes, GRBL RX buffer), `dataLength` (bytes currently in flight), and a `queue` of per-line lengths; it only sends the next line if its length fits in the remaining buffer, and decrements `dataLength` as each `ok`/`error` acknowledgment arrives. Buffer size cannot be shrunk below current in-flight data. The 128-byte default matches GRBL's serial RX buffer; FluidNC's larger buffer would be under-utilized unless reconfigured.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > TWO SEPARATE QUEUES — this is CNCjs's signature design and the reference for ArctZ. `Feeder` (src/server/lib/Feeder.js) handles manual / real-time-ish / MDI / jog commands with its own queue, hold/unhold, and a single-pending flow (feed one, wait for `ok`). `Sender` (src/server/lib/Sender.js) handles bulk file streaming with the char-counting protocol, line/elapsed/remaining tracking, and its own hold/unhold. Both live inside GrblController, each with an independent dataFilter; both emit `data`/`hold`/`unhold`. Manual jogs and MDI go through the Feeder and are NOT interleaved into the streamed file's Sender queue, so operator commands stay responsive and separable from the running program.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time bytes bypass BOTH the Feeder and Sender queues entirely. In GrblController.writeln(), a command is classified real-time if it is a member of GRBL_REALTIME_COMMANDS (`?`, `!`, `~`, 0x18 soft-reset) or matches extended ASCII 0x80-0xFF (covers 0x84 safety-door, 0x85 jog-cancel, and feed/rapid/spindle overrides 0x90-0x9D). Real-time commands are written directly via `this.connection.write(data)` with no trailing newline and no queueing; all other commands get `\n` appended and flow through the normal path. This mirrors GRBL's own design where real-time bytes are intercepted by the ISR before the line buffer.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both incremental (fixed step distance) and continuous jogging are supported. Jogs are issued as GRBL `$J=` commands (G91 incremental or G90 absolute, G94 feed) sent through the feeder/gcode command path. Continuous jogging is implemented (notably by pendant plugins) by repeatedly sending short `$J=` motions on a timer (~200 ms) to keep GRBL's planner buffer full while a button/joystick is held.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > A dedicated `jogCancel` command sends the GRBL real-time byte 0x85 (jog cancel), which flushes queued jog motions and decelerates to a stop without an alarm (added in PR #512). Pendant/joystick implementations send jog-cancel when the control returns to neutral (key-up / stick-center). Because 0x85 is a real-time byte it bypasses the queues and stops motion immediately — the recommended pattern to prevent runaway if a key-up event is missed. Directly relevant to ArctZ VirtualJoystick release handling.
- **jog_latency_budget** _Транспортная задержка jog-команд и минимальный интервал отправки при удержании (особенно по BLE/WiFi)_:
  > Continuous-jog implementations use roughly a 200 ms resend interval for `$J=` motions, sized so that at max jog feed the commanded step is not consumed before the next command arrives (avoiding stutter) yet short enough that jog-cancel stops promptly. Total latency = browser->server WebSocket hop + server->controller serial write. Since the controller link is serial/USB, latency is low and deterministic; CNCjs has no native BLE/WiFi path, so it offers no direct data point on BLE jog latency budgets (a key gap for ArctZ, where BLE notification/write intervals dominate).

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > GrblLineParserResultStatus parses GRBL 1.1 `<...>` reports: machine state + substate, MPos and/or WPos (up to 6 axes x/y/z/a/b/c), feed & spindle (F or FS), buffer state Bf (planner blocks free + serial RX bytes free), work-coordinate offset WCO, overrides Ov (feed/rapid/spindle %), pin states Pn (limits/probe/door/control), and accessory state A (spindle dir / coolant). Regex-based field mapping.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Models the GRBL state set: Idle, Run, Hold, Alarm, Jog, Door, plus Check/Home/Sleep, with substates such as Hold:0/Hold:1 and Door:0-3. Drives UI enable/disable and workflow decisions.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active polling — the server periodically writes the `?` real-time query and parses the pushed `<...>` line (GRBL does not auto-push status). Polling is timer/throttle-driven inside GrblController (queryStatusReport with an action-time guard). [uncertain on exact cadence — a fast query loop plus a ~5 s safety-tolerance window was observed in source; not a fixed single documented interval]. Note: frequent `?` polling would load a BLE notification channel — relevant caveat for ArctZ.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Supports work coordinate systems G54-G59, distinguishes machine (MPos) vs work (WPos) coordinates, applies WCO from status reports, and reads offsets via `$#`. Zero-work-position and go-to actions exposed in the UI.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > React + Redux (Flux-style) component architecture on the client; event-driven Node.js server. Not MVVM — so it does not map 1:1 to ArctZ's CommunityToolkit.Mvvm pattern, but the state/store separation is conceptually comparable. Widget-based dashboard (each widget is a React component subscribing to server events).
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Strong separation and a notable strength. ALL controller/serial communication, the Feeder, the Sender, protocol parsing, and state live in the Node server; the browser/Electron client is a thin socket.io consumer that sends commands and renders pushed events (`serialport:read`, `serialport:write`, `controller:state`, `sender:status`, etc.). This cleanly reusable communication core is the aspect most worth emulating for ArctZ: a transport/protocol core independent of the UI, with the UI as one of potentially many clients.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > GRBL alarm and error codes are parsed by dedicated line parsers (GrblLineParserResultAlarm / GrblLineParserResultError) into code + human-readable message, broadcast to clients; on `error` during a stream the Sender pauses so the operator can intervene.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > On serial `close`/`error` the controller shuts down and notifies clients (`serialport:close`); there is no automatic controller-link reconnect in the server (operator reopens the port). The browser client, however, auto-reconnects its socket.io session to the server. In-flight Sender workflow is halted on link loss. See bt_reconnect_behavior for the Bluetooth-as-serial specifics.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > 3D toolpath visualizer in the client Visualizer widget rendered with Three.js/WebGL, showing tool path, machine/work origin, and live tool position; 2D top-view usable as well.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Uses a G-code parser/toolpath library (gcode-parser + gcode-toolpath) that tracks modal state and expands arcs (G2/G3, plus G17/18/19 planes) into line segments for the visualizer; handles G90/G91 and units modals.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Server runs on any Node.js host — Windows, macOS, Linux, and Raspberry Pi. The UI is browser-based, so any device with a modern browser (including phones/tablets via responsive layout) can control the machine, and multiple clients can connect simultaneously to one server/controller. Also packaged as an Electron desktop app for one-click use. Single shared JS codebase covers server + all clients. Contrast with ArctZ/Avalonia: CNCjs achieves cross-platform via the browser + a central server rather than a compiled native shared UI, and it has no native mobile app and (critically) no in-app Bluetooth — mobile control is browser-to-server, with the serial/BT link terminating on the server host.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Reads/writes GRBL settings via `$$` (settings dump), `$G` (parser/modal state), `$I` (build/version info), and `$#` (coordinate offsets); GRBL/grblHAL/FluidNC all answer `$$` so basic settings work across them. CNCjs does NOT provide FluidNC YAML-config upload (that is handled by FluidNC's own ESP32 WebUI, not CNCjs). [uncertain on any partial grblHAL extended-setting UI].
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > Multi-client by design: many WebSocket (socket.io) clients can attach to a single CNCjs server and share one controller/serial connection concurrently (advertised multi-client support); the single point of contention is the one serial link to the controller.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > Grbl (incl. Grbl-Mega), Marlin, Smoothieware, and TinyG/g2core, each with a dedicated controller class. The controller type is chosen by the user at connection time (a dropdown), NOT auto-detected from the welcome string. FluidNC and grblHAL are driven through the Grbl controller because they are Grbl 1.1-protocol compatible; there is no separate FluidNC/grblHAL dialect class or `$I`-based auto-identification.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes — a Probe widget supports single-axis touch-off probing via G38.2, and an Autolevel plugin builds a surface height-map (with bilinear interpolation added in recent releases, e.g. v1.11.x) for Z compensation on uneven stock/PCB. Not a priority for ArctZ but confirms full sender-grade probing support.

### Прочая информация
- **type**: sender

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- os_bt_api
- mode_mutual_exclusion

---

## 5. ESP3D-WebUI (FluidNC WebUI) <a id="esp3d-webui-fluidnc-webui"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > JavaScript single-page app that runs in the operator's browser, not a standalone installed program. WebUI v3 (used by FluidNC) is built with Preact + htm (hyperscript tagged-template components with hooks/context); the older v2 line was jQuery-based. The whole SPA is compiled/minified and gzip- or Brotli-compressed into a single index.html.gz that is flashed onto the ESP32's filesystem (LittleFS/SPIFFS) and served by the on-chip ESP3D/FluidNC HTTP server. Deliberately minimized to fit ESP32 flash. It is the 'native' UI of FluidNC / Grbl_ESP32 / grblHAL-on-ESP32 and also of ESP3D for 3D printers.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GPL-3.0 (package.json declares "(ISC OR GPL-3.0)"); several source-file headers carry GNU LGPL v3 notices. Effectively copyleft (GPLv3) for reuse purposes.
- **repository** _Ссылка на исходный код_:
  > https://github.com/luc-github/ESP3D-WEBUI (FluidNC ships a preconfigured build; see also bdring/FluidNC embedding). Runtime web-socket/HTTP API documented at https://esp3d.io
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Actively maintained. WebUI v3.1.0 released 2024-05-25 (added HTTPS/WSS, theme/language pack manifests, Brotli); maintenance release 3.0.1 on 2024-12-16; v3.0.0 stable 2024-10-09. Single primary maintainer (Luc Lebosse). FluidNC pins a specific WebUI build in its firmware releases, so the effective WebUI version depends on the FluidNC release installed.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Browser-to-ESP32 only, over WiFi. Two mechanisms: (1) HTTP(S) REST-style requests using ESP3D bracket commands ([ESPxxx]) and a /command endpoint for G-code, plus file-upload/download handlers; (2) a WebSocket for the live serial/terminal stream (subprotocol 'webui-v3', typically the web port +1 e.g. 81 on the Arduino build) and, in ESP3D V3, a second data WebSocket for binary file transfer. v3.1.0 auto-detects and uses HTTPS + WSS when available. There is NO serial/USB transport and NO Bluetooth transport in the WebUI itself: it always assumes an HTTP+WebSocket server, which on FluidNC is the same ESP32 running the motion firmware. WiFi can be STA (join your network) or AP (the board hosts its own SSID).
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Weak/absent as a general transport layer, by design. The WebUI is tightly coupled to the ESP3D web API (HTTP command endpoints + the webui-v3 WebSocket). It has no polymorphic transport interface that could swap in serial or BLE — everything is expressed as HTTP requests and WebSocket frames to the ESP3D server. Communication is centralized in a small number of helpers/context providers (a WebSocket context and an HTTP-command helper), so protocol handling is at least isolated from individual UI widgets, but it is not a reusable transport abstraction independent of ESP3D. As a direct architectural model for ArctZ's multi-transport (BLE/serial/WiFi) goal it is a poor reference; it is a good reference only for the WiFi-WebSocket path.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > Not applicable to the WebUI: ESP3D-WebUI cannot be reached over Bluetooth at all — it requires HTTP + WebSocket, which FluidNC exposes only over WiFi. FluidNC's own Bluetooth support (separate from WebUI) is Bluetooth Classic SPP (RFCOMM), i.e. a raw serial channel. Over that BT channel you use a plain serial terminal / a serial-capable sender, NOT the WebUI. So on this project Bluetooth and the WebUI are mutually exclusive worlds.
- **esp32_variant_support** _На каких вариантах ESP32 доступен BT-режим (WROOM/WROVER = SPP; S3/C3/C6/H2 = только BLE)_:
  > FluidNC Bluetooth (Classic SPP) exists only on the original ESP32 (WROOM/WROVER), because Bluetooth Classic/SPP hardware is present only on that die. ESP32-S3/C3/C6/H2 have no Bluetooth Classic (S3/C3 offer BLE only), and FluidNC does not implement a BLE serial channel, so those newer variants have effectively no FluidNC Bluetooth — their only wireless option is WiFi, i.e. the WebUI. Consequently, on the ESP32-S3-class boards that ArctZ is likely to target, the WebUI-over-WiFi path is the sole wireless route and BT is unavailable.
- **radio_coexistence** _Могут ли WiFi и Bluetooth работать одновременно на одной плате_:
  > No simultaneous operation. FluidNC selects a single radio mode via $Radio/Mode (Off / STA / AP / BT); WiFi and Bluetooth are mutually exclusive. Because the WebUI needs WiFi, switching FluidNC to Bluetooth mode disables the WebUI, and vice-versa. This is a firmware/radio constraint, not a WebUI choice.
- **firmware_build_variant** _Нужна ли отдельная сборка/переключение прошивки для включения BT_:
  > No separate firmware build is needed to toggle radios: a single FluidNC image contains WiFi + Bluetooth, and the mode is chosen at runtime with $Radio/Mode (or the WiFi/BT $ settings) and persists in config. The WebUI assets themselves are a build-time artifact embedded in the same firmware/flash, updatable independently by uploading a new index.html.gz.
- **bt_reconnect_behavior** _Поведение при разрыве связи — автопереподключение, feed hold/alarm станка_:
  > No Bluetooth path in the WebUI, so this is really WebSocket-reconnect behavior. The webui-v3 WebSocket uses a PING/PONG keepalive (server sends PING:<time_left>:<timeout> or PONG:<millis>) and the client detects drops and attempts to reconnect, re-reading controller state on reconnect. There is also a single-active-client model (currentID/activeID): when another browser connects, the server broadcasts activeID and the previously active client is notified it is no longer the controlling session. The machine itself is not fed-held by the WebUI on a WebSocket drop — if a job is running from the controller's SD card, FluidNC keeps executing it independently of the browser connection (a key point: WebUI disconnect does not stop an SD job).
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > Not applicable to the WebUI. FluidNC's Bluetooth (when used instead of the WebUI) relies on OS-level Bluetooth Classic pairing/bonding, advertising a device name (e.g. 'FluidNC'); the host OS creates an SPP virtual COM/rfcomm port. The WebUI performs no pairing — it only ever opens HTTP/WS over an existing WiFi connection.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Not applicable: the WebUI is a web page, so on every platform (Windows, Android, iOS, desktop Linux/macOS) it needs only a modern browser plus WiFi/IP reachability to the ESP32 — no CoreBluetooth, no Web Bluetooth, no native BT API is used. The iOS-no-SPP limitation is moot for the WebUI because it never uses Bluetooth; it is relevant only if one abandons the WebUI to use FluidNC's SPP channel, which iOS cannot open at all.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Two-tier. (1) The command terminal / manual-command box sends single G-code (or [ESPxxx]) lines and shows the ok/error and streamed serial replies over the WebSocket — a simple send-then-observe model. (2) For running a whole program, the intended workflow is to upload the .gcode/.nc file to the controller's SD card or flash and then run it (ESP filesystem run command / FluidNC's SD job runner, e.g. $SD/Run). In that case FluidNC's firmware does the actual line-by-line execution and buffer management internally — the browser is not the streaming engine and does not perform host-side character-counting. This offloads flow control to the ESP32, which is the whole point of an on-controller WebUI, but it means the WebUI is not a full-featured host streamer like CNCjs/UGS.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > There is no CNCjs-style dual feeder/sender queue inside the browser. Manual commands, jog buttons, and real-time actions are issued as individual requests (over HTTP /command or the WebSocket) and relayed by ESP3D to the controller; program execution is a separate UI action that hands a file to the firmware's SD job runner. So the separation between 'manual/jog' and 'file streaming' is achieved structurally by delegating the file run to firmware, rather than by two host-side queues. The firmware (FluidNC) owns the real command/planner queue.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > The WebUI exposes dedicated buttons for real-time control — Soft-Reset (Ctrl-X / 0x18), Feed Hold (!), Cycle-Start/Resume (~), and Status query (?). These are sent to FluidNC, which interprets them as real-time bytes and injects them ahead of the line buffer in firmware (the classic GRBL real-time interception). The actual bypass happens inside FluidNC, not in the browser; the WebUI's role is only to emit the correct character(s) immediately rather than queueing them behind a file.
- **mode_mutual_exclusion** _Блокировка jog во время файлового стриминга и наоборот (GRBL lockout error)_:
  > Enforced by the firmware rather than the WebUI. FluidNC rejects $J= jog commands unless it is in Idle/Jog state (e.g. during an active program run it returns an error), so jog-during-stream lockout is provided by the controller's state machine. The WebUI does not appear to add its own hard client-side block; it relies on FluidNC's state gating.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses FluidNC's GRBL 1.1 style status reports (the <...> lines) to drive a DRO/status panel: machine state, MPos and/or WPos, feed and spindle (F / FS), and pin/probe states where present. Fields are read from the serial stream delivered over the webui-v3 WebSocket and rendered into the position and state widgets.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Recognizes and displays the FluidNC/GRBL state set — Idle, Run, Hold, Jog, Alarm, Door, Home, Check, Sleep — using it to color/enable the status area and gate certain controls. The authoritative state machine lives in FluidNC; the WebUI mirrors it.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Shows machine (MPos) and work (WPos) coordinates and provides zero/home buttons; work coordinate systems (G54-G59) and offsets come from FluidNC via $G/$#. Coordinate math itself is the firmware's; the WebUI displays and issues zeroing commands.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Component-based reactive UI, not classic MVVM. WebUI v3 uses Preact + htm with hooks and context providers (state held in contexts such as the settings/data and WebSocket contexts); v2 used jQuery/imperative DOM. It does not map cleanly to ArctZ's CommunityToolkit.Mvvm (ObservableProperty/RelayCommand) pattern, though the context/hooks state model is loosely comparable to a shared observable store.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Moderate. Communication is concentrated in a WebSocket context/provider plus HTTP command helpers, and UI widgets consume state from context rather than opening sockets themselves — so there is a recognizable comm layer distinct from the views. However, that layer is inseparable from the ESP3D web API and from the single-SPA build: it is not a portable transport/protocol core that could be reused outside a browser or against a non-ESP3D transport. For ArctZ this is a much weaker separation than CNCjs's server-side core; it is instructive mainly for how a thin WebSocket client mirrors firmware state.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > FluidNC alarm and error codes/messages appear in the WebUI terminal/console as they are streamed over the WebSocket, and error states surface in the status area; the operator clears alarms by sending the unlock/reset commands (e.g. $X, Ctrl-X) from the panel. The WebUI relays and displays them rather than maintaining its own alarm-code table.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > The webui-v3 WebSocket has PING/PONG keepalive and client-side reconnection; on reconnect the client re-establishes the session (currentID/activeID handshake) and re-reads controller state. HTTP command calls are stateless requests retried by user action. There is no controller-link reconnect concept because the 'link' is the ESP32 itself; if WiFi drops, any SD-card job on FluidNC continues running independently of the browser.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Universal via the browser and zero-install. Because the UI is served from the ESP32 over WiFi, any device with a modern browser — Windows/macOS/Linux desktops, Android and iOS phones/tablets — can control the machine with no app to install, from a single embedded codebase. This is its standout strength and a philosophical contrast to ArctZ/Avalonia: cross-platform is achieved by shipping a web page from the controller rather than compiling a native shared UI. The trade-offs: it requires WiFi and the ESP32 as the server, offers no in-app Bluetooth, no native OS integration, and is constrained by ESP32 flash/RAM and a small concurrent-client limit.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Strong, and a core purpose of the WebUI. It can view/download/upload the FluidNC config.yaml to the controller's flash (a built-in file/config editor), read and change ESP3D-level settings via [ESPxxx] bracket commands (WiFi, radio mode, notifications, etc.), and issue GRBL/FluidNC $ settings and $$/$Report dumps. So it covers both the YAML-config path (FluidNC) and the $-settings path.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > Small and enforced. The ESP32's async web/WebSocket server supports only a limited number of simultaneous WebSocket clients (commonly cited around ~5), and on top of that ESP3D enforces a single active controlling client via the currentID/activeID scheme — when a new browser connects, the server broadcasts the new activeID and the previous client is demoted/notified. In practice one operator at a time is expected; extra connections are discouraged because they strain ESP32 RAM and can disrupt the active session.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > ESP3D-WebUI is multi-target: the underlying ESP3D 'target firmware' setting ([ESP800]/[ESP420]) can be a CNC dialect (grbl / Grbl_ESP32 / FluidNC / grblHAL-on-ESP32) or a 3D-printer firmware (Marlin/Repetier/Smoothieware). The FluidNC-embedded build is preconfigured for FluidNC. The dialect is therefore determined by the ESP3D target-firmware configuration, not by welcome-string / $I auto-detection at runtime.

### Прочая информация
- **type**: sender / firmware-embedded-client

### Неопределённые поля (uncertain)
- bt_mtu_packet_size
- bt_throughput
- rx_buffer_handling
- jog_mode_types
- jog_cancel_mechanism
- jog_latency_budget
- status_report_polling_model
- preview_rendering
- arc_modal_parsing
- probing_support

---

## 6. Fluid-controller (gjkrediet) <a id="fluid-controller-gjkrediet"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Arduino/C++ firmware for the ESP32, written as a single-file sketch (arduino/PendantV2_45.ino, ~1170 lines). Targets a LilyGO TTGO T-Display V1.1 board (original ESP32 with built-in 1.14" ST7789 TFT and Li-ion charger). Uses Arduino-ESP32 core libraries: BluetoothSerial (esp_spp), TFT_eSPI, RotaryEncoder, PinButton, ArduinoOTA, EEPROM, esp_wifi. Compiled with the 'minimal SPIFFS' partition scheme. It is embedded firmware, not a PC/phone application.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_: GPL-3.0 (LICENSE file in repo, GitHub spdx_id GPL-3.0).
- **repository** _Ссылка на исходный код_:
  > https://github.com/gjkrediet/Fluid-controller (KiCad PCB 'pendant v2', 3D-printed case, single Arduino sketch). Community derivatives: https://github.com/AC8L/FluidNC-Pendant (WiFi/WebSocket TCP:81 adaptation) and a TCP variant by Sardar Azari.
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Low but not abandoned. Repo created 2023-01-24; most substantive commits in Feb 2024; the most recent commit is 2026-05-03 (pushed_at 2026-05-03). 87 commits total, single primary author (gjkrediet). No tagged releases; distributed as source only. Effectively a personal hobby project maintained sporadically.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Bluetooth Classic SPP (RFCOMM) ONLY as the control link to FluidNC. The ESP32 runs BluetoothSerial in MASTER role (SerialBT.begin("ESP32test", true)) and actively connects out to the FluidNC controller's SPP device name (default 'FluidNC'). WiFi (WIFI_STA) is present but used solely to enter Arduino OTA firmware-update mode (hold the red button at power-on); it is never used to reach the CNC controller. There is NO serial/USB link (the pendant is a separate battery device, not tethered), NO BLE, NO Telnet, NO WebSocket in this original project. The AC8L fork replaces the BT link with a WiFi WebSocket (TCP port 81) client, but that is a different codebase.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Essentially none. The transport is hard-wired to BluetoothSerial. All controller I/O goes through two thin wrappers: btWrite(char) -> SerialBT.write() for single real-time bytes, and btPrintln(char*) -> SerialBT.println() for line commands, plus direct SerialBT.read()/SerialBT.available() in the status parser. These wrappers only guard on a btConnected flag; they are not a polymorphic transport interface. Swapping to WiFi/WebSocket required forking the whole sketch (AC8L). As an architectural model for ArctZ's transport abstraction it is a negative example: it shows a minimal 'two functions + a connected flag' facade that is enough for a fixed single transport but does not decouple protocol from link.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > Bluetooth Classic SPP (RFCOMM), via the ESP-IDF esp_spp / Arduino BluetoothSerial API. NOT BLE, NOT GATT/NUS. The pendant is the SPP master/initiator; FluidNC's built-in Bluetooth (also Classic SPP) is the slave. Data is an unstructured byte stream carrying GRBL/FluidNC ASCII commands and real-time bytes, identical to what a USB serial link would carry.
- **esp32_variant_support** _На каких вариантах ESP32 доступен BT-режим (WROOM/WROVER = SPP; S3/C3/C6/H2 = только BLE)_:
  > Requires an original ESP32 (WROOM/WROVER class) on BOTH ends because the link is Bluetooth Classic SPP, which exists only on the original ESP32 die. The pendant hardware is a LilyGO TTGO T-Display (original ESP32). ESP32-S3/C3/C6/H2 have no Bluetooth Classic (BLE-only or no radio) and therefore cannot run this SPP-master firmware, nor can they be the FluidNC target for it. This is a hard constraint directly relevant to ArctZ: if the target FluidNC board is an S3-class chip, this BT-SPP approach is not available at all and only WiFi remains.
- **radio_coexistence** _Могут ли WiFi и Bluetooth работать одновременно на одной плате_:
  > No simultaneous WiFi + Bluetooth for control. On the FluidNC side, the README states plainly that 'bluetooth and wifi can not be used at the same time and, in fact, need different fluidnc firmwares' - so the controller must be flashed/configured for BT to use this pendant. On the pendant side, WiFi (OTA) and Bluetooth (operation) are mutually exclusive modes chosen at boot: normal boot connects BT; booting with the red button held brings up WiFi/OTA instead. So both ends treat the two radios as either/or.
- **firmware_build_variant** _Нужна ли отдельная сборка/переключение прошивки для включения BT_:
  > Yes on the controller: FluidNC must run a Bluetooth-enabled configuration/firmware (BT radio mode), which per the README is a different firmware setup than the WiFi one. On the pendant itself a single firmware image contains both BT-operation and WiFi-OTA paths, selected at runtime by the boot button rather than by separate builds.
- **bt_reconnect_behavior** _Поведение при разрыве связи — автопереподключение, feed hold/alarm станка_:
  > Connection tracked by a btConnected flag updated in an esp_spp callback (btCallback): ESP_SPP_OPEN_EVT/DATA_IND/WRITE set it true; ESP_SPP_CLOSE_EVT and ESP_SPP_CONG_EVT set it false. When disconnected, btWrite/btPrintln become no-ops and mState is forced to 'Unknwn' (the DRO shows unknown/no-connection). checkConnectBt() retries SerialBT.connect() up to 5 times; if all 5 fail it prints 'No connection / Shutting down' and calls ESP.restart() (full reboot, which re-runs the initial connect). Crucially, the pendant does NOT command a feed-hold on link loss - it relies on the fact that jog motions are bounded (each $J= commands only a small computed distance and, if a keep-alive $J= stops arriving, GRBL/FluidNC simply finishes the last short move), so a dropped link makes the machine coast to a stop rather than run away. This is a deliberate safety property of the incremental-jog streaming model, not an explicit disconnect handler.
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > Programmatic connect-by-name at the firmware/stack level, no user pairing UI. The pendant (SPP master) calls SerialBT.connect(name) with name = "FluidNC" (the FluidNC BT advertised name; an alternate 'btgrblesp' is present commented out). Bonding/authentication is handled by the ESP-IDF Bluetooth stack's default SSP; there is no PIN entry or OS-level pairing dialog because neither end is a phone/PC. To point the pendant at a different controller you edit the 'name' string and reflash.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Not an OS/phone API at all - it uses the ESP-IDF/Arduino BluetoothSerial (esp_spp_*) API on the ESP32 as SPP master. There is no Windows/Android/iOS/Web Bluetooth code because the pendant is dedicated hardware, not an app on a general-purpose OS. This is worth noting for ArctZ: the SPP path this project uses is exactly the one iOS forbids to third-party apps (no public SPP/RFCOMM access, MFi-only), so an ArctZ iOS client could not replicate this Bluetooth approach - it would need BLE or WiFi instead.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Fire-and-forget, status-poll driven - NOT character-counting and NOT true send-response ok/error handshaking. The pendant never parses 'ok'/'error' acknowledgements; its receive loop (getGrblState) only scans the incoming stream for '<...>' status reports and ignores everything else. Commands (jog strings, $ commands, M-codes, probe macro lines) are simply pushed out with SerialBT.println back-to-back, and machine progress is inferred by polling '?' status. This works because the workload is short interactive commands and small jog moves rather than long programs, but it means there is no host-side flow control or line-buffer accounting. This is a key contrast for ArctZ: adequate for a jog pendant, insufficient for reliable full-file G-code streaming.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > No host-side RX-buffer accounting. The pendant does not track FluidNC's ~128-byte serial receive buffer or count outstanding bytes/lines; it depends on issuing only a few short commands at a time and on FluidNC's own buffering. There is no risk-managed buffer-fill logic like UGS/CNCjs. For jog, self-throttling comes from the resend interval (see jog_latency_budget), not from buffer feedback.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > No explicit queue at all - a single procedural loop() decides, on each pass, what one thing to send (a jog keep-alive, an override byte, a menu command, or a status poll), so there is effectively one implicit command path and no CNCjs-style feeder/sender separation. Because the device only does interactive jog/control and never streams files, the problem the feeder/sender split solves does not arise here. Mutual exclusion between actions is achieved by the current state (mState/pState/rState) gating which branch runs, not by separate queues.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time single bytes are sent immediately via btWrite() and thus bypass any line buffering, exactly matching GRBL/FluidNC real-time semantics. Implemented: '?' status query (sent every poll, ~198 ms), '!' feed-hold (when state==Run), '~' cycle-start/resume (when state==Hold), 0x18 (24, soft-reset), 0x84 (132, safety-door), 0x85 jog-cancel, and the override bytes 0x91/0x92 (feed +/-10%), 0x9A/0x9B (spindle +/-10%), 0x95/0x96/0x97 (rapid 100/50/25%). These are written as raw bytes so FluidNC intercepts them ahead of the line buffer. Line commands go through btPrintln() instead. The separation of 'raw byte' vs 'line' at the wrapper level is a clean, minimal realtime-bypass model ArctZ can mirror.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both continuous (hold-to-move) and incremental (step) jogging, using GRBL 1.1 $J= jog commands (G21 mm, G91 relative). (1) Continuous: while the analog PSP joystick is deflected, in the main loop the pendant recomputes a per-interval travel distance from stick deflection (a non-linear curve mapping deflection to feed) and emits '$J=G21G91X..Y..Z..F..' roughly every tExec=1000 ms, keeping the machine moving as long as the stick is held; the F/distance scale with jogSpeed set by the encoder. (2) Incremental: rotating the encoder in an axis mode emits a single '$J=G21G91X0Y0Z<step>F1000' style move of a fixed small step (~0.001 * jogSpeed mm). Z-jog is engaged by holding the red button while moving the joystick. No absolute ($J=G90) jog is used for manual motion (G90/G53 appear only in the go-to-origin macro).
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > Explicit and robust. On stick return-to-center or state changes, forceEndJog() sends 0x85 (jog-cancel), waits, polls status, and LOOPS - resending 0x85 every 100 ms until getGrblState reports the machine has left the Jog state. This guard specifically prevents a 'runaway' if a single cancel byte is lost or a stick-release event is missed: it will not stop resending 0x85 until confirmed out of Jog. Combined with the bounded per-interval jog distance, this gives two independent safeguards against continued motion. This is a strong pattern for ArctZ's joystick jog-cancel design.
- **mode_mutual_exclusion** _Блокировка jog во время файлового стриминга и наоборот (GRBL lockout error)_:
  > Continuous XYZ jogging is gated to only run when mState==Idle (or already Jog) and pState==Pendant and the encoder is not in spindle mode (checkJoystick returns early otherwise), so the pendant will not start jogging during other states. There is no file streaming in this project, so the classic 'jog vs file-stream' lockout is moot; FluidNC itself would reject $J= if not in Idle/Jog, and the pendant additionally avoids issuing it. Feed-hold '!' is only offered when Run, resume '~' only when Hold - state-driven button behavior enforces exclusivity.
- **jog_latency_budget** _Транспортная задержка jog-команд и минимальный интервал отправки при удержании (особенно по BLE/WiFi)_:
  > The keep-alive resend interval for held continuous jog is tExec = 1000 ms, i.e. each $J= is dimensioned to cover ~1 s of motion and is refreshed about once per second; status is polled at updateInterval = 198 ms. Over Bluetooth Classic SPP the round-trip is low and fairly deterministic (better than WiFi), but the 1 s resend cadence means the practical stop-latency after releasing the stick is dominated by the jog-cancel loop (0x85 every 100 ms until confirmed) rather than by transport delay. Actual on-wire BT latency is not measured in the project. For ArctZ note the trade-off: a long (1 s) keep-alive distance reduces command rate/BT load but increases the coast distance if cancel is missed - hence the explicit repeated-cancel safeguard.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses GRBL 1.1 / FluidNC '<...>' status reports directly from the SPP stream, character by character, accumulating until newline. State is derived from character positions of the state word (e.g. charAt(1)=='I'->Idle, 'R'->Run, 'A'->Alarm, 'J'->Jog, 'D'->Door; 'm' at index 3 ->Home; 'd' at index 4 ->Hold). It then string-searches for 'MPos:' (machine XYZ via convertPos), '|FS:' (takes the second field as reported spindle speed), 'WCO:' (work-coordinate offset via convertPos), and '|Ov:' (feed,rapid,spindle override percentages via convertOverride), plus '|A:' accessory flags to detect spindle-on ('S'). Only these fields are consumed; other report fields are ignored. Parsing is ad-hoc substring/charAt, not a general tokenizer.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Enumerated machine states mirrored from the controller: Alarm, Idle, Run, Hold, Door, Home, Jog, plus a local 'Unknwn' used when disconnected. Detection is by the leading characters of the status state word (see status_report_format). The state drives UI color/labels and gates actions (jog allowed only in Idle/Jog, hold/resume offered per Run/Hold, etc.). There is no separate Check/Sleep handling. The authoritative state lives in FluidNC; the pendant is a mirror + gate.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active polling only. The pendant writes '?' and reads whatever '<...>' report comes back, on a ~198 ms cadence (updateInterval) in the main loop, and additionally polls on demand around jog/menu actions. It does not rely on FluidNC's auto-report ($Report/Interval) push; it drives the cadence itself. Each poll is a full round-trip. For ArctZ this is the simple, predictable model, but a fixed ~5 Hz '?' poll over a wireless link is exactly the kind of channel load to weigh against BLE notifications.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Displays both machine (MPos) and work coordinates (derived via WCO from the status report). Work-zero is set with 'G10 P1 L20 X0 Y0' (XY) and 'G10 P1 L20 Z0' (Z) into G54/P1. A 'go to work origin' macro raises Z with '$J=G53Z0F1000' (machine coords) then jogs to 'X0 Y0'. The probe macro uses 'G10 L20 P1 Z21.35' to set Z after touch-off. So it works in G54/P1 work coordinates and uses G53 for safe machine-coordinate Z retract.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > None of MVVM/MVC - it is bare procedural embedded firmware. A single loop() polls inputs (joystick, encoder, three buttons via PinButton/RotaryEncoder), mutates global state variables (mState, pState, rState, jogSpeed, etc.), sends BT commands, and repaints the TFT via TFT_eSPI immediate-mode calls. UI 'state' is just global enums and force-redraw flags. There is no data-binding, no observable pattern, no separation of view/model - the opposite of ArctZ's CommunityToolkit.Mvvm approach.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Very weak / none. Communication (SerialBT read/write), parsing (getGrblState), business logic (jog math, menu handling), and rendering (tftPrint*) all live intertwined in one ~1170-line .ino with shared globals. The only 'layer' is the btWrite/btPrintln pair. getGrblState even calls tftUpdate() directly, coupling the status parser to the display. It is not a reusable comm core; as an ArctZ reference it illustrates what to avoid, though the tiny btWrite/btPrintln + status-poll nucleus is a useful minimal example of the FluidNC command surface.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Handled by state, not by decoding numeric alarm codes. When mState==Alarm the pendant sends '$X' (unlock) - available both as a menu 'Unlock' action and automatically in some button paths; a soft-reset (0x18) followed by '$X' then 0x84 (door) sequence is used to recover from a Door/hung state. It does not parse or display 'ALARM:n' code numbers or maintain an alarm-code table; it just offers unlock/reset.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > See bt_reconnect_behavior. Summary: esp_spp callback maintains btConnected; on loss mState becomes Unknwn and sends are suppressed; checkConnectBt() retries SerialBT.connect() up to 5 times and, on total failure, reboots the ESP32 (ESP.restart()) which re-attempts the initial connect. No exponential backoff, no partial-state resync beyond re-polling '?' after reconnect. Recovery is essentially reconnect-or-reboot.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > None. The TFT shows only a numeric DRO (machine/work X-Y-Z), spindle speed/state, jog speed/step, override percentages, machine state, and battery - no 2D/3D toolpath preview. Toolpath visualization is out of scope for a jog pendant.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > None. The pendant neither generates nor parses G2/G3 arcs and does not track modal G-state for preview; the only modal codes it emits are G21/G91/G90/G53 inside jog and macro strings. No arc interpolation or modal-state model exists.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Single fixed hardware target: the LilyGO TTGO T-Display (original ESP32) pendant. It is not cross-platform software - there is no desktop/mobile/web build, no shared UI core. 'Portability' happens only through community forks that rewrite the transport (AC8L's WiFi/WebSocket version, a TCP version). This is the philosophical opposite of ArctZ/Avalonia's one-codebase-many-platforms model: here 'the platform' is the dedicated device itself.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Minimal and command-specific; it does NOT expose a settings editor. It never issues '$$'/'$Report' dumps or uploads/downloads FluidNC config.yaml. It only sends the specific commands it needs: '$X' (unlock), '$H' (home), 'G10 P1 L20 ...' (work offsets), '$J=' (jog), 'M3/M5 S...' (spindle). A source comment notes that the firmware's jogSpeedMax constant 'must match the settings in config.yaml' - i.e. configuration coupling is manual/compile-time, with no runtime settings management.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One. A single SPP master-to-slave link between the pendant and the FluidNC controller. No multi-client concept (that concern belongs to WiFi/WebUI setups, not this BT pendant).

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > Targets FluidNC specifically (connects to the FluidNC BT device name and depends on FluidNC's Bluetooth SPP), but the command/protocol surface it uses is standard GRBL 1.1 real-time + jog protocol ($J=, '?', '!', '~', 0x18/0x84/0x85, feed/rapid/spindle override bytes, '<...>' status, G10 L20), so it is compatible with GRBL-1.1-compatible controllers exposing an SPP serial link. There is NO welcome-string/$I auto-detection of dialect - the controller identity is assumed and set by the hard-coded BT device name in the sketch. grblHAL is not specifically addressed.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes - a hard-coded Z touch-probe macro (menu 'probe'): 'G21G91' -> 'G38.2 Z-10 F100' (fast probe) -> 'G0 Z0.3' (retract) -> 'G38.2 Z-2 F10' (slow re-probe) -> 'G10 L20 P1 Z21.35' (set Z accounting for a fixed probe-plate/tool thickness) -> 'G91' -> 'G0 Z8.65' (lift) -> 'G90'. This is a single-point Z tool/work-height touch-off. There is no surface height-map / autoleveling grid probing.

### Прочая информация
- **type**: hardware pendant (ESP32 firmware). NOTE: the task metadata described it as an 'Android/iOS app, Telnet/WiFi' client, but that is a conflation with the unrelated 'Fluid Control' Android app (com.arhiled.fluidcontrol). The actual gjkrediet/Fluid-controller is a self-contained wireless hardware jog pendant whose ESP32 talks to FluidNC over Bluetooth Classic SPP.

### Неопределённые поля (uncertain)
- bt_mtu_packet_size
- bt_throughput

---

## 7. Grbl-Plotter <a id="grbl-plotter"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > C# on .NET Framework (project self-describes as 'DotNET 4.0', built with Visual Studio 2022), Windows-only WinForms GUI application. Uses System.IO.Ports.SerialPort (with a SerialPortFixer workaround) for serial, a raw TCP socket path for Ethernet, and SharpDX/DirectInput for USB gamepad. UI is a set of WinForms partial classes (MainForm*.cs) plus reusable UserControls in MachineControl/. No separate headless/core library — the app is a monolithic desktop GUI. Its specialty is advanced 2D vector import (SVG/DXF/HPGL) and 2D G-code transformation/visualization, not 3D.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GPL-3.0 (GNU General Public License v3.0, per the GitHub repo license and file headers). Strong copyleft — usable as a design reference but source cannot be copied into an MIT/closed ArctZ. The README also adds a plain-language 'free, use at your own risk, no warranty' note.
- **repository** _Ссылка на исходный код_:
  > https://github.com/svenhb/GRBL-Plotter (author Sven Hasemann / 'svenhb'). Project page: https://svenhb.github.io/GRBL-Plotter/ . Wiki: https://github.com/svenhb/GRBL-Plotter/wiki
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Active. GitHub API (2026-07-24) shows default branch master pushed 2026-06-13, repo updated 2026-07-20+, ~21 open issues, primary language C#. Source file headers carry a running changelog with 2025-2026 dates (e.g. '2025-03-04 $I customization string'). Regular tagged releases are published on the Releases page. Single-maintainer project.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Two host-side transports, selected per connection instance: (1) Serial/USB via System.IO.Ports.SerialPort (SerialPortDataSend branches on `if (!useEthernet) serialPort.Write(...)`), configured by cbPort/cbBaud, with a SerialPortFixer for driver quirks; (2) Ethernet/TCP via a raw socket wrapped in a `Connection` object with a StreamReader (`if (useEthernet) Connection.Write(...)`; toggled by the `CbEthernetUse` checkbox / `useEthernet` flag / Settings.serialEthernetUse1|2) — intended for grblHAL Ethernet boards and ESP raw-telnet bridges. The app can host up to three of these connection instances simultaneously (`iamSerial` = 1/2/3: main controller + 2nd GRBL + 3rd), each driving one controller. There is NO native WebSocket transport and NO native Bluetooth stack. Marlin firmware is also spoken over the same serial path.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Weak — a cautionary reference for ArctZ. There is no polymorphic transport interface; the choice between serial and TCP is a hard boolean branch (`useEthernet`) inside the send/receive methods of the ControlSerialForm partial class: `SerialPortDataSend()` does `if (!useEthernet) serialPort.Write(...) else Connection.Write(...)`, and the receive loop likewise branches between the SerialPort DataReceived event and `reader.ReadLine()` on the TCP stream. The GRBL/Marlin protocol, character-counting streaming, status polling and state machine all live in the same WinForms UserControl (ControlSerialForm*.cs, a multi-file partial class), fused with the transport branch rather than sitting on top of an abstract transport. So transport is NOT cleanly separated from protocol — the opposite of the transport-interface + protocol-core split ArctZ wants. The one positive is that this whole serial-control unit is itself encapsulated and instantiated up to 3× for multi-controller use, so the protocol layer is at least reused, just not decoupled from the WinForms host or the physical link type.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No Bluetooth support of any kind (no BLE/GATT/Nordic-UART, no Bluetooth Classic SPP/RFCOMM in code). Bluetooth is only usable indirectly if the OS exposes a paired Classic-SPP device as a virtual COM port, which Grbl-Plotter's SerialPort path would then open like any serial port. Provides no reusable BLE design for ArctZ.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Grbl-Plotter uses no native Bluetooth API on any platform — no WinRT BLE, no Android/iOS/Web Bluetooth (it is Windows-desktop-only). It reaches a controller only through System.IO.Ports.SerialPort or a raw TCP socket. None of ArctZ's mobile/web BT APIs are exercised.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Classic GRBL character-counting (send-ahead) streaming, and a clean readable reference for it. Grbl.RX_BUFFER_SIZE = 127 (the Arduino GRBL serial RX buffer). Two tracked quantities: `grblBufferSize` (=127) and `grblBufferFree`, plus a `streamingBuffer` object exposing IndexSent, IndexConfirmed, Count, GetSentLine()/GetSentLineNr(), GetConfirmedLine()/GetConfirmedLineNr(). PreProcessStreaming() copies lines from streamingBuffer into the send path while `grblBufferFree >= lengthToSend` (streaming ahead), decrementing grblBufferFree by each sent line's byte length; when GRBL returns 'ok'/error the confirmed line's length is added back to grblBufferFree, freeing room for the next lines. A StreamingMonitor wakes PreProcessStreaming to keep the pipe full. In $Check mode grblBufferSize is deliberately reduced to 100 'to avoid fake errors'. This is true character counting, not naive wait-for-ok.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Modeled with a single fixed constant `Grbl.RX_BUFFER_SIZE = 127` ('grbl buffer size inside Arduino'). It is NOT negotiated from the controller — every GRBL/grblHAL/FluidNC target is driven with the same 127-byte assumption, so a controller with a larger RX buffer (grblHAL/FluidNC) is under-utilized and a smaller one could theoretically overflow. The only dynamic adjustment is lowering the effective size to 100 during $Check streaming. Comment logging tracks grblBufferFree/grblBufferSize throughout streaming for diagnostics.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Single unified send pipeline centered on `streamingBuffer` + `grblBufferFree`, NOT the separate feeder/sender pair of CNCjs. File lines, tool-change macros, injected variable lines and interactive commands all funnel through RequestSend -> ProcessSend -> sendLine, sharing the one 127-byte character-count budget. Jog/gamepad motion does not use a distinct queue either — it also goes out through the serial-form send methods and is gated against the same buffer (the gamepad checks `_serial_form.GetFreeBuffer() >= 99` before sending). Real-time single bytes are the one exception (see realtime_bypass_path). The streamingBuffer additionally understands special keyword lines ($TS/$TO/$TI/$TE for tool-change staging) that are matched when confirmed, but they still live in the same queue.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time single-byte commands bypass the streamingBuffer / character-count accounting and are written straight to the link. `SerialPortDataSend('?')` is fired from the status timer (never counted; logging explicitly skips `tmp == "?"`); soft-reset is sent as the raw byte 0x18/Ctrl-X (`new byte[]{24}`); feed-hold/resume and the jog-cancel 0x85 are emitted via a `SendRealtimeCommand(int)` helper (gamepad calls `SendRealtimeCommand(133)`, i.e. 0x85, when the stick recenters). Because these go directly to SerialPort.Write / TCP Connection.Write regardless of grblBufferFree, they are delivered immediately even when the streaming buffer is full — exactly the immediate-dispatch behavior ArctZ needs for a responsive joystick and e-stop.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Two interactive paths. (1) UI jog buttons (ControlMoveXY / manual controls) do step/incremental jogging using the GRBL 1.1 jog feature (Features.md lists 'Jogging (GRBL 1.1)') with selectable step size and feed. (2) USB gamepad/joystick (MainFormGamePad.cs, ControlGamePad.cs) does continuous, proportional jogging: `ProcessGamePadNew()` computes `stepWidth = 1.2f * feedRate * jdir * invert * gamePadTimer.Interval / 60000`, scaling feedrate from stick deflection (`feedRate = maxSpeed * (maxValue - deadzone)/(32767 - deadzone)`), and re-sends a G91 relative move each timer tick while the stick is held. There is no absolute-target jog mode. Note the divergence worth flagging for ArctZ: the gamepad path streams repeated small relative moves rather than a single soft-limit-bounded '$J=' move, relying on the 0x85 cancel at recenter to stop.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > Uses the GRBL 1.1 real-time jog-cancel byte 0x85, sent as `SendRealtimeCommand(133)` when the gamepad stick returns inside the deadzone (thresholds `gamePadAnalogOffset`/`gamePadAnalogDead`). Runaway protection is primarily rate/buffer based rather than distance-bounded: the gamepad only emits the next move when `_serial_form.GetFreeBuffer() >= 99` and only every `gamePadTimer.Interval` (50 ms normal, up to 200 ms for tiny movements), so at most a small handful of short relative moves are ever in flight; on release the 0x85 flushes the planner and the buffer gate prevents further sends. Caveat for ArctZ: because motion is a train of relative moves rather than a single bounded '$J=', a lost recenter event on a laggy link could leave one buffered move to complete, and there is no soft-limit-bounded single-jog fallback like Candle's.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses GRBL 1.1 '<...>' status reports, e.g. '<Idle|MPos:0.000,0.000,0.000|FS:0,0|WCO:0.000,0.000,0.000>'. GrblRelated.GetPosition() extracts machine position (MPos) and work position (WPos) with WCO offset handling; feed and spindle come from the 'FS'/parser fields (internally 'FR' feedrate, 'SS' spindle speed) and overrides from an 'Ov' field (ModState.Ov). Supports up to 6 axes in the DRO (X Y Z plus two of A/B/C/U/V/W enabled via flags axisA/axisB/axisC...). Marlin position reports (M114) are parsed on the alternate `isMarlin` path.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > GrblRelated defines `internal enum GrblState { idle, run, hold, jog, alarm, door, check, home, sleep, probe, reset, unknown, Marlin, notConnected }`, each mapped to a localized caption and a color (e.g. Lime=idle, Yellow=run, Red=alarm). The state token parsed from each '<...>' report sets the current grbl state which drives control enable/disable, DRO coloring, streaming pause-on-idle logic, and jog gating.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active host-driven polling, not push. A WinForms `timerSerial` fires at `timerSerial.Interval` (default ~1000 ms, dynamically shortened e.g. toward 200 ms while running) and sends the real-time '?' byte (`SerialPortDataSend('?')`); a response counter (`rtsrResponse`) and `countMissingStatusReport` backoff detect a stalled controller (e.g. wrong baud) and reduce query frequency / prompt 'try Marlin'. Relevant to ArctZ: on a bandwidth-limited wireless link this fixed-interval '?' polling would contend with jog traffic and need tuning; Grbl-Plotter has no auto-push status mode.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Supports work coordinate systems G54-G59 plus G28/G30/G92/TLO and PRB — SetCoordinates() recognizes the token set 'PRBG54G55G56G57G58G59G28G30G92TLO' and stores each origin in a dictionary. Distinguishes machine coordinates (MPos) from work coordinates (WPos) via the WCO offset (posWCO). Multi-axis (up to 6) DRO display for the active WCS.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > WinForms, event-driven, NOT MVVM/MVC. The application is a large main form split into many partial-class files (MainForm.cs + MainFormStreaming/GamePad/Interface/PictureBox/... .cs) plus MachineControl UserControls; business logic, protocol, streaming and rendering directly manipulate WinForms controls. There is no view-model layer and no data-binding pipeline. This is the strongest divergence from ArctZ's CommunityToolkit.Mvvm view-model + compiled-binding approach.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Moderate and better-structured than a pure god-object, but still GUI-bound. The entire GRBL communication + character-counting streaming + status/state logic is encapsulated in the ControlSerialForm partial class (ControlSerialForm*.cs), which is a self-contained WinForms UserControl instantiated up to 3× (main + 2nd/3rd GRBL). MainForm talks to it through a narrow surface — `_serial_form.RequestSend(...)`, `GetFreeBuffer()`, `SendRealtimeCommand(...)`, and events like SendStreamEvent — so the protocol/streaming is a reusable component, not scattered across the main window. HOWEVER it is still a WinForms Form/UserControl, not a headless, UI-agnostic core: it owns timers, invokes and control state. For ArctZ the lesson is positive (a dedicated serial-control unit exposing RequestSend/GetFreeBuffer/realtime + events) and negative (that unit is welded to WinForms and to a hard serial/TCP branch instead of an injectable transport).

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > GrblState.alarm is a first-class state (rendered red). GRBL 'error:n' and 'ALARM:n' responses are surfaced to the log/console and mapped to human-readable text via the message/localization layer (MessageText.cs and GRBL code tables), and 'no-echo'/reset conditions are handled during streaming. Recovery is user-driven ($X unlock, $H home, Ctrl-X soft reset). More descriptive than Candle's literal-string matching, though not a full grblHAL extended code catalog.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > Includes an 'automatic reconnect on program start' convenience (re-opens the last port/host at launch). At runtime, a link error (System.IO.IOException on TCP, serial receive/timeout exceptions) triggers `this.BeginInvoke(new EventHandler(DisconnectFromGrbl))` and a StateReset() that clears the stream buffers and resets grblBufferFree; a missing-status-report counter (countMissingStatusReport) detects a dead link and slows polling / suggests wrong baud. There is no automatic retry loop mid-session and no mid-job resume of a partially streamed file after a drop — an interrupted job is restarted (though a run-from-line capability exists for manual recovery). Adequate for stable USB/LAN; insufficient for ArctZ's flaky-BLE case.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > 2D only — and this is the project's headline strength (the task note: 'Advanced 2D visualization and G-code transformation'). It renders the imported vector art and the resulting G-code toolpath as 2D GDI+ drawing in a PictureBox (MainFormPictureBox.cs), with pan/zoom, live tool position, path simulation (MainFormSimulatePath.cs), and rich 2D transforms (scale/rotate/mirror/zero-offset, clip-and-tile, hatch fill, tangential). There is NO 3D OpenGL visualizer (unlike Candle/UGS) — appropriate for its plotter/laser/engraving focus.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — G2/G3 arcs are handled and expanded to segments for the 2D preview and path simulation, with modal G-code state tracked through the analyze/simulate pipeline (ImportMath.cs and the Graphic*/simulate modules); arc/line geometry is also produced by the SVG/DXF/HPGL importers and by transforms. Comments/whitespace are stripped before send (streamingBuffer.Add(line.Replace(" ", ""))).

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Windows desktop only — a WinForms/.NET Framework application with hard Windows dependencies (System.Windows.Forms, GDI+, DirectInput). No Linux/macOS build, no mobile (Android/iOS), no browser/WASM. This is the single largest divergence from ArctZ, whose Avalonia stack targets Desktop + Android + iOS + WASM from one core. Grbl-Plotter offers no reference for cross-platform code sharing, touch UI, or a portable transport layer; only its GRBL protocol/streaming logic is conceptually portable.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Classic GRBL '$'-settings model: reads the '$$' numbered settings report and writes settings as '$x=val' through a GRBL setup/configuration UI, plus '$#' offsets, '$G' parser state and '$I' version/features (the '$I customization string' handling was extended 2025-03-04). There is NO FluidNC YAML config upload/download workflow and no dedicated grblHAL extended-settings editor — grblHAL/FluidNC are configured only insofar as they expose GRBL-compatible '$' settings.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > Per instance one controller, but the app supports up to 3 simultaneous connection instances (`iamSerial` 1/2/3 = main GRBL + 2nd GRBL + 3rd), each its own serial or Ethernet/TCP link — enabling e.g. a machine plus a separate tool-changer/second head. Each individual connection drives exactly one controller; there is no multi-client sharing of a single controller like the ESP32 WebUI WebSocket model.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > GRBL 1.1 primary, with GRBL 0.9-and-below legacy support (`isVersion_0` flag), Marlin support (`isMarlin` flag, M114 position / different line endings, auto-suggested when '?' polling fails), and compatibility notes for grblHAL (the Ethernet/TCP path targets grblHAL Ethernet boards) and the VoidMicro controller (added 2021-11-03). Dialect/version is inferred from the welcome/'$I' version string and response behavior rather than a formal capability handshake. There is no FluidNC-specific handling — FluidNC works only as a GRBL-1.1-compatible target, and the fixed 127-byte RX buffer assumption is not adjusted for it.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes — a strong probing/leveling workflow. Supports G38.x probing (Features.md cites 'G38.3 probe toward tool length sensor' and 'G43.1 dynamic tool length offset'), a dedicated probing control (ControlProbing.cs) and a full height-map / auto-leveling module (ControlHeightMap.cs + ControlHeightMapClass.cs) that probes a surface mesh and offsets each program line's Z. PRB responses are parsed into probe coordinates via the coordinate handling. Comparable in capability to Candle's height-map feature.

### Прочая информация
- **type**: sender
- **research_date**: 2026-07-24

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- bt_reconnect_behavior
- bt_pairing_model
- mode_mutual_exclusion
- jog_latency_budget

---

## 8. grblHAL <a id="grblhal"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > C (embedded), fork/32-bit successor of GRBL 1.1f. A shared portable 'core' plus a Hardware Abstraction Layer (HAL): the core implements motion planning, the G-code interpreter and the streaming protocol, while per-MCU driver repos implement the HAL. Runs on 15+ 32-bit MCU families: STM32 (F1/F3/F4/F7/H7), RP2040/RP2350 (Pi Pico), ESP32 (Xtensa), NXP iMXRT1062 (Teensy 4.x), LPC176x, SAM3X8E (Arduino Due), MSP432/TM4C (TI), plus Linux/Windows simulator drivers. Not a UI/sender — it is the controller-side firmware ArctZ talks TO.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GNU GPL v3 (grblHAL core COPYING file is GPLv3, inherited from GRBL). Individual driver/plugin repos are generally GPLv3 as well; check each plugin repo for its own header when reusing code.
- **repository** _Ссылка на исходный код_:
  > GitHub organization https://github.com/grblHAL — core: https://github.com/grblHAL/core ; drivers as separate repos (grblHAL/STM32F4xx, grblHAL/RP2040, grblHAL/ESP32, grblHAL/iMXRT1062, grblHAL/STM32F1xx, etc.); plugins as separate repos (grblHAL/Plugin_networking, grblHAL/Plugin_WebUI, grblHAL/plugins overview, and per-driver bluetooth.c).
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Very actively maintained (2026). Core build date observed 20260718; ESP32 driver last updated 2026-07-21, RP2040/STM32F4xx/iMXRT1062 drivers updated 2026-07-19, all within days of this research (2026-07-24). Continuous commit cadence and responsive issue tracker.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > As firmware it EXPOSES multiple console streams that a sender can attach to: (1) UART serial and native USB CDC (USB varies by MCU; e.g. USB CDC on STM32/RP2040/iMXRT1062 and on ESP32-S3); (2) Networking via the Plugin_networking on top of the lwIP stack — raw/Telnet, WebSocket, plus FTP and HTTP (the latter two require the SD-card plugin), and extensions WebDAV, SSDP, MQTT; (3) Bluetooth Classic SPP on the original ESP32 via the driver's bluetooth.c. Networking hardware paths: STM32F7xx/H7xx and TM4C129/MSP432E cabled Ethernet, STM32F4xx/RP2040 SPI Ethernet, RP2040(W)/ESP32 WiFi. grblHAL can run several of these streams concurrently and hot-switch the active input stream.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > High, on the firmware side. grblHAL has a formal stream abstraction: every transport registers an io_stream_t vtable (read/write/get_rx_buffer_count/reset etc.) and the core reads/writes through that indirection, fully decoupled from the character-counting protocol above it. Multiple streams can be registered simultaneously and the core can switch the active stream at runtime (stream_connect/disconnect), which is exactly the clean transport/protocol separation ArctZ wants on its own client side. Note this is the CONTROLLER's abstraction; ArctZ still needs its own client-side transport interface, but grblHAL proves the pattern and guarantees the same byte-level GRBL protocol regardless of which physical transport is used.

### Bluetooth Specifics
- **firmware_build_variant** _Нужна ли отдельная сборка/переключение прошивки для включения BT_:
  > Yes — Bluetooth is a compile-time option, not a runtime toggle. It must be enabled via a build define (BLUETOOTH_ENABLE in my_machine.h / CMake board config) and requires the Bluetooth Classic stack enabled in ESP-IDF menuconfig; it is described as a preview/experimental feature. A default networking (WiFi) build does not include BT. So distributing a BT-capable grblHAL means shipping a specific ESP32 build.
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > OS-level Bluetooth Classic pairing/bonding: the ESP32 advertises an SPP device (device name configurable via a grblHAL setting, default around 'grblHAL'/'GRBL'), the host OS pairs (legacy PIN or Just-Works depending on stack), then the connection is a bonded SPP/virtual-serial link opened programmatically. Not a per-app software connect like BLE GATT.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Critical for ArctZ's platform matrix because the transport is SPP: Windows — pair once, then a Bluetooth virtual COM port (or Win32 Bluetooth socket, RFCOMM) — works. Android — Classic SPP via BluetoothSocket/RFCOMM UUID 00001101-... — works. iOS/iPadOS — CoreBluetooth exposes ONLY BLE; iOS does not allow app access to classic SPP for non-MFi accessories, so a grblHAL SPP board is effectively UNREACHABLE from a stock iOS app. Web Bluetooth — supports ONLY BLE GATT, not classic SPP, so grblHAL Bluetooth is UNREACHABLE from a browser Web Bluetooth path. Implication: for iOS and Web, ArctZ must reach grblHAL over WiFi (Telnet/WebSocket) or USB, not Bluetooth.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting flow control, identical in principle to GRBL 1.1: the sender counts bytes in flight against the controller RX buffer and refills as 'ok'/'error' responses arrive; a simple send-response (wait-for-ok) mode also works. grblHAL is a controller, so it IMPLEMENTS the receiving half — it acks each accepted line with 'ok' or 'error:N' and reports free RX space in the status report Bf: field. It also supports the GRBL 1.1 $J= jog protocol as a distinct, buffer-limited command class.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Controller-side, grblHAL keeps the GRBL model: a line ring buffer feeding the G-code parser feeding the motion planner block buffer, while real-time command bytes are handled out-of-band in the stream ISR. Jog ($J=) commands share the same input stream and planner but form a cancellable jog motion class distinct from a running program. There is no separate 'feeder vs sender' queue inside the firmware — that split (as in CNCjs/gSender) is a SENDER concern; grblHAL simply guarantees real-time bytes jump ahead of the line buffer. For ArctZ the takeaway: the joystick-jog vs file-play separation must live in the ArctZ client; grblHAL provides the primitives (jog motion + 0x85 cancel + real-time overrides).
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > grblHAL intercepts real-time command bytes in the stream reader BEFORE they reach the line buffer, so they act immediately regardless of how full the buffer is. It supports the full GRBL 1.1 real-time set and extensions: '?' status, '~' cycle-start, '!' feed-hold, 0x18 (Ctrl-X) soft-reset, 0x84 safety-door, 0x85 jog-cancel, feed-override bytes 0x90–0x94, rapid-override 0x95–0x97, spindle-override 0x99–0x9D, coolant toggles 0xA0/0xA1, plus additional/user-definable real-time commands. This is precisely the bypass path ArctZ must reserve on its transport so status polls, feed-hold and jog-cancel are never stuck behind streamed G-code.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > grblHAL implements GRBL 1.1 jogging ($J=): jogs accept G91 (incremental/step) or G90 (absolute) plus G53 machine-coordinate jogs, at a specified feed. Continuous 'hold-to-move' jog is realized the standard way — the sender issues a long/bounded $J= move and cancels it with 0x85 on release; the firmware also supports queuing successive jog moves for smooth continuous motion. Step/incremental, absolute and machine-coord jogs are all supported at the protocol level.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > grblHAL honors the GRBL real-time jog-cancel byte 0x85: on receipt it decelerates and flushes any queued jog motion immediately, without an alarm, returning to Idle. This is the anti-runaway primitive; the 'lost key-up' safety must be handled by the SENDER (bounded jog distance + reliable 0x85 on release, and/or a re-arm timeout), because if the client never sends 0x85 the firmware will complete the last commissioned jog move. grblHAL's contribution is that 0x85 is real-time and cannot be blocked by a full buffer.
- **mode_mutual_exclusion** _Блокировка jog во время файлового стриминга и наоборот (GRBL lockout error)_:
  > Enforced by the firmware state machine: G-code lines sent while in an active jog or alarm state are rejected (GRBL 'error:33' — command not executable in current state / locked out during alarm or jog), and jogs are rejected when travel/soft-limits would be exceeded. During a running program the controller is in Run state and interactive jog is not accepted until Idle/Hold cleared. So grblHAL provides hard interlocks, but a well-behaved sender should still gate its own jog vs file-stream modes rather than rely solely on error responses.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > GRBL 1.1 style single-line report '<State|MPos:..|WPos:..|FS:feed,rpm|Bf:blk,rx|Ov:..|Pn:..|WCO:..>' with grblHAL extensions: tool-change status, active coordinate-system reporting, SD-card streaming progress, homing-complete flags, auxiliary I/O and pin-state, and multi-spindle info. Extended fields are additive and senders are expected to ignore unknown ones, preserving GRBL-1.1 compatibility — so an existing GRBL-1.1 parser in ArctZ will work and can be extended incrementally.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > GRBL active-state model with grblHAL additions: Idle, Run, Hold, Jog, Alarm, Door (safety door), Home, Check, Sleep, plus Tool (tool-change) states. Sub-states appear (e.g. Hold:0/Hold:1, Door:n). Same core states ArctZ needs to drive VirtualJoystick enable/disable and file-play gating.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Full WCS support: work coordinate systems G54–G59 (and G59.1–.3), machine (MPos) vs work (WPos) with WCO offset reported, G92 offsets, and $# to dump coordinate parameters. More work coordinate systems than classic GRBL in some builds.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Not applicable — grblHAL is headless controller firmware with no application UI/MVVM. The only bundled UI is the optional Plugin_WebUI (an ESP3D-WebUI backend) served over HTTP/WebSocket, which is a third-party browser front-end, not part of grblHAL's architecture. ArctZ's MVVM concerns live entirely on the client side.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Not applicable in the sender sense (no UI in the firmware). Architecturally relevant only in that grblHAL cleanly separates its transport streams (io_stream_t) from the command/protocol core, mirroring the comm-core-vs-UI separation ArctZ targets — but the reusable 'core' for ArctZ is its own client code, not grblHAL.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > grblHAL uses the GRBL alarm/error scheme ('ALARM:N', 'error:N') and, importantly, lets the controller ENUMERATE them to the sender: $EA lists alarm codes and $EE lists error codes with human-readable text, so a sender can show current descriptions without shipping a hard-coded table. Standard unlock via $X and homing via $H apply.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > Firmware exposes stream connect/disconnect events and can switch active streams, but implements no automatic client reconnection or backoff (that is the sender's job). On stream loss buffered motion continues unless the sender/interlocks intervene; soft-reset (0x18) re-initializes the protocol state after recovery. See bt_reconnect_behavior for the Bluetooth-specific caveat.

### Visualization
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > grblHAL's G-code interpreter fully parses and executes modal state and arcs (G2/G3, G17/18/19 planes, G90/G91, units, arc from I/J/K or R) to generate motion — but it does not emit a preview; arc expansion for on-screen preview is a sender concern.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Firmware portability: one shared core runs across 15+ 32-bit MCU families via per-target HAL drivers — this is grblHAL's headline strength and a conceptual parallel to ArctZ's 'shared core + thin platform heads' (here the 'heads' are MCU drivers, not OS UI heads). It says nothing about desktop/mobile/web app portability (grblHAL has no app), but it does define ArctZ's target surface: ArctZ must speak the same GRBL-1.1+extensions protocol to grblHAL regardless of which MCU/transport the board uses.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > grblHAL uses an EXTENDED GRBL '$'-settings model (not FluidNC YAML). '$$' dumps all settings, '$<n>=<val>' writes one; the numbering space is far larger than GRBL's and grouped. Crucially it is self-describing: $ES/$ESH enumerate every setting with its group, datatype, unit and allowed range (human-readable / machine-readable), $EG lists setting groups, and $I / $I+ report the build, axes, and enabled features/plugins (including whether Bluetooth streaming is present). Settings persist to driver-appropriate storage (flash/EEPROM/FRAM). For ArctZ this means a settings editor can be built generically from $ES output instead of hard-coding a settings table.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > grblHAL IS one of the target dialects, not a multi-dialect sender. It presents itself as GRBL-1.1-compatible with extensions and identifies via its welcome/version banner containing 'GrblHAL' (e.g. 'GrblHAL 1.1f ...') and via $I / $I+ build info reporting plugins and capabilities. A sender distinguishes grblHAL from classic GRBL and from FluidNC by that banner/$I string. This is the second GRBL dialect ArctZ should target alongside FluidNC.

### Прочая информация
- **type**: firmware
- **vendor**: grblHAL open-source project; principal maintainer Terje Io (GitHub: terjeio / grblHAL org).

### Неопределённые поля (uncertain)
- bt_profile_type (no BLE-console variant known to exist; SPP-only is asserted from available evidence)
- esp32_variant_support (whether any community BLE bridge exists for S3/C3/C6/H2)
- radio_coexistence (reliability of simultaneous WiFi + BT Classic on classic ESP32 under grblHAL)
- bt_mtu_packet_size (exact negotiated RFCOMM frame size)
- bt_throughput (no published grblHAL-specific benchmark)
- bt_reconnect_behavior (whether any grblHAL setting hard-halts motion on Bluetooth stream loss)
- rx_buffer_handling (exact default RX buffer byte size per driver)
- jog_latency_budget (any grblHAL setting affecting jog planning latency)
- status_report_polling_model (exact setting number and default interval for auto-report)
- preview_rendering (exact visualization capability of bundled ESP3D WebUI plugin)
- max_concurrent_connections (precise max simultaneous network client count per driver)
- probing_support (any firmware-side auto-leveling beyond raw G38.x probing)

---

## 9. gSender <a id="gsender"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > JavaScript/TypeScript. Electron desktop app: React front-end (renderer) + Node.js back-end (server) communicating over socket.io. Front-end migrated to recent React with TypeScript and Tailwind/shadcn UI in 1.5/1.6.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_: GNU GPLv3 (free software, provided as-is).
- **repository** _Ссылка на исходный код_: https://github.com/Sienci-Labs/gsender
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Actively maintained. v1.6.0 released 2026-04-16 (grblHAL SD-card jobs via yModem/FTP, expanded grblHAL compatibility); frequent releases and open issue/PR activity through 2026.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Serial/USB (node-serialport) and Ethernet/network as a raw TCP socket (node 'net.Socket', telnet-style) to grblHAL boards that support it. No WiFi-STA-managed workflow, no WebSocket-to-controller, no Bluetooth. (The internal socket.io/WebSocket link is only between the Electron renderer and the local Node server, not to the CNC controller.)
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Partial abstraction. 'src/server/lib/Connection.js' is a transport wrapper that also performs firmware auto-detection, but it delegates to a single 'SerialConnection.js' class that internally branches between a SerialPort object and a net.Socket based on a 'network'/'ethernetPort' option (and an IP-address heuristic). So serial and TCP share one interface (write/writeImmediate/read events) but there is no fully pluggable multi-transport registry; adding a new transport (e.g. BLE) means extending SerialConnection rather than adding a peer class. Command protocol (Grbl/Grblhal controllers, Feeder, Sender) is cleanly separated from the byte transport, which is the useful architectural pattern for ArctZ.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > Not supported. gSender has no Bluetooth transport of any kind (neither BLE GATT/NUS nor Bluetooth Classic SPP). Controller connectivity is serial-USB or wired Ethernet/TCP only. No direct relevance for ArctZ's BLE path beyond the transport-abstraction pattern.
- **esp32_variant_support** _На каких вариантах ESP32 доступен BT-режим (WROOM/WROVER = SPP; S3/C3/C6/H2 = только BLE)_: Not applicable — no Bluetooth transport in gSender, so no ESP32 BT-variant handling exists.
- **radio_coexistence** _Могут ли WiFi и Bluetooth работать одновременно на одной плате_:
  > Not applicable — gSender does not use the controller's radios; it connects over USB or wired Ethernet.
- **firmware_build_variant** _Нужна ли отдельная сборка/переключение прошивки для включения BT_:
  > Not applicable — gSender does not require or select any Bluetooth firmware build; it detects Grbl/grblHAL/FluidNC via the welcome-string on whatever transport is opened.
- **bt_mtu_packet_size** _MTU/размер полезной нагрузки пакета, влияние на фрагментацию G-code строк_:
  > Not applicable — no BLE/SPP link, so no MTU/packet-fragmentation handling. (On serial/TCP gSender writes full G-code lines terminated with '\n'; character-counting streaming caps in-flight bytes at the controller RX buffer size, default 128.)
- **bt_throughput** _Измеренная или заявленная пропускная способность в сравнении с USB/WiFi_:
  > Not applicable — no Bluetooth link to benchmark. Throughput is governed by USB serial or TCP, both far above BLE.
- **bt_reconnect_behavior** _Поведение при разрыве связи — автопереподключение, feed hold/alarm станка_:
  > Not applicable to Bluetooth (none). General reconnect: the Connection/SerialConnection layer emits 'close'/'error' events; on unexpected disconnect the controller is torn down and the UI shows disconnected. There is no controller-radio-specific auto-reconnect, and no BT-specific feed-hold/alarm-on-drop logic because there is no BT transport.
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > Not applicable — no OS-level BT pairing/bonding is used; connections are opened programmatically by port path (serial) or IP:port (TCP).
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Not applicable — gSender uses no OS Bluetooth API (no Windows Bluetooth, Android, CoreBluetooth, or Web Bluetooth). It is a desktop-only Electron app using node-serialport and node 'net' TCP sockets.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting streaming for file jobs. 'src/server/lib/Sender.js' implements two strategies — SP_TYPE_SEND_RESPONSE (0, simple wait-for-ok) and SP_TYPE_CHAR_COUNTING (1) — and the Grbl/grblHAL controllers instantiate the Sender with SP_TYPE_CHAR_COUNTING, tracking in-flight byte count against the controller buffer to keep the RX buffer full without overflow. Manual/single lines go through the Feeder, which is send-response (one line at a time, wait for 'ok').
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Explicit. The char-counting strategy keeps state.bufferSize (default 128 bytes, matching classic GRBL) and refuses to send a line unless dataLength + line.length fits; it decrements as 'ok'/error responses arrive. bufferSize is settable so grblHAL boards with larger RX buffers can be configured. This is the standard GRBL character-counting flow-control model.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Separate queues (CNCjs feeder/sender model, inherited and kept). 'Feeder.js' handles interactive/manual commands and jogging (send-response, one line at a time); 'Sender.js' handles file streaming (character-counting). A 'Workflow.js' state machine (Idle/Running/Paused) gates the sender. Manual jog therefore does not share the file-stream queue, which is exactly the split ArctZ wants between VirtualJoystick jog and file play.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time bytes bypass both queues via 'connection.writeImmediate()' which writes directly to the serial/TCP port ahead of buffered line writes. Macro/G-code text may embed '[\xNN]' tokens; 'extract-realtime-commands.js' strips them out of the line and the controller's dataFilter calls writeImmediate() for each decoded byte. Status '?', feed-hold '!', cycle-start '~', soft-reset 0x18, and jog-cancel 0x85 are all sent through writeImmediate, so they are never blocked by the char-counting buffer.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both incremental (fixed-step) and continuous (hold-to-move) jog, plus joystick/gamepad analog jog. UI/keyboard: a short press sends one incremental $J= move; holding past a threshold (jogHelper.ts, ~200 ms) switches to continuous jog by sending a long-distance $J= move that is cancelled on release. Gamepad/joystick: 'JoystickLoop.js' converts analog stick magnitude into feedrate (with EMA smoothing) and streams incremental jogs on a timed loop.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > Jog cancel uses the GRBL 0x85 real-time byte. In GrblController.js both 'jog:stop' and 'jog:cancel' write '\x85'. On the client, cancelJog is throttled (JoystickLoop.js throttles to 50 ms; jogHelper throttles stop to ~150 ms) to avoid flooding. The continuous-jog design sends a long move then relies on 0x85 on key/pointer release; runaway risk on a lost keyup is mitigated by the loop re-issuing bounded moves and by the throttled cancel, but a genuinely lost release event can still leave a queued long move (a known class of jog issues). Relevant known bug: issue #545 — keyboard continuous jog failed on Linux because the axis letter arrived lowercase ('x') from keyboard shortcuts while the jog math only matched uppercase 'X'; the reported fix normalizes axis case in the 'jog:start' handler.
- **mode_mutual_exclusion** _Блокировка jog во время файлового стриминга и наоборот (GRBL lockout error)_:
  > Enforced partly by GRBL itself and partly by the Workflow state. GRBL/grblHAL reject G-code that is locked out during alarm or jog state (error 33 'G-code locked out during alarm or jog state') and jog commands that exceed travel (error 15) or are malformed (error 20). gSender's Workflow gates the file Sender so streaming and interactive feeder use are coordinated rather than interleaved arbitrarily.
- **jog_latency_budget** _Транспортная задержка jog-команд и минимальный интервал отправки при удержании (особенно по BLE/WiFi)_:
  > Client-side rate limiting is explicit: jogHelper throttles jog issue to ~150 ms and stop to ~150 ms; JoystickLoop uses EMA smoothing, a ~600 ms move-duration model, a per-move duration/timer to schedule the next incremental jog, and a 50 ms throttled cancel; tiny stick deflections are treated as idle to prevent micro-jog spam. Because the transport is USB serial or wired TCP (sub-millisecond to low-ms latency), gSender's timing constants assume a fast link and are not tuned for a high-latency BLE channel — ArctZ over BLE would need larger send intervals / bigger per-move distances to compensate.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses standard GRBL 1.1 '<...>' status reports via GrblLineParserResultStatus (MPos/WPos, feed & spindle 'FS', buffer 'Bf', override 'Ov', pins 'Pn', etc.), with a grblHAL parser variant. Dedicated line-parser result classes exist for status, alarm, error, feedback, settings, parser-state, parameters, version and startup lines.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Full GRBL active-state model in constants.js: Idle, Run, Hold, Door, Home, Sleep, Alarm, Check. The server-side Workflow.js adds an application workflow state (Idle/Running/Paused) layered over the controller's reported machine state.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Handles machine (MPos) vs work (WPos) coordinates and G54–G59 work coordinate systems; parses G-code parameters/offsets ($# / parser-state) and supports zeroing work origin per axis.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > React component architecture (feature-folder structure under src/app/src/features), Redux for app state, hooks; not MVVM. Communicates with the Node server over socket.io. Different paradigm from ArctZ's CommunityToolkit.Mvvm, but the feature-oriented separation and the server-side command/transport layers are the transferable ideas.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Strong separation. All controller/protocol logic (Grbl/Grblhal controllers, Feeder, Sender, Workflow, Connection, SerialConnection, line parsers) lives in the Node server process; the React renderer is a thin client that sends commands and receives events over socket.io. This clean comm-core-vs-UI split is exactly the reusability model ArctZ targets (shared core + thin platform heads), though gSender's 'core' is a local Node server rather than an in-process library.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > GRBL alarm and error codes are parsed and surfaced with human-readable messages (GrblLineParserResultAlarm / ...Error, plus GRBL_ERRORS/GRBL_ALARMS tables in constants.js). Alarm state triggers UI notifications and unlock ($X) / homing prompts.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > Connection/SerialConnection emit close/error; controller is torn down on drop and the UI reflects disconnection. Reconnect is generally user-initiated (re-select port and connect); no controller-radio-specific auto-reconnect/backoff layer. Soft-reset 0x18 is used to recover the controller after alarms.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > 3D toolpath visualizer using WebGL/Three.js (src/app/src/features/Visualizer, GCodeVisualizer.js, CoordinateAxes/Cuboid/CuttingPointer). Renders the parsed toolpath with a live cutting pointer that tracks progress; camera controls and job outline supported.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — a G-code toolpath/interpreter layer (GcodeToolpath.js server-side and the client GCodeVisualizer) tracks modal state (units, plane, absolute/incremental, motion mode) and expands arcs (G2/G3) into segments for rendering.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Desktop only, but broad within desktop: prebuilt for Windows (x64), macOS (Intel), Linux (Intel x64), Linux ARM, and Raspberry Pi (64-bit) via Electron. No mobile (Android/iOS) and no browser/web build; headless Raspberry Pi is not yet supported. This is a different portability strategy than ArctZ's Avalonia shared-core-plus-thin-heads reaching mobile/web — gSender achieves cross-platform via Electron packaging of one desktop codebase, and deliberately does not target touch/mobile jog scenarios.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > GRBL '$$'/'$'-settings model: on connect the controller issues '$$' to read all settings and '$G'/'$#' for parser state and offsets; settings are parsed by GrblLineParserResultSettings and editable in the UI (with a friendly EEPROM-settings UI and firmware-tool wizards). grblHAL extended settings are supported. FluidNC is detected as GRBL-compatible, but gSender does not provide a FluidNC YAML-config upload workflow — it treats FluidNC through the GRBL '$'-settings interface.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One controller connection at a time per gSender instance. The local Node server can serve multiple socket.io UI clients (remote mode), but there is a single active serial/TCP link to the CNC.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > GRBL 1.1, grblHAL, and FluidNC. Dialect is auto-detected from the welcome/echo string in Connection.js: a regex matches 'grblhal' → GRBLHAL controller, otherwise 'grbl' or 'fluidnc' → GRBL controller (recent releases specifically made detection more robust so FluidNC is caught as grbl). Requires minimum GRBL 1.1 firmware.

### Прочая информация
- **type**: sender
- **vendor**: Sienci Labs Inc.

### Неопределённые поля (uncertain)
- probing_support (whether gSender includes a surface height-map/auto-leveling probe grid is unconfirmed; standard G38.2 touch-plate probing is confirmed)
- status_report_polling_model (exact default '?' poll interval in ms not extracted from source; parser-state '$G' timer confirmed at ~500 ms and status polling confirmed as active setInterval)

---

## 10. ioSender <a id="iosender"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > C# / WPF (Windows Presentation Foundation), .NET Framework 4.6.2, Windows-only desktop application. Organised as a multi-project Visual Studio solution: 'CNC Core' (protocol/comms/parsing library), 'CNC Controls' (reusable WPF UserControls incl. jog/DRO/job), 'CNC GCodeViewer' (3D viewer), plus feature modules 'CNC Controls Probing', 'CNC Controls Lathe', 'CNC Controls Camera', 'CNC Controls Dragknife', 'CNC Converters', 'Grbl Config App', and the 'ioSender' / 'ioSender XL' app heads. Same base stack (.NET, XAML, MVVM) as ArctZ, which is why it is the priority architectural reference — though ioSender is .NET Framework 4.6.2 + WPF, not .NET 10 + Avalonia.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > BSD-3-Clause (permissive; code/ideas can be reused with attribution and license notice, unlike the GPLv3 senders such as gSender/UGS).
- **repository** _Ссылка на исходный код_: https://github.com/terjeio/ioSender
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Actively maintained by the grblHAL author. Latest release v2.0.47 published 2026-04-29 (last push 2026-04-29); prior releases 2.0.46 (2025-06-05) and 2.0.45 (2024-12-12). Repo not archived, ~117 open issues, an 'ioSender XL' extended build and 'edge' pre-releases are maintained alongside the stable line.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Three transports, exposed via a StreamType enum { Serial, Telnet, Websocket }: (1) Serial/USB COM port (with a 'Toggle DTR' option to reset 8-bit Arduino/classic-Grbl boards); (2) Telnet over TCP (default port 23, e.g. 10.0.0.70:23) to networked grblHAL boards / the grblHAL simulator; (3) WebSocket (default port 80, e.g. ws://10.0.0.70:80) using the websocket-sharp library. There is NO native Bluetooth transport of any kind. A network tab exists in the connection dialog, but historically telnet/websocket also had to be set via the config file or command line.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Mature, cleanly abstracted — the single most directly transferable pattern for ArctZ. 'CNC Core/Comms.cs' defines a StreamComms interface (WriteByte, WriteBytes, WriteString, WriteCommand, ReadByte, a DataReceived event, Reply/GetReply/AwaitAck/AwaitResponse, and a CommandState with AwaitAck/DataReceived/ACK/NAK states) plus a StreamType enum. Three concrete implementations — SerialStream.cs, TelnetStream.cs, WebsocketStream.cs — each satisfy the same interface, and the rest of the app talks only to a singleton 'Comms.com' of type StreamComms. The command/protocol layer (Grbl.cs, GrblViewModel, GCodeJob, KeypressHandler) is fully decoupled from the byte transport. Adding a new transport (e.g. a BLE/GATT-NUS stream for ArctZ) is a matter of adding a 4th StreamComms implementation and a StreamType value, with no changes to the streaming or UI code — exactly the pluggable-transport model ArctZ wants.

### Bluetooth Specifics
- **radio_coexistence** _Могут ли WiFi и Bluetooth работать одновременно на одной плате_:
  > Not applicable — ioSender does not manage the controller's radios; it connects over USB serial, TCP telnet, or WebSocket. WiFi/BT coexistence is entirely a firmware/board concern outside ioSender's scope.
- **firmware_build_variant** _Нужна ли отдельная сборка/переключение прошивки для включения BT_:
  > Not applicable — ioSender does not select or require any Bluetooth firmware build. It connects to whatever Grbl/grblHAL endpoint is presented on serial/telnet/websocket and auto-detects capabilities via $I / grblHAL enumeration.
- **bt_mtu_packet_size** _MTU/размер полезной нагрузки пакета, влияние на фрагментацию G-code строк_:
  > Not applicable — no BLE/SPP link, so no MTU / packet-fragmentation handling. On serial/telnet/websocket ioSender streams full G-code lines and applies character-counting flow control against the controller's reported RX buffer size (see streaming_strategy).
- **bt_throughput** _Измеренная или заявленная пропускная способность в сравнении с USB/WiFi_:
  > Not applicable — no Bluetooth link to benchmark. Throughput is bounded by USB serial or the TCP/WebSocket link, both far above BLE rates.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Not applicable — ioSender uses no OS Bluetooth API (no Windows BluetoothLE/RFCOMM, no Android, no iOS CoreBluetooth, no Web Bluetooth). It is Windows-only WPF; its transports are System.IO.Ports-style serial, TCP telnet, and websocket-sharp WebSocket. (Contrast with ArctZ, which must implement per-platform BT APIs itself — Windows/Android/iOS CoreBluetooth/Web Bluetooth — since ioSender provides no reference for that.)

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting streaming (the standard Grbl flow-control model), implemented in 'CNC Controls/JobControl.xaml.cs'. It computes serialSize = Math.Min(AppConfig.Settings.Base.MaxBufferSize, (int)(GrblInfo.SerialBufferSize * 0.9f)) — i.e. 90% of the controller's reported RX buffer — and in SendNextLine() sends the next line only while job.serialUsed < (serialSize - NextRow.Length). It increments job.serialUsed by the line length on send and decrements it by the corresponding line length when an 'ok' acknowledgement arrives (job.ACKPending tracked). This keeps the controller RX buffer as full as possible without overflow, rather than a slow one-line send-then-wait-for-ok. Interactive/MDI/jog commands and real-time bytes bypass this counter.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Explicit and controller-aware. Instead of hard-coding 128 bytes, ioSender uses GrblInfo.SerialBufferSize — the actual RX buffer size reported by the controller ($I / grblHAL info; classic Grbl defaults to 128) — and applies a 0.9 safety factor, further capped by a user MaxBufferSize setting. This lets it exploit grblHAL boards with larger RX buffers while staying safe on classic 128-byte Grbl.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Two distinct paths rather than the CNCjs dual-feeder/sender object model. File streaming runs through JobControl/GCodeJob with the character-counting handler and a streaming state machine (streamingHandler with states like Idle/Send/SendMDI/Halted/etc.). Interactive commands (MDI console entry, jogging, macros, overrides) are issued directly through Comms.com outside the file-stream counter. So manual jog does not share or stall the file-stream queue — the same jog-vs-file separation ArctZ needs between VirtualJoystick and file playback — but it is expressed as a streaming-state-machine + direct-write split rather than two parallel queue objects.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time single-byte commands are written straight to the transport via Comms.com.WriteByte(...), completely bypassing the character-counting job buffer, so they take effect immediately regardless of streaming state. Examples in code: Reset (0x18), FeedHold ('!' / CMD_FEED_HOLD 0x82), CycleStart ('~' / CMD_CYCLE_START 0x81), status request ('?' / CMD_STATUS_REPORT 0x80), jog cancel (CMD_JOG_CANCEL 0x85), and feed/rapid/spindle override bytes. A helper GrblLegacy.ConvertRTCommand() maps the classic-Grbl ASCII real-time chars (?, !, ~) to the grblHAL binary real-time codes (0x80/0x82/0x81) depending on the connected dialect — a nuance ArctZ should replicate if it targets both Grbl 1.1 and grblHAL/FluidNC.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both continuous and incremental, driven from 'CNC Controls/KeypressHandler.cs'. Jogs are sent as $J= jog commands built as "$J=G91G21" + axis/distance + "F{feed}" (relative, metric). Continuous / hold-to-move ('fullJog') sends a large-distance jog while a key is held, using JogMode.Slow or JogMode.Fast selected by the Shift modifier; incremental/step mode (JogMode.Step) is selected with the Ctrl modifier and sends discrete configured step distances. Feed and step distances are taken from jog configuration (JogConfigControl).

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > GrblViewModel.ParseStatus() parses standard Grbl 1.1 '<...|...>' reports split on '|': machine position 'MPos' (sets IsMachinePosition) vs work position 'WPos', work-coordinate-offset 'WCO' (reconciled so both MPos and WPos are always known via has_wco), feed+spindle 'FS' (feed, programmed RPM, optional actual RPM) and feed-only 'F', buffer 'Bf' (planner buffer + RX buffer free), overrides 'Ov' (feed/rapids/spindle %), input pins 'Pn' decoded against GrblInfo.SignalLetters into a Signals flag set, plus 'WCS' for the active work coordinate system. grblHAL extensions are supported.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Full Grbl active-state model held in a GrblState structure: State enum = Unknown, Idle, Run, Hold, Jog, Alarm, Door, Home, Sleep, Check, Tool, with an integer Substate (e.g. Hold:0/1, Door substates), a Color for UI, and LastAlarm tracking. SetGRBLState() performs transitions and raises property-change notifications so the UI (DRO/buttons) reacts to state.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Handles machine (MPos) vs work (WPos) coordinates with WCO reconciliation, and tracks the active work coordinate system G54–G59 via the 'WCS' status field / GrblParserState.WorkOffset; supports zeroing work origin and per-axis DRO editing.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > MVVM on WPF — the closest paradigm match to ArctZ among the surveyed senders. GrblViewModel (public class GrblViewModel : MeasureViewModel) is the central view-model implementing INotifyPropertyChanged-style OnPropertyChanged() and exposing ObservableCollection<string> logs; XAML UserControls (DRO, jog, job, probing) bind to it. It predates and does not use CommunityToolkit.Mvvm source generators (hand-written properties/commands, .NET Framework 4.6.2), but the shape — one shared view-model + data-bound reusable controls — maps directly onto ArctZ's ViewModelBase/[ObservableProperty] approach.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Good layering with one caveat. All protocol/transport/parsing lives in the 'CNC Core' library (Comms/StreamComms, SerialStream/TelnetStream/WebsocketStream, Grbl.cs, GrblViewModel, GCodeParser/Emulator, PollGrbl), and the WPF UI lives in separate 'CNC Controls*' projects that reference Core — a reusable-core-plus-thin-UI split analogous to ArctZ's shared-core + platform heads. The caveat is the global 'Comms.com' singleton (static current-connection accessor) threaded through the code, which couples callers to a single active connection and is less cleanly injectable than a DI-based transport would be. ArctZ should keep the StreamComms-style interface but favour dependency injection over a static singleton.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Grbl/grblHAL alarm and error codes are parsed and surfaced with human-readable text; GrblState tracks LastAlarm and substate, the UI prompts for unlock ($X) / homing ($H), and status colour reflects the Alarm state. grblHAL's extended alarm/error tables are supported.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > 3D toolpath visualization in the 'CNC GCodeViewer' project using HelixToolkit.Wpf (WPF 3D). Renderer/RenderControl display the parsed program with live tool-position tracking as the job runs, camera controls, and configurable colours (ColorPicker/ConfigControl).
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — GCodeParser.cs plus a GCodeEmulator track modal state (units G20/G21, plane, absolute/incremental G90/G91, motion mode) and expand arcs G2/G3 into line segments for the 3D viewer and for job length/time estimation.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Windows-only. Single WPF / .NET Framework 4.6.2 desktop codebase — no macOS, Linux, mobile, or web build, and the README explicitly states '(Windows only)'. This is the opposite portability strategy to ArctZ's Avalonia shared-core + thin platform heads targeting Desktop/Android/iOS/Browser. ioSender is therefore a strong reference for .NET/XAML/MVVM architecture, character-counting streaming, keyboard jogging and DRO, but not for cross-platform packaging or for any non-Windows transport (its serial/telnet/websocket stack and HelixToolkit.Wpf viewer are Windows-bound).

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Grbl '$$'/'$' settings model with grblHAL enhancements. On connect it reads controller settings and, for grblHAL, enumerates the setting/group/description metadata the firmware exposes to build a dynamic configuration UI (the 'Grbl Config App' / GrblConfiguration) with on-screen documentation — settings are not hard-coded but generated from controller data. Classic Grbl uses the flat '$$' list. No FluidNC YAML-config upload workflow is provided.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One controller connection per ioSender instance (a single active StreamComms 'Comms.com'). No multi-controller or shared-connection model.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Extensive — a dedicated 'CNC Controls Probing' module built on G38.x probing. Includes edge finder (external/internal), center finder, tool-length probing, work-coordinate-system probe selection, probe verification, rotation/alignment probing, and a full surface HeightMap / auto-leveling feature (HeightMap.cs, HeightMapControl, HeightMapViewModel, GCodeTransform.cs applies the height map to the program). This is one of the more complete probing/auto-leveling implementations among Grbl senders.

### Прочая информация
- **type**: sender
- **vendor**: terjeio (Terje Io) — author of grblHAL; ioSender is grblHAL's reference GCode sender

### Неопределённые поля (uncertain)
- bt_profile_type (SPP-via-Windows-virtual-COM is an inferred workaround, not a documented ioSender feature; no native BT)
- esp32_variant_support (N/A — inferred implication only, ioSender has no BT code)
- bt_reconnect_behavior (no BT; exact auto-reconnect behaviour on a mid-job serial drop not confirmed from source)
- bt_pairing_model (N/A at app level; OS-level SPP pairing assumption unverified)
- jog_cancel_mechanism (behaviour on a genuinely lost key-up / focus-loss event not fully traced)
- mode_mutual_exclusion (exact jog-disable predicates during file streaming not quoted from source)
- jog_latency_budget (inferred; no explicit latency-budget/throttle constant found in source)
- status_report_polling_model (default 'Poll interval' value in ms and its range not confirmed; confirmed to be an active, user-configurable poll)
- reconnect_logic (precise automatic recovery behaviour on unexpected mid-stream disconnect not confirmed)
- supported_dialects (real-world FluidNC compatibility via the Grbl path is plausible but not verified/tested by the project)

---

## 11. LaserGRBL <a id="lasergrbl"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > C# on the classic .NET Framework (3.5+, current builds target .NET Framework 4.x), Windows WinForms desktop GUI (GDI+ rendering, no WPF/no headless core). Compilable with Microsoft Visual Studio and SharpDevelop. Serial access uses several backends: System.IO.Ports (UsbSerial), an alternate UsbSerial2, and a bundled RJCP.IO.Ports independent serial implementation (RJCPSerial). It is a laser-oriented GRBL sender, and one of the smallest readable C#/.NET references for the GRBL character-counting streaming protocol.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GPL-3.0 (GNU General Public License v3.0). Strong copyleft — safe to read and study for architecture/patterns, but source cannot be copied into a permissively-licensed or closed ArctZ codebase. Being C#/.NET makes its GrblCore streaming logic a closer language match for ArctZ than the C++/Qt Candle reference, but the license constraint is the same.
- **repository** _Ссылка на исходный код_:
  > https://github.com/arkypita/LaserGRBL (author Arkypita). Website: https://lasergrbl.com. DeepWiki architecture docs: https://deepwiki.com/arkypita/LaserGRBL. Notable community forks: makerbase-mks/LaserGRBL, buzzmarshall/LaserGRBL, and a fresh Linux port to Avalonia/.NET 10 (Gilmore-Enterprises/LaserGRBL-Linux) that is directly relevant to ArctZ's Avalonia stack.
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Active, single-maintainer project. Recent tagged releases: v7.14.1 (2025-03-01, 'bugfixes and improved diagnostics', Longer Nano/NanoDuo custom menu), v7.14.0 (2024-12-06), v7.13.0 (2024-12-03), v7.12.0 (2024-07-22). Long-lived (versioning already in the 7.x line) with steady incremental releases; development is ongoing but Windows/.NET-Framework-bound.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Transports are pluggable via the IComWrapper interface in LaserGRBL/ComWrapper/. Concrete wrappers: (1) Serial/USB with THREE selectable implementations — UsbSerial (System.IO.Ports.SerialPort), UsbSerial2 (alternate), and RJCPSerial (bundled RJCP.IO independent serial port, a fallback when the .NET SerialPort misbehaves, e.g. CH340 driver quirks); (2) Telnet — raw TCP socket by IP+port (path to FluidNC/ESP32 over WiFi); (3) LaserWebESP8266 — a WebSocket wrapper targeting the ESP8266/ESP32 LaserWeb-style websocket bridge; (4) Emulator — a virtual controller for testing without hardware. There is NO native Bluetooth wrapper; Bluetooth is only usable if the OS exposes a Classic-SPP device as a virtual COM port that UsbSerial then opens.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Good transport abstraction, moderate protocol separation — a useful middle example for ArctZ. The IComWrapper interface (LaserGRBL/ComWrapper/IComWrapper.cs) is a clean, protocol-agnostic seam: Open/Close, IsOpen, Write(byte[])/Write(string), a line/char read, HasData, etc. GrblCore holds a single mComWrapper reference and all six+ concrete wrappers (UsbSerial, UsbSerial2, RJCPSerial, Telnet, LaserWebESP8266, Emulator) are interchangeable behind it, so switching serial↔TCP↔WebSocket only changes which wrapper is instantiated. Unlike Candle (where protocol lives in the UI form), LaserGRBL's streaming/protocol logic is consolidated in a dedicated non-UI class, GrblCore (LaserGRBL/Core/GrblCore.cs), which the WinForms UI subscribes to via events — so ArctZ gets TWO reusable seams here: transport (IComWrapper) AND a distinct core/model. However GrblCore is a large, laser-specialized class still coupled to Windows/.NET-Framework threading primitives and WinForms-oriented event dispatch, so it is a design reference rather than a drop-in portable core. Three-tier ideal (transport / protocol / UI) is approximated: transport and protocol are cleanly split from each other, but the protocol tier is one big class rather than layered feeder/sender.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No dedicated Bluetooth support of any kind — no BLE (GATT/Nordic UART) and no explicit Bluetooth Classic SPP/RFCOMM code in the ComWrapper set. The only Bluetooth path is indirect: an OS-level paired Classic-SPP device presented as a virtual serial COM port, which the UsbSerial/RJCPSerial wrapper opens like any COM port. Provides no reusable BLE design for ArctZ.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > LaserGRBL uses no native Bluetooth API on any platform — no WinRT BLE, and (being Windows-only .NET-Framework WinForms) no Android BluetoothLeGatt, no iOS CoreBluetooth, no Web Bluetooth. It reaches a controller only through serial (System.IO.Ports / RJCP.IO), a TCP Telnet socket, or an ESP8266 WebSocket. None of ArctZ's mobile/web BT APIs are exercised, so LaserGRBL offers zero cross-platform BT-API reference.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting is the core mode and LaserGRBL is a clean, compact C# reference for it. A StreamingMode enum (selected via CurrentStreamingMode setting) offers three strategies: (1) Buffered = classic GRBL character-counting send-ahead, streaming as many complete lines as fit under the controller RX buffer without waiting for each 'ok'; (2) Synchronous = simple send-response, one command then wait for its 'ok' before the next; (3) RepeatOnError = like buffered but re-queues a failed command (up to 3 attempts) via a single retry slot. Buffer accounting: GrblCore tracks mUsedBuffer; HasSpaceInBuffer(GrblCommand) tests (mUsedBuffer + command.SerialData.Length) <= BufferSize before sending; on each 'ok'/'error', ManageCommandResponse() decrements mUsedBuffer by the completed command's SerialData.Length, freeing room for the next line. This is true character counting, not naive wait-for-ok nor blind buffered dump.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > More sophisticated than Candle's hard-coded 127. DEFAULT_BUFFER_SIZE = 127 is the fallback, but LaserGRBL AUTO-DETECTS the real controller buffer at connect (mAutoBufferSize) and also reads it live from the GRBL 1.1 status report 'Bf:' field (Bf:blocks,bytes). Supported/recognized buffer sizes include 128, 254, 255, 256 and even 10240 (large-buffer controllers such as ESP32/FluidNC or grblHAL). So streaming is sized to the actual firmware rather than assuming the 128-byte AVR GRBL buffer — a directly relevant pattern for ArctZ targeting FluidNC, whose RX buffer differs from classic GRBL.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Single unified pipeline (NOT CNCjs-style separate feeder/sender). Commands flow through one path: mQueue (enqueued, awaiting transmit) → mPending (sent, awaiting 'ok'/'error') → mSent (completed history for UI). File lines, UI/console commands and jog commands all share this pipeline and therefore compete for the same character-count buffer budget. Queue pointers (mQueuePtr/mSentPtr) let the core temporarily redirect enqueue/history during config import/export. Real-time single-byte commands are the exception and bypass the queue entirely (see realtime_bypass_path).
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time single bytes are written straight to the transport via a SendImmediate()/immediate-write path, bypassing mQueue/mPending and the mUsedBuffer accounting, so they are delivered regardless of buffer fullness. Documented immediate bytes: status query 0x3F ('?'), feed-hold '!' and cycle-resume '~', soft-reset 0x18 (Ctrl-X), jog-cancel 0x85, and feed/rapid/spindle override bytes in the 0x90–0x99 range (driven by ManageOverrides() reconciling mTarOv* targets against mCurOv* current values). This is exactly the immediate-dispatch mechanism ArctZ needs for a responsive joystick and e-stop.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both step and continuous jog, and version-aware. Modern GRBL 1.1+: EnqueueJogV11() emits '$J=' jog commands (relative moves with feed). Legacy GRBL 0.9: EnqueueJogV09() emulates jogging with ordinary G1/G91/G90 motion because 0.9 lacks '$J='. Continuous (hold-to-move) jog is managed by a dedicated ContinuousJog class holding the active direction/speed target; the TX thread's HandleContinuosJog() polls ContinuousJog.GetAndClearTarget() and enqueues the next jog increment while a direction is held. Supported directions include N/S/E/W and diagonals NE/NW/SE/SW plus Zup/Zdown, and special targets Home/Position/Abort. Step/incremental jog sends a single fixed-distance '$J='.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > Uses the GRBL 1.1 real-time jog-cancel byte 0x85 via the immediate path (SendImmediate(0x85)). Continuous jog is driven by the TX thread repeatedly polling ContinuousJog.GetAndClearTarget(): when the held target changes or clears, a new jog (or the 0x85 cancel) is issued. This poll-and-clear design means a lost key-up stops feeding new increments (the target clears) and a 0x85 flushes any in-flight jog — a more UI-friendly model than Candle's blocking 5-second busy-wait for the machine to leave Jog state. Good pattern reference for ArctZ's VirtualJoystick, though exact runaway-protection bounds math is less explicit than Candle's soft-limit pre-sizing.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses GRBL 1.1 '<...>' status reports, version-aware via StatusReportVersion(). Recognized fields include machine state token, Ov:feed,rapids,power (overrides), Bf:blocks,bytes (planner/RX buffer), MPos:x,y,z (machine position), WPos:x,y,z (work position), WCO:x,y,z (work-coord offset), FS:feed,spindle. ParseMachineStatus() extracts the state; ParseMPos/ParseWPos/ParseWCO/ComputeWCO() handle positions; ParseOverrides() updates current override state. It also tolerates older GRBL 0.9 report shapes.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > A rich MacStatus enum drives the state machine: Disconnected, Connecting, Idle, Run, Hold, Door, Home, Alarm, Check, Jog, Queue, plus LaserGRBL-specific pseudo-states Cooling, AutoHold and Tool. The state token from each status report sets the current status, RiseMachineStatusChanged() fires an event, and 'Hold' is auto-relabeled 'Cooling' when a cooling-request flag is set (a laser-specific feature). State gates control enable/disable and jog availability.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active host-driven polling (not push). A QueryTimer (a PeriodicEventTimer) fires at CurrentThreadingMode.StatusQuery interval (default 500ms in Fast threading mode) and QueryPosition() sends the immediate '?' byte (0x3F). The interval is part of a configurable ThreadingMode (Slow/Fast, with StatusQuery/TxLong/TxShort/RxLong/RxShort tunables), so ArctZ can note that on a bandwidth-limited BLE link this fixed-interval '?' polling would compete with jog traffic and should be widened.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Distinguishes machine vs work coordinates: mMPos (machine, absolute), mWCO (work-coordinate offset), and WorkPosition computed as mMPos - mWCO (with reverse ComputeWCO()). Zeroing/offsets are set via G10 L20 / G92-style commands from the UI. As a laser sender it centers on the active work coordinate system (typically G54) and does not expose a rich G54–G59 WCS switcher the way a general CNC controller would.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > WinForms, event-driven — NOT MVVM and not textbook MVC, but with a cleaner model/UI split than Candle. The protocol/streaming 'model' lives in the non-UI GrblCore class (LaserGRBL/Core/), and the forms (MainForm, ConnectionForm/console, jog panel, custom-button bar, settings dialogs) subscribe to GrblCore events (machine-status-changed, override-changed, file-loaded, etc.). There are no XAML-style view-models or data-binding; UI updates are done by event handlers manipulating WinForms controls. Divergent from ArctZ's CommunityToolkit.Mvvm data-binding, but the GrblCore-as-model pattern is a reasonable conceptual analogue to an ArctZ core service.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Moderately good — a better reference than Candle for ArctZ's reusable-core goal. Both transport (IComWrapper) AND protocol (GrblCore) are separated from the UI: GrblCore owns the mComWrapper, the three-queue streaming, buffer accounting, status parsing, state machine, jog logic and overrides, and communicates outward purely through events, so the forms are consumers rather than owners of protocol logic. This means a headless/alternate UI could in principle drive GrblCore. The caveats: GrblCore is a single large laser-specialized class (not layered) and is bound to Windows/.NET-Framework threading and some WinForms conveniences, so it is not literally portable to Avalonia/mobile without extraction work. ArctZ should emulate the IComWrapper seam and the GrblCore-as-nonUI-model idea, while splitting the monolith into transport/protocol/streaming layers.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Handles GRBL alarm and error codes with human-readable descriptions: LaserGRBL ships a mapping of numeric ALARM:n and error:n codes to descriptive text (error/alarm code tables), shown in the console and diagnostics, rather than Candle's literal-string matching. Recovery follows GRBL convention — $X unlock, $H home, soft-reset (Ctrl-X). Alarm is a MacStatus state that gates the UI.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > Moderate, with proactive health monitoring. DetectHang() watches time since last status (raises StopResponding when no response for >5s and beyond status-period × 10). HandleMissingOK() detects a stuck stream (running + pending + buffer full + no movement ~10s) and injects synthetic 'ok's via CreateFakeOK() to recover streaming. A CH340-driver workaround (FixCH340 flag with a HasIncomingData() pre-check) reduces serial read exceptions. On disconnect it clears queues; there is no BLE-style auto-reconnect or mid-job resume of a partially streamed file — an interrupted job is restarted. Better link-hang recovery than Candle, but still oriented to stable serial rather than flaky BLE.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes — a 2D top-down toolpath preview rendered with GDI+ (the natural view for a laser/engraver: X/Y plane with laser power shown as color/intensity), including live cursor/progress overlay. No 3D visualizer (unlike Candle's OpenGL view); Z is not a primary concern for lasers.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — G-code is parsed into GrblCommand objects (within a GrblFile) that track modal state; G2/G3 arcs are interpreted for the 2D preview and time/analysis estimates. Comments are stripped and commands analyzed for laser power (S)/motion so the preview can color-map engraving intensity.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Windows-only. Built on .NET Framework WinForms, so it runs natively only on Windows (community reports of running under Wine/Mono on Linux/macOS with caveats). No mobile (Android/iOS) and no browser/web build from the mainline. This is a sharp divergence from ArctZ's Avalonia + cross-platform-core + thin-heads approach; LaserGRBL offers no reference for mobile/web transport or touch UI. Notably a separate community effort (Gilmore-Enterprises/LaserGRBL-Linux) is porting it to Avalonia/.NET 10 — the very stack ArctZ uses — which could itself be worth watching, though it is an independent fork, not the mainline.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Classic GRBL '$$' numbered-settings model. A GrblConf/configuration subsystem reads the '$$' report, edits parameters in a settings grid, and writes them back as '$x=val', with import/export of the config to/from file and a compare/restore workflow. There is NO FluidNC YAML config-file upload/download and no grblHAL-specific settings path — it is the numbered '$'-settings model only (works against FluidNC's GRBL-compatible '$$' subset but not its YAML).
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One. A single active IComWrapper/mComWrapper drives exactly one controller per application instance; no multi-connection support.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > GRBL-family, version-aware. Detects/handles classic GRBL 0.9 and 1.1 differently (e.g. EnqueueJogV09 vs EnqueueJogV11 for jogging, StatusReportVersion() for report parsing, plus welcome-string / $I '[VER:]/[OPT:]' interrogation and buffer-size auto-detect). FluidNC and grblHAL are driven insofar as they are GRBL-1.1-compatible (and the large auto-detected buffer sizes 254/255/256/10240 accommodate ESP32-class controllers), but there is no FluidNC-YAML- or grblHAL-specific dialect handling. Focus is laser-oriented GRBL builds.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > No traditional CNC probing — as a laser-cutter/engraver sender it does not implement G38.x straight-probe workflows or a probed height-map/auto-leveling surface mesh (lasers don't touch-probe). Its 'leveling' concerns are camera/overlay and material-focus aids rather than Z touch-probing. Not a reference for ArctZ probing needs.

### Прочая информация
- **type**: sender
- **research_date**: 2026-07-24

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- bt_reconnect_behavior
- bt_pairing_model
- mode_mutual_exclusion
- jog_latency_budget

---

## 12. LightBurn <a id="lightburn"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Native desktop application built in C++ on the Qt framework (cross-platform Qt, community-observed to be Qt Widgets-based). Distributed as native builds for Windows, macOS and Linux. Closed-source commercial product by LightBurn Software; it is both a vector layout/design editor AND a machine controller (sender) for laser cutters/engravers, with a secondary G-code/CNC mode. Because the source is not public, all internal-architecture fields below describe externally observable behavior only.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > Commercial, closed-source, paid perpetual-with-updates license (per-seat activation, license key + limited number of activations). NOT open source — no code is available to study or reuse, so it is only usable as a UX/behavior reference for ArctZ, never as a source of code or algorithms. Only external, black-box behavior can inform ArctZ.
- **repository** _Ссылка на исходный код_:
  > None public. Closed-source proprietary software; no source repository. Product site: https://lightburnsoftware.com ; documentation: https://docs.lightburnsoftware.com ; community forum: https://forum.lightburnsoftware.com
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Actively developed and commercially maintained as of 2026 (regular paid point releases; the 1.7.x line was current through mid-2026, e.g. 1.7.08 referenced for the last supported Ubuntu builds). Frequent releases add device profiles and fix controller compatibility. Note: the vendor announced dropping some older-Linux/32-bit targets over time, but Windows/macOS/Linux desktop support remains active.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > For GRBL-family (G-code) devices LightBurn supports: (1) Serial/USB (COM port, default 115200 baud, optional DTR) — the primary and recommended path, including for FluidNC per its wiki; (2) Network — 'Ethernet/UDP' and 'WiFi/TCP' connection types selectable when creating a GRBL device manually, entering the controller IP/hostname and a network port (default 23, i.e. raw-TCP/telnet-style). Ruida/DSP controllers use their own UDP-over-Ethernet path. There is NO native Bluetooth transport and NO documented WebSocket transport for GRBL — Bluetooth-Classic devices are reached only if the OS exposes them as a virtual serial COM port. FluidNC's own docs steer users to Serial/USB (or a virtual COM bridge for WiFi).

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Two selectable G-code transfer modes for GRBL devices: 'Buffered' (default) and 'Synchronous'. Buffered is the standard GRBL character-counting / send-ahead protocol (keeps GRBL's ~128-byte RX buffer filled by tracking outstanding characters against incoming 'ok' responses) — faster and the recommended default. Synchronous is a stricter send-one-line/wait-for-'ok' send-response mode, offered as a fallback for controllers that stutter/stall/reset under buffered streaming. This mirrors the classic GRBL character-counting-vs-simple-send-response tradeoff; the buffered path is the direct behavioral analogue to what ArctZ's file streaming needs, with a synchronous fallback for flaky links.

### Jogging vs File Streaming
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both step/incremental and continuous jog are provided via the Move window. Step jog moves a fixed configured distance per click; 'continuous jog' is an explicitly enable-able mode (hold-to-move / press-and-move) that keeps the axis moving while the control is held. For GRBL devices these are emitted as GRBL 1.1 '$J=' jog commands. There is no absolute-position 'jog' beyond typed 'Go to position' moves in the Move panel.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes — a 2D job Preview (simulated laser run over the laid-out design, with time estimate and playback scrubbing) plus the main 2D design/layout canvas. It is a full vector layout editor, so the 'toolpath' is the designed geometry itself. There is no 3D CNC-toolpath visualizer (LightBurn is laser/2D-oriented, not a 3D milling previewer).

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Desktop only: Windows (10+), macOS (10.13+) and Linux (Ubuntu-based, through the supported versions of the 1.7.x line). NO mobile (Android/iOS) build and NO browser/web build. This is a key divergence from ArctZ's Avalonia stack (which additionally targets Android/iOS/WASM): LightBurn offers no reference for mobile/web transport or touch-first UI, only a desktop model.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > GRBL-centric. LightBurn exposes GRBL '$$' numbered settings through an 'Edit > Machine Settings' editor (read/write '$'-settings by name/number) and lets users type '$' commands in the Console. There is NO FluidNC YAML config upload/download workflow inside LightBurn (FluidNC YAML editing is done via FluidNC's own WebUI/serial), and no grblHAL-specific settings path — FluidNC/grblHAL are treated purely as GRBL-1.1-compatible '$'-settings controllers.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One controller per configured device/connection at a time (single active machine connection). LightBurn supports multiple saved device profiles but drives one at a time.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > Broad. G-code side: GRBL, GRBL-M3 (0.9/older laser mode), Smoothieware, Marlin — FluidNC and grblHAL are used via the GRBL profile (GRBL 1.1 compatibility), not as first-class distinct dialects; FluidNC's own docs recommend adding it as a GRBL device. Beyond G-code, LightBurn also drives proprietary DSP laser controllers (Ruida, Trocen/AWC, TopWisdom) and galvo/EZCad2 controllers via dedicated profiles. Dialect is chosen by the user at device-creation (profile selection), with the controller's welcome/version string shown in the Console for confirmation rather than fully automatic dialect detection.

### Прочая информация
- **type**: sender
- **research_date**: 2026-07-24

### Неопределённые поля (uncertain)
- transport_abstraction_maturity
- bt_profile_type
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- bt_reconnect_behavior
- bt_pairing_model
- os_bt_api
- rx_buffer_handling
- command_queue_model
- realtime_bypass_path
- jog_cancel_mechanism
- mode_mutual_exclusion
- jog_latency_budget
- status_report_format
- state_machine
- status_report_polling_model
- coordinate_system_handling
- ui_pattern
- comm_ui_separation
- alarm_codes
- reconnect_logic
- arc_modal_parsing
- probing_support

---

## 13. OpenBuilds CONTROL <a id="openbuilds-control"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Electron desktop app in JavaScript. A single Electron process is BOTH the backend and the UI: the main process (index.js, ~4000 lines) runs an Express + socket.io(v4) server plus the Node `serialport` v12 controller link, while the Chromium renderer (app/, jQuery + Metro UI 4 + socket.io client) is the GUI. Electron ^23. Runs on Windows, macOS (x64+arm64), Linux and Raspberry Pi (detect-rpi launches a Chrome kiosk).
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > AGPL-3.0 (declared in package.json). Note: the GitHub repo badge shows a GPL-3.0 shield, but the authoritative package.json field is "AGPL-3.0" — treat as AGPL-3.0 (network-copyleft) when reusing code/ideas.
- **repository** _Ссылка на исходный код_: https://github.com/OpenBuilds/OpenBuilds-CONTROL
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Latest release v1.0.390 published 2025-06-02; most recent commit also 2025-06-02. Historically very active (~990 commits) but has slowed: as of 2026-07 the last update is ~13 months old. v1.0.390 changelog: removed mailing-list signup gate and lowered update-check frequency.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Two controller transports only: (1) serial/USB via Node `serialport` v12 (`new SerialPort({path,baudRate,rtscts,hupcl})`); (2) WiFi/network via RAW TELNET TCP on port 23 (`net.connect(23, data.ip)`) for networked grblHAL/FluidNC controllers, selected by `data.type == 'telnet'`. Network controllers are auto-discovered with an `evilscan` TCP port-23 sweep that reads the welcome banner (Grbl/GrblHAL). There is NO Bluetooth, NO BLE, and NO WebSocket transport TO the controller. (socket.io is used only for the GUI<->backend link, never for the machine link.) HTTP API (Express + multer/formidable) exists for external G-code upload/integration, not as a controller transport.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Duck-typed single-variable abstraction. A single module-level `port` holds either a `SerialPort` or a `net.Socket`; both expose `.write()`, `.isOpen`, `.pipe(new ReadlineParser({delimiter:'\r\n'}))` and `open`/`close`/`error`/`data` events, so the streaming engine (`send1Q`/`machineSend`/`BufferSpace`) is transport-agnostic by STRUCTURAL typing rather than by a declared interface or polymorphic class. The connection is chosen by a `data.type` string switch (`usb` vs `telnet`) inside the `connectTo` socket handler. Protocol/command logic is cleanly decoupled from the transport (a real strength), but it is not a formal, extensible transport interface — adding a third backend (e.g. BLE) would mean editing the switch and duck-typing another object. Moderate maturity: better than a hard-wired serial-only design, weaker than a first-class transport interface.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No Bluetooth support of any kind. The codebase has no BLE/GATT/Nordic-UART (NUS) and no Bluetooth-Classic SPP/RFCOMM handling — only serial USB and Telnet TCP. A BT-Classic device that the OS has paired and exposed as a virtual COM port could in principle be opened through the normal serial path, but nothing Bluetooth-specific exists in CONTROL. This makes OpenBuilds CONTROL a weak Bluetooth reference for ArctZ, whose primary target (ESP32-S3/C3-class FluidNC) is BLE-only.
- **bt_reconnect_behavior** _Поведение при разрыве связи — автопереподключение, feed hold/alarm станка_:
  > No Bluetooth, so no BT-specific reconnect. For CONTROL's real transports the pattern is: on serial `close`/`error` (or Telnet `port.destroy()`), `stopPort()` sets connectionStatus=0, clears the `?` status poll interval, and DUMPS both `gcodeQueue` and `sentBuffer`. There is NO automatic controller-link reconnect — the operator must reselect and reopen the port/IP. CONTROL does not itself feed-hold the machine on link loss (GRBL keeps executing whatever is already in its planner buffer). A background `PortCheckinterval` (1 s) only refreshes the list of available USB ports for the menu; it does not auto-reconnect.
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > No Bluetooth pairing logic in the app. The only 'pairing-like' step is OS-level: if a user had a BT-Classic SPP virtual COM port, CONTROL would just open it as a serial device; CONTROL performs no bonding/pairing itself.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting (buffered) streaming. A single `gcodeQueue` array + `queuePointer` holds the program; `sentBuffer[]` holds lines sent-but-not-yet-acked. `send1Q()` (grbl case) sends the next queued line ONLY if `gcodeQueue[queuePointer].length < BufferSpace()`, then pushes it to `sentBuffer`; otherwise it sets `status.comms.blocked=true`. On each received `ok`, the handler does `command = sentBuffer.shift()`, sets `blocked=false`, and calls `send1Q()` again to top-up the controller buffer. Real-time bytes bypass this entirely via `addQRealtime()`. This is a classic multi-line-in-flight char-counting sender, NOT a one-line send-wait-ok model.
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Explicit RX-buffer accounting. Constants: `GRBL_RX_BUFFER_SIZE = 127`, `GRBLHAL_RX_BUFFER_SIZE = 1023`. `BufferSpace('grbl')` returns `(rxBufferSize - 1) - sum(sentBuffer[i].length)`. It prefers the buffer size REPORTED by the controller (parsed from the `$I` `[OPT:...]` block into `status.machine.firmware.rxBufferSize`) when > 0; otherwise it falls back to the platform default — 1023 for platform `grblHAL`, else 127 (used for classic Grbl AND FluidNC). Consequence: grblHAL streams with a much deeper buffer, while FluidNC is treated with the conservative 127-byte Grbl default unless the controller advertises a larger OPT buffer.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > SINGLE UNIFIED queue — the key contrast with CNCjs's dual Feeder/Sender design. Jogs (`socket.on('jog')` -> `$J=G91G21...`), MDI/console (`runCommand`), and full file jobs (`runJob`) ALL push onto the same `gcodeQueue` via `addQToEnd()` and are drained by the same `send1Q()` character-counting engine. `addQToEnd` also injects a `$G` modal-state query after modal/tool commands. The ONLY thing that bypasses the queue is real-time bytes (`addQRealtime`). So, unlike CNCjs, manual jogs are interleaved into the same stream as the running program rather than living in a separate feeder — simpler, but it means jog and file streaming are not queue-isolated (mutual exclusion is instead handled by client-side Idle checks and GRBL's own state machine, see mode_mutual_exclusion).
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > `addQRealtime(byte)` -> `machineSend(byte, realtime=true)` -> `port.write(byte)` directly, with NO trailing newline, NO queue insertion and NO `ok` bookkeeping. Used for `?` (status), `!` (feed hold), soft-reset `0x18` (Ctrl-X), jog-cancel `0x85`, feed/rapid overrides `0x90`-`0x94`, spindle overrides `0x99`/`153`-`157`, and `0x9E` (stop spindle/laser, on v1.1d). This mirrors GRBL's own design where real-time bytes are intercepted before the line buffer. Everything non-real-time gets `\n` appended and flows through `send1Q`/`sentBuffer`. Directly reusable model for ArctZ: keep a real-time byte path independent of the line queue.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both incremental and continuous, plus absolute go-to. Incremental: `socket.emit('jog', 'dir,dist,feed')` -> backend `$J=G91G21<dir><dist>F<feed>`. Continuous (hold-to-move): on press the frontend sends ONE long relative jog `$J=G91 G21 <dir><distance> F<rate>` where distance = 1000 mm, or is computed down to the soft-limit boundary when `$20` soft-limits are enabled, then cancels on release (see jog_cancel_mechanism). Absolute: DRO entry / `jogTo` sends `$J=G90 ...`. Continuous jog is only started when `laststatus.comms.runStatus == 'Idle' || 'Door:0'`.
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > On button/touch release the frontend calls `cancelJog()` -> `socket.emit('stop', {stop:false, jog:true, abort:false})`; backend `stop()` detects `data.jog` and issues the real-time byte `0x85` (jog cancel), which flushes queued jog motion and decelerates without an alarm. A GLOBAL `$(document).mouseup` handler ALSO fires `cancelJog()` if `continuousJogRunning`, so a jog is cancelled even when the pointer slid off the button before release — a deliberate runaway guard. Because continuous jog is implemented as one long bounded `$J=` move (1000 mm or soft-limit-bounded) rather than an unbounded stream, a MISSED cancel results in motion to that finite target, not infinite travel. Highly relevant to ArctZ VirtualJoystick release handling: send 0x85 on any release/blur/pointer-loss.
- **mode_mutual_exclusion** _Блокировка jog во время файлового стриминга и наоборот (GRBL lockout error)_:
  > Client-side guard for continuous jog: it only starts when `runStatus` is `Idle`/`Door:0`, otherwise a `toastJogNotIdle()` warning is shown. Incremental jogs are simply appended to the shared `gcodeQueue`; there is no separate feeder isolation, so the ultimate lockout for `$J=` during an active job is GRBL's own firmware state machine (which rejects jog while in Run). No hard client-side block prevents queueing an incremental jog mid-stream, but the single-queue model plus GRBL rejection keeps it safe in practice.
- **jog_latency_budget** _Транспортная задержка jog-команд и минимальный интервал отправки при удержании (особенно по BLE/WiFi)_:
  > Command path = GUI socket.io hop (localhost, or LAN for the mobile PWA) -> backend `port.write`. Over serial/USB latency is low and deterministic; over Telnet a LAN round-trip is added. Status is polled with `?` every 200 ms and the full status object is pushed to clients every 100 ms. Continuous jog deliberately avoids high-rate resends (a single long bounded move instead of a timer-driven burst), which makes it tolerant of higher-latency/lossy links (WiFi/Telnet) and lighter on the channel. CONTROL has no BLE path, so it offers no direct data point on BLE notification/write jog-latency budgets (a gap for ArctZ, where BLE write/notify intervals dominate).

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > `parseFeedback()` parses GRBL 1.1 `<...>` reports via string/regex: leading machine state, `WCO:` work-coordinate offset, `WPos:`/`MPos:` position (up to 4 axes X/Y/Z/A, `has4thAxis` auto-detected when a 4th field is present), applying WCO to derive the other of work/machine. A real-time feed-rate indicator is shown in the UI. Parsing is ad-hoc regex/substring rather than a structured field map; pin-state/override sub-fields are less fully modeled than in CNCjs.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Two layers. (1) Machine `runStatus` taken from the first token of each `<...>` report: Idle/Run/Hold/Alarm/Jog/Door/Home/Check (e.g. `Hold:0` triggers `pause()`; `Alarm` sets connectionStatus 5). (2) A numeric `status.comms.connectionStatus` workflow state: 0=disconnected, 1=connected, 2=connected/idle-or-finished, 3=streaming a job, 4=paused, 5=alarm, 6=firmware-upgrade. UI enable/disable and job flow are driven off these.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active polling. On connect a `statusLoop = setInterval(() => addQRealtime('?'), 200)` queries status every 200 ms while connected (GRBL does not auto-push). Separately the backend pushes the whole `status` object to all socket.io clients every 100 ms (`frontEndUpdateLoop`). Frequent `?` polling would load a BLE notification channel — a relevant caveat for ArctZ, though moot for CONTROL's serial/Telnet links.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Work coordinate systems G54-G59 are handled through `$G` modal-state tracking (auto-queried after modal commands); WCO from status reports is applied so both MPos and WPos are known and distinguished; DRO entry zeroes work position via `G10 P0 L20`. Go-to-zero (work) and G53-based go-to-machine-zero actions are exposed.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Not MVVM/MVC. The GUI is jQuery + Metro UI 4 (a Windows-Metro CSS/JS framework) with event-driven DOM manipulation, consuming backend socket.io events (`status`, `data`, `ok`, `grbl`, `jobComplete`, `queueCount`, `fluidncConfig`). The backend is event-driven Node. This does NOT map onto ArctZ's CommunityToolkit.Mvvm/compiled-binding pattern, though the backend-owns-state / UI-renders-pushed-events split is conceptually comparable to a ViewModel feeding a View.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Strong separation and the most reusable idea here. ALL controller comms, the queue/streaming engine, protocol parsing, alarm/error decoding and the state machine live in the backend (index.js); the front-end is a socket.io consumer that emits intent commands (`jog`, `runJob`, `stop`, `runCommand`, `feedOverride`) and renders pushed events. Because the link is socket.io, MULTIPLE clients (the Electron desktop window plus a mobile-browser Jog PWA at `/jog`) can attach to the same backend concurrently. Caveat vs CNCjs: CONTROL bundles backend+UI in one Electron app rather than shipping a standalone headless server, but the client/server boundary over socket.io is architecturally the same and the comms core is cleanly separable — the aspect most worth emulating for ArctZ (a transport/protocol core independent of the UI).

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > GRBL `ALARM:<n>` is mapped through `grblStrings.alarms(code)` to a human-readable message, sets connectionStatus=5 and emits `toastErrorAlarm`. `error:<n>` is mapped via `grblStrings.errors(code)`, pops one entry from `sentBuffer`, stops the queue and sets connectionStatus=5. Clearing: `clearAlarm` method 1 sends `$X` (unlock); method 2 dumps `gcodeQueue`+`sentBuffer`, sends Ctrl-X (`0x18`) then `$X` after 500 ms.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > No automatic controller-link reconnect. On serial/Telnet `close`/`error` the backend sets connectionStatus=0 and calls `stopPort()` which dumps `gcodeQueue`+`sentBuffer` and clears the status poll; the operator must manually reopen. `PortCheckinterval` (1 s) only refreshes the available-ports list for the connect menu. The front-end's own socket.io session to the backend does auto-reconnect, but that is the GUI link, not the machine link. See bt_reconnect_behavior.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes. A WebGL 3D G-code viewer (app/lib/3dview, a 'verylitegcodeviewer' web worker) renders the toolpath and live tool position (2D top view also usable). Loaded G-code is parsed in a web worker (`parseGcodeInWebWorker`). An Ace code editor provides G-code text editing/highlighting.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > The G-code parser (web worker) tracks modal state and expands arcs (G2/G3) into segments for the viewer, honoring units (G20/G21) and distance mode (G90/G91). Sufficient for toolpath preview and bounding-box/grid drawing.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Desktop via Electron: Windows, macOS (x64 + arm64), Linux; plus Raspberry Pi (detect-rpi -> Chrome kiosk). Mobile control is delivered as a built-in Mobile Jog PWA (app/jog with service-worker + manifest, iOS-aware) served over the LAN — a phone/tablet browser connects to the same backend socket.io. So, like CNCjs, cross-platform reach comes from Electron + a browser client rather than a compiled native shared UI, and there is NO native mobile app and NO in-app Bluetooth (mobile is browser-to-backend, with the serial/Telnet link terminating on the host). Direct contrast with ArctZ/Avalonia, which compiles ONE native UI across desktop/mobile/browser and can host in-app BLE on the mobile head itself.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Grbl/grblHAL: `$$` settings (rich UI in grbl-settings.js with Basic/Advanced tabs, per-setting descriptions, search, and toolhead/servo wizards), plus `$G` (modals), `$I` (build/version + `[OPT:]` buffer/axis info) and offset reads. FluidNC: `$CD` dumps the YAML config, which is streamed line-by-line into `fluidncConfig`, shown in an Ace YAML editor and `YAML.parse`d (emitted as `fluidncConfig` socket event); an `xmodem.js` dependency is present for uploading FluidNC config (per issue #283 this upload/flashing integration is partial/in-progress). grblHAL is noted in-code as moving toward config.yaml, but CONTROL still primarily reads its `$$`-style settings.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > Multi-client: several socket.io clients (the Electron desktop window and one or more Mobile Jog PWA browsers) can attach to a single backend simultaneously and share the one controller link. The single point of contention is the single serial/Telnet `port` to the machine.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > GRBL 1.1 (platform `gnea`), grblHAL, and FluidNC — all driven through `firmware.type = 'grbl'` (Grbl 1.1 protocol). Smoothieware has partial handling (`type='smoothie'`), and OpenBuilds' own BLOX board is a grblHAL variant. Dialect is AUTO-DETECTED from the welcome/banner string: `Grbl` -> gnea, `GrblHAL`/`[FIRMWARE:grblHAL]` -> grblHAL, `FluidNC v...` -> FluidNC; then refined by `$I` `[VER:]`/`[OPT:]`. Versions below 1.1 are rejected with an upgrade prompt.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes (single-axis). A Probing Wizard issues `G38.2 <dir>-5 F1` then `G92 <dir> <probeOffset>` for touch-off (touching the probe also doubles as confirm, per PR #339). There is also a Surfacing Wizard, but that generates a spoilboard-flattening toolpath (facing), NOT probe-based autoleveling. No PCB height-map / bilinear autolevel surface compensation is provided (a capability CNCjs's Autolevel plugin has and CONTROL lacks). Not a priority for ArctZ.

### Прочая информация
- **type**: sender

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- os_bt_api

---

## 14. OpenCNCPilot <a id="opencncpilot"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > C# / WPF (Windows Presentation Foundation), .NET Framework (classic desktop .exe), Windows-only. 3D toolpath and height-map viewport uses HelixToolkit.Wpf. Single Visual Studio project ('OpenCNCPilot'); the entire controller-communication layer is one class, 'OpenCNCPilot/Communication/Machine.cs' (~1240 lines), with all UI as WPF code-behind partial classes (MainWindow.xaml.*.cs). Same broad family as ArctZ (.NET + XAML) but WPF/.NET Framework, not .NET 10 + Avalonia, and it is not an MVVM-toolkit app.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > MIT (permissive; code and ideas can be reused with attribution/license notice — unlike the GPLv3 senders such as UGS/gSender). Confirmed via GitHub license metadata (spdx_id: MIT).
- **repository** _Ссылка на исходный код_: https://github.com/martin2250/OpenCNCPilot
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Lightly but still maintained by the single author. Latest tagged release v1.5.13 (2024-09-01); last repository push 2025-04-28. ~424 stars, ~125 forks, ~39 open issues. Recent releases added TCP connection support (v1.5.11, 2024-02-28), GRBL settings import/export + probe XY offset + uCNC firmware support (v1.5.12, 2024-04-26).

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Two transports, selected by a ConnectionType enum { Serial, Ethernet } in Machine.cs: (1) Serial/USB COM port via System.IO.Ports.SerialPort (configurable baud, optional DtrEnable to reset classic-Arduino boards), used as SerialPort.BaseStream; (2) 'Ethernet' = a raw TCP socket via System.Net.Sockets.TcpClient to a configured IP:port (e.g. an ESP32/grblESP telnet-style port), used as TcpClient.GetStream(). There is NO WebSocket transport, NO Telnet protocol negotiation (it is a raw byte TCP stream), and NO Bluetooth of any kind.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Present but lightweight — abstracted at the .NET stream level rather than via a dedicated transport interface. Machine.cs holds a single 'private Stream Connection' field; Connect() switches on ConnectionType, opens either a SerialPort.BaseStream or a TcpClient network stream, and assigns the resulting System.IO.Stream to 'Connection'. The whole protocol/streaming layer then talks only to a StreamReader/StreamWriter wrapped around that Stream, so it is genuinely transport-agnostic above the byte layer — adding a new transport (e.g. a BLE stream for ArctZ) means producing another System.IO.Stream. However, the abstraction is informal: there is no ITransport/IConnection interface, the connection-type choice and its parameters are read directly from a global Properties.Settings singleton, and Connect()/Disconnect() contain per-type switch statements. ArctZ can borrow the 'protocol talks to an abstract byte stream' idea but should formalise it as an explicit transport interface with DI rather than a switch-on-enum + global-settings model.

### Bluetooth Specifics
- **radio_coexistence** _Могут ли WiFi и Bluetooth работать одновременно на одной плате_:
  > Not applicable — OpenCNCPilot does not manage the controller's radios. It connects over USB serial or raw TCP; any WiFi/BT coexistence is entirely a firmware/board concern outside the app's scope.
- **firmware_build_variant** _Нужна ли отдельная сборка/переключение прошивки для включения BT_:
  > Not applicable — OpenCNCPilot neither selects nor requires any Bluetooth firmware build. It connects to whatever GRBL endpoint is presented on serial or TCP.
- **bt_mtu_packet_size** _MTU/размер полезной нагрузки пакета, влияние на фрагментацию G-code строк_:
  > Not applicable — no BLE/SPP link, so no MTU / packet-fragmentation handling. On serial and TCP it streams whole G-code lines terminated by '\n' and applies GRBL character-counting flow control against a configured controller buffer size (see streaming_strategy).
- **bt_throughput** _Измеренная или заявленная пропускная способность в сравнении с USB/WiFi_:
  > Not applicable — no Bluetooth link to benchmark. Throughput is bounded by the USB serial baud rate or the TCP link, both far above BLE rates.
- **os_bt_api** _Системный API на каждой платформе (Windows, Android, iOS CoreBluetooth, Web Bluetooth) — iOS не поддерживает SPP_:
  > Not applicable — OpenCNCPilot uses no OS Bluetooth API (no Windows BluetoothLE/RFCOMM, no Android, no iOS CoreBluetooth, no Web Bluetooth). It is Windows-only WPF; its transports are System.IO.Ports serial and System.Net.Sockets.TcpClient. It provides no reference for the per-platform BT APIs ArctZ must implement itself (Windows/Android/iOS CoreBluetooth/Web Bluetooth).

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Classic GRBL character-counting streaming, implemented inline in Machine.cs's Work() worker thread. It reads a configured 'ControllerBufferSize' (Properties.Settings.Default.ControllerBufferSize, GRBL's ~128-byte RX buffer) and tracks a running 'BufferState' (bytes believed in-flight). In SendFile mode it sends the next file line only while (line.Length + 1) < (ControllerBufferSize - BufferState); on send it increments BufferState by line.Length+1 and enqueues the line in a 'Sent' queue. When an 'ok' reply arrives it dequeues from 'Sent' and decrements BufferState by that line's length+1; on 'error:' it likewise dequeues the offending line, reports it, and (for file mode) aborts to Manual. This keeps the controller RX buffer as full as safe rather than a slow send-one-wait-for-ok. Whitespace is stripped from lines before sending. Optional 'SyncBuffer' can re-derive BufferState from the 'Bf' field of a status report after a button press.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Four synchronized queues inside Machine.cs rather than a CNCjs-style feeder/sender object pair: 'ToSend' (normal manual/MDI lines), 'ToSendPriority' (single real-time control characters — soft reset, feed hold, cycle start, jog cancel, overrides), 'ToSendMacro' (macro lines run one-at-a-time gated on Idle+empty-buffer), and 'Sent' (in-flight lines awaiting 'ok', used for character-counting bookkeeping). A single worker thread services all four with priority ordering: ToSendPriority is flushed first every loop iteration, then file streaming OR macro OR ToSend depending on OperatingMode. So jogging/manual (ToSend / real-time) is kept structurally separate from file streaming (the File array + FilePosition), and the two never interleave because they are gated by mutually-exclusive OperatingModes. This maps onto ArctZ's need to separate VirtualJoystick/jog traffic from file playback, though it is expressed as mode-gated queues on one thread rather than independent feeder objects.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > Real-time single bytes are injected via the dedicated 'ToSendPriority' queue, which the worker loop drains and writes+flushes before any buffered line each iteration, completely bypassing the character-counting buffer and OperatingMode gating. Public helpers enqueue them: SoftReset() -> 0x18 (also clears all queues and resets overrides), FeedHold() -> '!', CycleStart() -> '~', JogCancel() -> 0x85, and a generic SendControl(byte) used to send feed/rapid/spindle override bytes (e.g. 0x90-0x9A). These take effect regardless of streaming state. Note it uses the classic-GRBL ASCII real-time chars ('?','!','~') rather than grblHAL binary codes, so it targets GRBL 1.1 semantics.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Incremental relative jog only (GRBL 1.1 '$J=' jog protocol). Keyboard jog (MainWindow.xaml.ManualTab.cs) sends a single '$J=G91F{feed}{axis}{distance}' command on a non-repeat KeyDown, using JogFeed/JogDistance from settings, with Ctrl selecting an alternate JogFeedCtrl/JogDistanceCtrl (fast/slow) pair and Shift remapping the arrow keys to the Z axis. It behaves 'continuous-like' only if the configured JogDistance is large, because release cancels the move (see jog_cancel_mechanism) — there is no timed re-send loop that streams jog moves while a key is held, and there is no absolute-target jog. There is no on-screen mouse/touch joystick control (contrast with ArctZ's VirtualJoystick).
- **jog_cancel_mechanism** _Реализация отмены jog (0x85) и защита от "убегания" станка при потере keyup-события_:
  > Uses the GRBL real-time jog-cancel byte 0x85 (Machine.JogCancel() -> ToSendPriority). Runaway protection is unusually robust for keyboard jog: (a) key auto-repeat is ignored ('if (e.IsRepeat) return;') so each physical press sends exactly one jog; (b) a new jog is only issued when the machine is genuinely idle ('if (machine.BufferState > 0 || machine.Status != "Idle") return;'), preventing stacked/queued jog moves; (c) JogCancel is fired on KeyUp, and critically also on the Jogging control's LostKeyboardFocus event, which covers the 'lost key-up because the window lost focus' failure mode; (d) unchecking the jog-enable checkbox and pressing Escape also cancel (Escape optionally does a full soft reset). This single-shot-plus-cancel-on-release pattern with focus-loss handling is a good reference for ArctZ, but it assumes low transport latency.
- **mode_mutual_exclusion** _Блокировка jog во время файлового стриминга и наоборот (GRBL lockout error)_:
  > Enforced via the OperatingMode enum { Manual, SendFile, Probe, Disconnected, SendMacro }. SendLine() (manual/jog) is accepted only in Manual or Probe mode; FileStart(), ProbeStart(), SendMacroLines() each require Manual mode; keyboard jog additionally checks machine.Mode == Manual and Status == 'Idle'. Starting a file switches Mode to SendFile (jog/manual then blocked); finishing, pausing, hitting an error/ALARM, or a soft reset returns Mode to Manual and clears pending queues. So file streaming and interactive jog are hard-mutually-exclusive by design rather than merely coordinated — simpler than GRBL's own error:9/error:33 lockout reliance.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses standard GRBL 1.1 '<...>' status reports in ParseStatus() using a regex (StatusEx) that splits fields on '|'/'<'/'>'. Handles: the leading machine-state word; 'MPos'/'WPos' position (with WPos reconciled to MPos via WorkOffset); 'WCO' work-coordinate offset; 'Ov' feed/rapid/spindle override percentages; 'Bf' planner/RX buffer (used only when SyncBuffer is set); 'Pn' input-pin flags (X/Y/Z limit + P probe); 'F' feed and 'FS' feed+spindle real-time values. An 'IgnoreAdditionalAxes' setting truncates >3-axis position/offset vectors to XYZ. A separate UpdateStatus() parses modal replies ($G, [TLO:], G17/18/19, G20/21, G90/91, G43.1/G49 tool-length-offset).
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Two layers. (1) Application OperatingMode enum (Manual/SendFile/Probe/Disconnected/SendMacro) drives what the sender is allowed to do. (2) The controller's GRBL active state is kept only as a raw string property 'Status' (e.g. 'Idle','Run','Hold','Alarm','Jog','Door','Home') taken verbatim from the status report's first field — it is NOT modelled as a typed enum, and code compares it via string literals (e.g. Status == "Idle", cases "Run"/"Hold"). Events (StatusChanged, OperatingModeChanged, etc.) notify the UI of transitions.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Event-driven WPF code-behind, NOT MVVM. The 'Machine' class is an observable model exposing ~20 C# events (StatusChanged, PositionUpdateReceived, OperatingModeChanged, ProbeFinished, BufferStateChanged, etc.) and read-only properties; the UI is a set of MainWindow partial classes (MainWindow.xaml.ManualTab.cs, .ProbingTab, .MachineStatus, .FileTab, ...) whose event handlers push/pull data to and from the Machine. There are no ViewModels, no INotifyPropertyChanged data-bound VM layer, and no CommunityToolkit.Mvvm — so it is a weaker paradigm match for ArctZ than ioSender's true MVVM. The transferable idea is the observable-model-raising-events core, which ArctZ can wrap in an [ObservableProperty] ViewModel instead.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Good separation of concerns despite the lack of MVVM: essentially all protocol/transport/streaming/parsing logic lives in the single self-contained Machine.cs (no XAML dependency), and the UI subscribes only to its public events/properties. This means the communication core could in principle be lifted out and reused with a different UI. Two caveats for ArctZ: (1) Machine.cs marshals every event to the UI thread via 'Application.Current.Dispatcher.BeginInvoke' — a hard dependency on the WPF dispatcher baked into the core, which would need replacing with a platform-neutral synchronization context in Avalonia; (2) it reads configuration directly from the global 'Properties.Settings.Default' singleton rather than via injected config. So the core is separable but coupled to WPF's dispatcher and a global settings store.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Handled. Lines starting with 'ALARM' trigger ReportError (which runs GrblCodeTranslator.ExpandError to produce human-readable text), force Mode back to Manual, and clear the ToSend/ToSendMacro queues. 'error:NN' replies are matched to the offending in-flight line, reported with expanded text, and abort file streaming to Manual. A GrblCodeTranslator utility maps GRBL error/alarm numbers to descriptions.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > 3D toolpath visualization using HelixToolkit.Wpf (WPF 3D viewport), with live tool-position tracking and camera controls (e.g. a 'lay flat' camera button added in v1.5.13). The same viewport renders the probed height map as a surface, which is the project's signature visual.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — a dedicated GCode parser (OpenCNCPilot/GCode/GCodeParser.cs, GCodeCommands/Arc.cs, Motion.cs, Line.cs) tracks modal state (units G20/G21, plane G17/18/19, distance mode G90/G91, motion mode) and expands arcs G2/G3 into segments for the 3D preview and for applying the height map (long moves are also subdivided so Z can be warped along them).

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Windows-only. Single WPF / .NET Framework desktop codebase; the README explicitly notes it will not run under Linux because Mono does not support WPF. No macOS, Linux, mobile, or web build. This is the opposite portability strategy to ArctZ's Avalonia shared-core + thin platform heads (Desktop/Android/iOS/Browser). OpenCNCPilot is therefore a useful reference for GRBL character-counting streaming, keyboard jog safety, and especially the height-map/autolevel workflow, but not for cross-platform packaging or non-Windows transports (its serial/TCP stack and HelixToolkit.Wpf viewer are Windows-bound).

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > GRBL '$$' settings model. It reads controller settings and, as of v1.5.12, supports import/export of GRBL settings to/from file (a GrblSettingsWindow). GrblCodeTranslator provides human-readable names/descriptions for '$' settings. On connect it queries modal/coordinate state with '$G' and '$#'. No FluidNC YAML-config upload workflow and no grblHAL setting-enumeration model — it is oriented at classic GRBL 1.1's flat '$$' list.
- **max_concurrent_connections** _Ограничение одновременных соединений (актуально для WebSocket/ESP32 WebUI)_:
  > One controller connection per application instance (a single 'Machine' with a single 'Connection' stream and one worker thread). No multi-controller or shared-connection model.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Extensive and the project's flagship feature — the primary reason it is a reference for ArctZ-adjacent probing work. Dedicated Probe operating mode plus MainWindow.xaml.ProbingTab.cs and GCode/HeightMap.cs implement a full surface HEIGHT-MAP / AUTOLEVELLING workflow: the user defines a rectangular region and grid spacing ('Create New' with dimensions), the app probes each grid point with GRBL probing (G38-style) moves, parses the '[PRB:x,y,z:success]' reports (ParseProbe regex, converting machine->work coords and applying configurable ProbeOffsetX/Y for an offset probe tool), stores results in a 2D double array, and reconstructs any point via bilinear interpolation between the four nearest samples. The height map is then applied to a loaded G-code program by subdividing long moves and offsetting Z per-point, so an engraving/isolation-milling toolpath follows a warped surface (ideal for PCB isolation milling with V-cutters). The probed surface and warped toolpath are shown in the 3D viewport. Height maps can be saved/loaded, duplicated (with a warning), and margin-adjusted automatically.

### Прочая информация
- **type**: sender
- **vendor**: martin2250 (Martin Pittermann) — individual open-source author; project focused on PCB autolevelling / height-map probing

### Неопределённые поля (uncertain)
- bt_profile_type (no native BT; SPP-via-Windows-virtual-COM is an inferred workaround, not documented/tested)
- esp32_variant_support (N/A — inferred implication only; OpenCNCPilot has no BT code)
- bt_reconnect_behavior (N/A — no BT transport)
- bt_pairing_model (N/A at app level; OS-level SPP pairing assumption unverified)
- rx_buffer_handling (exact default ControllerBufferSize value not confirmed from source; GRBL standard 128 assumed)
- jog_latency_budget (inference; no explicit latency-budget/throttle constant in source)
- status_report_polling_model (default StatusPollInterval value in ms not confirmed; confirmed to be an active, user-configurable poll)
- coordinate_system_handling (no explicit G54-G59 WCS-index tracking found; works via reported WorkOffset)
- reconnect_logic (exact behaviour on an unexpected mid-stream drop vs clean close not fully traced)
- supported_dialects (real-world FluidNC/grblHAL compatibility via the GRBL path plausible but not verified/tested by the project)

---

## 15. Universal Gcode Sender (UGS) <a id="universal-gcode-sender-ugs"></a>

### Basic Info
- **language_platform** _Язык и платформа/фреймворк реализации_:
  > Java 17, built with Maven. Two editions sharing one headless core (ugs-core): UGS Classic is a single-window Java Swing application; UGS Platform is built on the NetBeans Platform (modular OSGi-like plugin/module system with docking window management). A newer JavaFX front-end (ugs-fx) and a command-line front-end (ugs-cli) also reuse the same core. The core is UI-agnostic and exposes a BackendAPI.
- **license** _Лицензия (важно при потенциальном заимствовании кода/идей)_:
  > GPL-3.0 (GNU General Public License v3.0). Copyleft — code/idea reuse into a proprietary or differently-licensed app is legally constrained; safe to study for design patterns, but not to copy source into ArctZ (MIT/closed).
- **repository** _Ссылка на исходный код_:
  > https://github.com/winder/Universal-G-Code-Sender (author Will Winder, org 'winder'). Website: https://winder.github.io/ugs_website/
- **maintenance_activity** _Активность поддержки (дата последнего релиза/коммита)_:
  > Very active and mature. Latest release v2.1.24 published 2026-07-06; steady cadence (v2.1.21 2026-03-08, v2.1.22 2026-03-17). One of the oldest and most widely used open-source GRBL senders, continuously maintained since ~2012.

### Transport Layer
- **supported_transports** _serial/USB, WiFi-Telnet, WebSocket, Bluetooth — какие поддерживаются_:
  > Serial/USB (default, via JSerialComm library), raw TCP, and WebSocket. Enumerated in the ConnectionDriver enum: JSERIALCOMM ('jserialcomm://'), TCP ('tcp://'), WS ('ws://'). Network controllers such as FluidNC over WiFi are reached via TCP or WebSocket. There is no first-class WiFi-Telnet driver and no native Bluetooth stack — Bluetooth is only usable indirectly through an OS-provided virtual serial (SPP) COM port picked up by the serial driver.
- **transport_abstraction_maturity** _Есть ли единый интерфейс транспорта, отделённый от протокола команд — прямой ориентир для архитектуры ArctZ_:
  > Very mature, cleanly layered three-tier abstraction — the strongest direct reference for ArctZ. (1) Transport layer: a Connection interface (connection/Connection.java) with concrete JSerialCommConnection, TCPConnection, WSConnection, selected via a ConnectionDriver enum and instantiated by a ConnectionFactory. (2) Communicator layer: ICommunicator / AbstractCommunicator / BufferedCommunicator (firmware-specific GrblCommunicator, TinyGCommunicator, SmoothieCommunicator) owns streaming/buffering and holds a Connection. (3) Controller layer: IController / AbstractController (GrblController, FluidNCController, TinyGController, etc.) holds a Communicator and implements firmware semantics. Transport is fully decoupled from both the streaming protocol and the command dialect — swapping serial for TCP/WebSocket changes only the Connection implementation, everything above is unchanged.

### Bluetooth Specifics
- **bt_profile_type** _BLE (GATT/NUS) vs Bluetooth Classic SPP (RFCOMM)_:
  > No dedicated Bluetooth code path. UGS treats Bluetooth exclusively as Bluetooth Classic SPP (RFCOMM) exposed by the operating system as a virtual serial COM port, then opened through the ordinary JSerialComm serial driver. BLE (GATT / Nordic UART Service) is NOT supported. Consequence for ArctZ: UGS offers no reusable BLE design — ArctZ's BLE-to-FluidNC path must be designed independently.
- **bt_pairing_model** _Механизм сопряжения (OS-level pairing/bonding vs программный коннект)_:
  > OS-level pairing/bonding. The user pairs the device in the operating system's Bluetooth settings, which creates a virtual SPP serial port; UGS then simply selects that COM port from its serial port list. There is no in-app pairing, scanning, or bonding UI.

### Streaming Protocol
- **streaming_strategy** _Character-counting vs buffered vs simple send-response ожидание "ok"_:
  > Character-counting buffered streaming (send-ahead), implemented in BufferedCommunicator. It keeps two java.util.concurrent.LinkedBlockingDeque queues: commandBuffer (queued, not yet sent) and activeCommandList (sent, awaiting 'ok'/error). streamCommands() sends as many commands as fit while CommUtils.checkRoomInBuffer(sentBufferSize, nextCommandString, getBufferSize()) is true and not paused, incrementing sentBufferSize by commandString.length()+1 per line. On each response, the head of activeCommandList is matched, marked done/error and popped, freeing buffer space and triggering another streamCommands() pass. This is the classic GRBL character-counting protocol (not naive line-by-line wait-for-ok, and not blind buffered send).
- **rx_buffer_handling** _Учёт размера RX-буфера контроллера (GRBL ~128 байт, FluidNC — иначе)_:
  > Explicitly modeled. GrblCommunicator.getBufferSize() returns GrblUtils.GRBL_RX_BUFFER_SIZE = 128 bytes; sentBufferSize accounts for each line plus the trailing newline (+1). The abstract getBufferSize() is overridden per firmware, so FluidNC/TinyG/Smoothie communicators report their own RX buffer sizes. checkRoomInBuffer guarantees the controller's serial RX buffer never overflows.

### Jogging vs File Streaming
- **command_queue_model** _Единая очередь vs раздельные очереди (модель feeder/sender из CNCjs)_:
  > Two-stage in-flight model rather than the fully separate feeder/sender pair of CNCjs, but conceptually similar: BufferedCommunicator holds commandBuffer (pending) + activeCommandList (in-flight awaiting ok). Regular G-code and jog G-code both flow through this same queue as normal buffered commands. A distinct path exists for real-time single-byte commands, which bypass both queues entirely (see realtime_bypass_path). Jog motion and file streaming therefore share one command queue but are separated in practice by mutual-exclusion checks in the controller/JogService.
- **realtime_bypass_path** _Как real-time байты (?, !, ~, 0x18, 0x85, overrides) инжектируются в поток минуя буфер отправки_:
  > ICommunicator.sendByteImmediately(byte b) writes a single byte straight to the connection (connection.sendByteImmediately(b)), completely bypassing commandBuffer and activeCommandList and the character-count accounting. Used by GrblController for all GRBL 1.1 real-time bytes: status '?' (0x3F), feed-hold '!' , cyclestart/resume '~', soft-reset 0x18, safety-door 0x84, jog-cancel 0x85, and the feed/spindle/rapid override bytes (0x90+ from CMD_FEED_OVR_* etc. in GrblUtils). These are dispatched immediately regardless of buffer state, exactly the mechanism ArctZ needs for responsive joystick/e-stop control.
- **jog_mode_types** _Непрерывный (hold-to-move) vs пошаговый (incremental) vs абсолютный jog_:
  > Both incremental/step jog and continuous (hold-to-move) jog. Step jog: JogService.adjustManualLocation / adjustManualLocationXY/Z/ABC send a discrete '$J=' jog G-code of a fixed step size. Continuous jog: utils/ContinuousJogWorker sends a stream of small '$J=' jog commands at a fixed interval while a key/button is held, sized to reach the configured jog feed rate, and stops (jog-cancel) on release. Absolute vs relative jog is handled by GRBL 1.1 '$J=' distance-mode words.
- **jog_latency_budget** _Транспортная задержка jog-команд и минимальный интервал отправки при удержании (особенно по BLE/WiFi)_:
  > Explicitly reasoned about in ContinuousJogWorker: it assumes the round-trip latency from sending a '$J=' jog command to receiving 'ok' is under ~10ms (typically 1-7ms over USB serial) and that the machine can accelerate to full jog feed within one command, then sizes each incremental jog move to a fixed JOG_COMMAND_INTERVAL so held-jog stays smooth without over-filling the planner buffer. This budget is calibrated for low-latency USB serial; higher-latency BLE/WiFi links (ArctZ's case) would need a larger interval / bigger per-command distance to avoid stutter or buffer starvation.

### Status & State Machine
- **status_report_format** _Формат status report и его парсинг (MPos/WPos, feed, spindle)_:
  > Parses GRBL status reports of the form '<...>' (STATUS_PATTERN = /<.*>/), supporting both legacy 0.8 and 1.1 formats (isGrblStatusStringV1 checks '^<state|...>$'). Extracts machine state, MPos/WPos, and additional fields (feed, spindle, overrides, buffer, etc.) via GrblUtils regexes into a ControllerStatus with position, feed and other telemetry.
- **state_machine** _Модель состояний Idle/Run/Hold/Alarm/Jog/Door_:
  > Machine state parsed from the status report state token (STATUS_STATE regex) into a ControllerState enum covering Idle, Run, Hold, Jog, Alarm, Door, Home, Check and Disconnected, plus a separate CommunicatorState for the transport/comm lifecycle. UI and services react to state transitions via UGSEvent listeners.
- **status_report_polling_model** _Активный опрос "?" с интервалом vs авто-push статусов (влияет на нагрузку BLE-канала)_:
  > Active host-driven polling: StatusPollTimer periodically sends the '?' real-time byte at a configurable rate (GrblController.getStatusUpdateRate(), default on the order of ~200ms / 5Hz, user-adjustable). It is not push-based auto-reporting. Relevant to ArctZ: on a bandwidth-limited BLE channel this fixed-interval '?' polling competes with jog traffic, so the poll rate would need tuning.
- **coordinate_system_handling** _WCS G54-G59, работа с machine vs work coordinates_:
  > Supports work coordinate systems G54-G59 and distinguishes machine position (MPos) from work position (WPos) using the work-coordinate offset reported by GRBL; provides reset-to-zero (G10 L20 for GRBL 1.1, G92 fallback for 0.8) and per-axis zeroing.

### UI Architecture
- **ui_pattern** _MVVM/MVC/другое, соответствие паттерну ArctZ (CommunityToolkit.Mvvm)_:
  > Not MVVM. Event-driven / observer architecture: the headless ugs-core exposes a BackendAPI, and UIs subscribe to UGSEvent notifications (observer pattern). UGS Classic is plain Swing (roughly MVC with panels observing the backend); UGS Platform composes NetBeans Platform modules/TopComponents. Contrast with ArctZ's CommunityToolkit.Mvvm — UGS achieves the same UI/logic decoupling through the BackendAPI + event bus rather than through data-binding view-models.
- **comm_ui_separation** _Насколько слой связи отделён от UI (переиспользуемость ядра между платформами)_:
  > Excellent — the clearest strength to emulate. All communication, streaming, parsing and state logic live in ugs-core behind BackendAPI, with zero UI dependency; this is proven by the existence of multiple independent front-ends over the same core (Classic Swing, Platform/NetBeans, ugs-fx JavaFX, and a headless ugs-cli). The core can be driven programmatically or from a terminal with no GUI, exactly the cross-platform-core / thin-head split ArctZ uses with its shared ArctZ project and per-platform heads.

### Error Handling & Recovery
- **alarm_codes** _Обработка alarm-состояний контроллера_:
  > Alarm states and codes are handled: GRBL alarm/error responses are parsed and surfaced (GrblUtils error/alarm handling), and FluidNCController fetches enumerated alarm and error code tables from the controller (GetAlarmCodesCommand, GetErrorCodesCommand) to present human-readable messages.
- **reconnect_logic** _Логика восстановления после разрыва связи (включая специфику BT — см. bt_reconnect_behavior)_:
  > A ConnectionWatchTimer monitors for a dropped link and flips the controller to disconnected; reconnection is generally user-initiated (reopen the port/socket). There is no automatic mid-job resume of a partially streamed program after a drop — a job interrupted by disconnect must be restarted (or manually resumed by hand-editing the queue). Adequate for USB serial; ArctZ's BLE case likely needs stronger auto-reconnect than UGS provides.

### Visualization
- **preview_rendering** _2D/3D preview траектории_:
  > Yes — a 3D toolpath visualizer (OpenGL via JOGL) renders the loaded G-code and live tool position; the Platform edition adds a richer visualizer and a vector/G-code designer. Not a priority for ArctZ but confirms full-featured preview.
- **arc_modal_parsing** _Парсинг дуг и модальных состояний для превью_:
  > Yes — the GcodeParser tracks modal state (motion mode, plane, units, distance mode, active WCS) and an ArcExpander converts G2/G3 arcs (and optional arc-to-line segmentation) for both the visualizer and for controllers/firmwares that need expanded arcs. Also supports processors like line-splitting and mesh/auto-level Z adjustment.

### Cross-Platform Support
- **platform_support** _Desktop/mobile/web поддержка общим кодом — прямое сравнение с Avalonia-подходом ArctZ_:
  > Desktop-only: Windows (x64), macOS (x64 and ARM64), Linux (x64, ARM, ARM64), all via the JVM (Java 17). No mobile (Android/iOS) and no browser/web build. This is the key divergence from ArctZ: UGS validates the shared-headless-core + multiple-front-end pattern, but ArctZ's Avalonia stack additionally targets Android/iOS/WASM, which UGS does not, so UGS provides no reference for mobile/web transport (BLE/Web Bluetooth) or touch UI.

### Configuration & Settings
- **config_access_model** _$$/$Report (GRBL) vs YAML upload (FluidNC) vs $ settings (grblHAL)_:
  > Firmware-settings abstraction (IFirmwareSettings) per controller. GRBL: reads/writes '$$'/'$x=val' numbered settings. FluidNC: full YAML config workflow — list/download/upload/delete config files on the controller flash (FluidNCFileService with ListFilesCommand, DownloadFileCommand, UploadFileCommand, DeleteFileCommand, Get/SetCurrentConfigFilename), plus '$' runtime settings. grblHAL is handled through the GRBL settings path.

### Firmware Dialect Support
- **supported_dialects** _GRBL 1.1 / FluidNC / grblHAL / другие, и как определяется диалект (welcome string, $I)_:
  > GRBL (0.8 and 1.1), grblHAL, FluidNC, TinyG, g2core, and Smoothieware. Dialect is auto-detected from the welcome/greeting string (GrblUtils recognizes prefixes 'Grbl ', 'GrblHAL ', 'CarbideMotion ', 'gCarvin') and confirmed via build/version info ('$I' / build-info). FluidNC and grblHAL are recognized as GRBL-family and routed to their controllers (dedicated FluidNCController; grblHAL via GrblController with capability flags). A ControllerFactory instantiates the matching IController.

### Probing Workflow
- **probing_support** _G38.x, height-map/autoleveling_:
  > Yes — G38.x straight-probe is supported, and the Platform edition includes surface-scanning / auto-leveling (height-map) via G-code processors that offset Z from a probed mesh, plus corner/edge-finding probe workflows.

### Прочая информация
- **type**: sender
- **research_date**: 2026-07-24

### Неопределённые поля (uncertain)
- esp32_variant_support
- radio_coexistence
- firmware_build_variant
- bt_mtu_packet_size
- bt_throughput
- bt_reconnect_behavior
- os_bt_api
- jog_cancel_mechanism
- mode_mutual_exclusion
- max_concurrent_connections
