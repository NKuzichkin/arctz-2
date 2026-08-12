using System.Collections.Generic;
using System.Text;

namespace ArctZ.Services.Device;

/// <summary>
/// Собирает завершённые строки из последовательных чтений байтового потока
/// (RFCOMM/serial). Разделитель — '\n', ведущий '\r' перед ним отбрасывается.
/// Не зависит от Android/платформы — так этот разбор можно покрыть тестами,
/// хотя единственный сейчас потребитель (AndroidBluetoothTransport) живёт
/// в net10.0-android и из ArctZ.Tests не виден.
/// </summary>
public sealed class LineAssembler
{
    private const int MaxLineLength = 4096;

    private readonly StringBuilder _buffer = new();
    private bool _droppingOverlongLine;

    public IReadOnlyList<string> Append(byte[] data, int count)
    {
        var lines = new List<string>();

        for (var i = 0; i < count; i++)
        {
            var b = data[i];
            if (b == (byte)'\n')
            {
                if (!_droppingOverlongLine)
                {
                    var line = _buffer.ToString().TrimEnd('\r');
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                    }
                }

                _buffer.Clear();
                _droppingOverlongLine = false;
                continue;
            }

            if (_droppingOverlongLine)
            {
                continue;
            }

            if (_buffer.Length >= MaxLineLength)
            {
                _droppingOverlongLine = true;
                _buffer.Clear();
                continue;
            }

            _buffer.Append((char)b);
        }

        return lines;
    }
}
