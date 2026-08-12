let port = null;
let writer = null;
let reader = null;
let pipeDone = null;

// Generation counter identifying the connection a read loop belongs to. Each loop
// captures the value it was started with and compares it against this counter, so a
// loop still parked in reader.read() when its connection goes away cannot emit into
// the next one. A single module-wide boolean cannot express that: the next connect
// would reset it and hand the stale loop a licence to keep running.
let readLoopId = 0;

function csharpExports() {
    return globalThis.__arctzSerialExports.ArctZ.Browser.SerialInterop;
}

export function isSupported() {
    return "serial" in navigator;
}

async function openAndStartReading(selectedPort) {
    // DeviceSession's reconnect loop calls ConnectAsync again without an intervening
    // DisconnectAsync (see DeviceSession.OnTransportDisconnected), so the port left
    // behind by the dropped link would still be in the "opened" state and getPorts()
    // hands back that very same SerialPort object - every reopen would then fail with
    // InvalidStateError. DesktopSerialTransport.ConnectAsync guards the same way.
    await closePort();

    await selectedPort.open({ baudRate: 115200 });
    port = selectedPort;
    writer = port.writable.getWriter();
    startReadLoop(port, ++readLoopId);
}

export async function requestPort() {
    // Note: cancelling the browser's port picker *rejects* this promise
    // (NotFoundError) rather than resolving with nothing, so the rejection
    // propagating out to C# - not the `true` returned here - is what actually keeps
    // BrowserSerialTransport from marking itself connected. Keep it that way.
    const selected = await navigator.serial.requestPort();
    await openAndStartReading(selected);
    return true;
}

export async function reopenSavedPort() {
    const ports = await navigator.serial.getPorts();
    if (ports.length === 0) {
        return false;
    }

    await openAndStartReading(ports[0]);
    return true;
}

export async function write(bytes) {
    const activeWriter = writer;
    if (!activeWriter) {
        return;
    }

    await activeWriter.write(new Uint8Array(bytes));
}

export async function closePort() {
    // Retire any in-flight read loop before tearing its streams down, so a loop that
    // wakes up mid-teardown stays silent instead of reporting a disconnect we already
    // know about.
    readLoopId++;

    const activeReader = reader;
    const activeWriter = writer;
    const activePort = port;
    const activePipe = pipeDone;
    reader = null;
    writer = null;
    port = null;
    pipeDone = null;

    if (!activePort) {
        return;
    }

    // Cancelling settles the pending read() and, propagating back through pipeTo,
    // releases the lock Web Serial put on port.readable. Without it port.close()
    // rejects with InvalidStateError because readable is still locked.
    if (activeReader) {
        try { await activeReader.cancel(); } catch (e) { console.warn("serial: reader.cancel failed", e); }
    }

    if (activePipe) {
        try { await activePipe; } catch (e) { console.warn("serial: read pipe rejected during close", e); }
    }

    // releaseLock() rather than close(): a queued write to a device that has
    // physically vanished can leave writer.close() unsettled forever, which would hang
    // DisconnectAsync with no timeout to rescue it.
    if (activeWriter) {
        try { activeWriter.releaseLock(); } catch (e) { console.warn("serial: writer.releaseLock failed", e); }
    }

    try { await activePort.close(); } catch (e) { console.warn("serial: port.close failed", e); }
}

// Started fire-and-forget from openAndStartReading, so every statement - stream setup
// included - has to stay inside try/catch: an escaping rejection would surface only as
// unhandledrejection, leaving C# believing it is still connected to a dead link.
async function startReadLoop(activePort, loopId) {
    try {
        const decoder = new TextDecoderStream();
        pipeDone = activePort.readable.pipeTo(decoder.writable).catch(e => {
            console.warn("serial: read pipe failed", e);
        });
        reader = decoder.readable.getReader();

        let buffer = "";
        while (loopId === readLoopId) {
            const { value, done } = await reader.read();
            if (done || loopId !== readLoopId) {
                break;
            }

            buffer += value;
            let newlineIndex;
            while ((newlineIndex = buffer.indexOf("\n")) >= 0 && loopId === readLoopId) {
                const line = buffer.slice(0, newlineIndex).replace(/\r$/, "");
                buffer = buffer.slice(newlineIndex + 1);
                csharpExports().OnLineReceived(line);
            }
        }
    } catch (e) {
        // A read error (cable/BT drop) and a clean stream close both fall through to
        // the single disconnect notification below.
        console.warn("serial: read loop failed", e);
    }

    // Only the loop that still owns the connection reports the drop, and only when it
    // was not retired deliberately by closePort() - which the C# side already knows
    // about, having asked for it.
    if (loopId === readLoopId) {
        try { csharpExports().OnDisconnected(); } catch (e) { console.warn("serial: OnDisconnected failed", e); }
    }
}
