using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Kronos.Helpers;

/// <summary>What the UI is actually being drawn with.</summary>
public enum ActiveRenderer
{
    Unknown,
    Vulkan,
    OpenGL,
    Software,
}

/// <summary>
/// Reports which renderer Uno ended up using: the chosen GPU API, or software on the CPU.
/// </summary>
/// <remarks>
/// <para>
/// Uno tries the GPU API enabled in UnoProgram and falls back to software rendering, and nothing on screen
/// says which happened. Software rendering is a plausible cause of a sluggish UI, so it is logged and shown
/// on the Settings page.
/// </para>
/// <para>
/// Read from Uno itself: each Win32 window's wrapper holds the renderer it created, a VulkanRenderer,
/// GlRenderer or SoftwareRenderer. Those are internal, so this uses reflection, against the Uno version
/// pinned in global.json; if Uno renames them it reports Unknown rather than failing. Loaded DLLs cannot
/// answer this: opengl32.dll is loaded even when Vulkan is drawing, and vulkan-1.dll is loaded just to
/// probe for Vulkan.
/// </para>
/// </remarks>
internal static class RenderingDiagnostics
{
    internal static void LogRendererSoon()
    {
        // After the window exists, which is when Uno creates its renderer.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            try
            {
                Logger.Info($"Renderer: {Describe(GetActiveRenderer())} (preference: {Settings.Instance.RendererPreference})");
            }
            catch (Exception err)
            {
                Logger.Error(err, "Could not determine the renderer.");
            }
        }, TaskScheduler.Default);
    }

    internal static ActiveRenderer GetActiveRenderer()
    {
        try
        {
            var wrapperType = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Uno.UI.Runtime.Skia.Win32")
                ?.GetType("Uno.UI.Runtime.Skia.Win32.Win32WindowWrapper");
            var wrappers = wrapperType?.GetField("_hwndToWrapper", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IDictionary;
            var rendererField = wrapperType?.GetField("_renderer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (wrappers is null || rendererField is null)
            {
                return ActiveRenderer.Unknown;
            }

            foreach (var wrapper in wrappers.Values)
            {
                switch (rendererField.GetValue(wrapper)?.GetType().Name)
                {
                    case "VulkanRenderer": return ActiveRenderer.Vulkan;
                    case "GlRenderer": return ActiveRenderer.OpenGL;
                    case "SoftwareRenderer": return ActiveRenderer.Software;
                }
            }
        }
        catch (Exception err)
        {
            Logger.Warning($"Could not read Uno's renderer: {err.Message}");
        }

        return ActiveRenderer.Unknown;
    }

    internal static string Describe(ActiveRenderer renderer) => renderer switch
    {
        ActiveRenderer.Vulkan => "Vulkan (GPU)",
        ActiveRenderer.OpenGL => "OpenGL (GPU)",
        ActiveRenderer.Software => "CPU (software rendering)",
        _ => "unknown",
    };
}
