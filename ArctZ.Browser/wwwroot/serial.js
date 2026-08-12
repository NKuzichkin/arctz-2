let port = null;
let writer = null;
let readLoopAbort = false;

function csharpExports() {
    return globalThis.__arctzSerialExports.ArctZ.Browser.SerialInterop;
}

export function isSupported() {
    return "serial" in navigator;
}

async function openAndStartReading(selectedPort) {
    await selectedPort.open({ baudRate: 115200 });
    port = selectedPort;
    writer = port.writable.getWriter();
    readLoopAbort = false;
    startReadLoop();
}

export async function requestPort() {
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
    if (!writer) {
        return;
    }

    await writer.write(new Uint8Array(bytes));
}

export async function closePort() {
    readLoopAbort = true;

    if (writer) {
        try { await writer.close(); } catch { }
        writer = null;
    }

    if (port) {
        try { await port.close(); } catch { }
        port = null;
    }
}

async function startReadLoop() {
    const activePort = port;
    const decoder = new TextDecoderStream();
    activePort.readable.pipeTo(decoder.writable).catch(() => { });
    const reader = decoder.readable.getReader();
    let buffer = "";

    try {
        while (!readLoopAbort) {
            const { value, done } = await reader.read();
            if (done) {
                break;
            }

            buffer += value;
            let newlineIndex;
            while ((newlineIndex = buffer.indexOf("\n")) >= 0) {
                const line = buffer.slice(0, newlineIndex).replace(/\r$/, "");
                buffer = buffer.slice(newlineIndex + 1);
                csharpExports().OnLineReceived(line);
            }
        }
    } catch {
        // Falls through to the disconnect notification below — a read error
        // (cable/BT drop) and a clean stream close are handled the same way.
    }

    if (!readLoopAbort) {
        csharpExports().OnDisconnected();
    }
}
