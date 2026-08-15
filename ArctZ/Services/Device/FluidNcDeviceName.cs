using System;

namespace ArctZ.Services.Device;

/// <summary>Матчинг имени устройства для автоподключения: устройства FluidNC отдают своё
/// имя с префиксом "FluidNC" (например "FluidNC-1234"). Регистр не учитывается.</summary>
public static class FluidNcDeviceName
{
    public static bool Matches(string? name) =>
        name is not null && name.StartsWith("FluidNC", StringComparison.OrdinalIgnoreCase);
}
