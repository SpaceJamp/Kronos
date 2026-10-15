namespace Kronos;

/// <summary>
/// Which GPU API the UI is drawn with. Software rendering on the CPU is the fallback for both.
/// </summary>
/// <remarks>
/// Stored as a number in settings.json, so existing values must keep their numbers.
/// </remarks>
public enum RendererPreference : int
{
    Vulkan = 0,
    OpenGL = 1,
}
