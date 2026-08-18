using System.Globalization;
using ArctZ.Services.Device;

namespace ArctZ.Services.Program;

/// <summary>
/// Единственное место, где формируется строка перемещения. Все движения идут
/// в режиме inverse time (G93): F = 1 / t, где t — время блока в минутах.
/// G93 повторяется в каждой строке намеренно — иначе любой путь останова
/// (Stop, StopAndDrain, ошибка, обрыв связи) был бы обязан вернуть G94, и
/// один пропущенный возврат оставил бы машину в режиме, где команда без F
/// даёт error:2.
/// </summary>
public static class InverseTimeMove
{
    /// <summary>Время перехода по умолчанию: подставляется вместо непозитивного значения.</summary>
    public const double DefaultTransitionSeconds = 5.0;

    /// <summary>
    /// Непозитивное время нельзя клампить к «почти нулю»: F стремился бы к
    /// бесконечности, и переход превратился бы в бросок на максимальной
    /// скорости оси. Это худший ответ и на пустое поле ввода, и на старый файл
    /// программы, где TransitionSeconds десериализуется в 0.
    /// </summary>
    public static double EffectiveSeconds(double seconds) =>
        seconds > 0 ? seconds : DefaultTransitionSeconds;

    public static string Line(MachinePose pose, double seconds) =>
        $"G93 G1 X{Axis(pose.X)} Y{Axis(pose.Y)} Z{Axis(pose.Z)} A{Axis(pose.A)} F{Feed(seconds)}";

    private static string Feed(double seconds) =>
        (60.0 / EffectiveSeconds(seconds)).ToString("0.#######", CultureInfo.InvariantCulture);

    private static string Axis(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
