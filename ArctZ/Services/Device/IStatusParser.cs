namespace ArctZ.Services.Device;

public interface IStatusParser
{
    FluidNcLine Parse(string rawLine);
}
