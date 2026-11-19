using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;

namespace CrystalExcavator.Core.Rendering;

/// <summary>APIs gráficas que o computador realmente oferece (Vulkan com GPU, Direct3D 12).</summary>
public sealed class GpuCaps
{
    public bool Checked { get; set; }
    public bool Vulkan { get; set; }
    public int VulkanDevices { get; set; }
    public string VulkanDevice { get; set; } = "";
    public bool D3D12 { get; set; }
    public string Note { get; set; } = "";
    /// <summary>Forward+/Mobile só podem iniciar com Vulkan ou Direct3D 12.</summary>
    public bool RenderingDevice => Vulkan || D3D12;
}

/// <summary>
/// Verificação das APIs gráficas SEM abrir janela: roda num processo separado sem tela
/// (<c>--headless -- --gpu-check=arquivo</c>). Assim, um driver com defeito nunca derruba o jogo e
/// a Godot nunca mostra a caixa de erro "Unable to initialize video driver" ao testar Forward+/Mobile
/// numa máquina sem Vulkan nem Direct3D 12 (ex.: ThinkPad T440).
/// </summary>
public static class GpuCapabilities
{
    public static GpuCaps Detect()
    {
        var caps = new GpuCaps { Checked = true };
        try { DetectVulkan(caps); } catch (Exception e) { caps.Note += "vulkan: " + e.Message + " "; }
        try { if (OperatingSystem.IsWindows()) caps.D3D12 = DetectD3D12(); } catch (Exception e) { caps.Note += "d3d12: " + e.Message; }
        return caps;
    }

    public static void RunAndWrite(string outPath)
    {
        var caps = Detect();
        try
        {
            using var f = FileAccess.Open(outPath, FileAccess.ModeFlags.Write);
            f?.StoreString(JsonSerializer.Serialize(caps));
        }
        catch { }
    }

    // ------------------------------------------------------------ Vulkan
    [StructLayout(LayoutKind.Sequential)]
    struct VkApplicationInfo
    {
        public int sType; public IntPtr pNext; public IntPtr pApplicationName; public uint applicationVersion;
        public IntPtr pEngineName; public uint engineVersion; public uint apiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VkInstanceCreateInfo
    {
        public int sType; public IntPtr pNext; public uint flags; public IntPtr pApplicationInfo;
        public uint enabledLayerCount; public IntPtr ppEnabledLayerNames; public uint enabledExtensionCount; public IntPtr ppEnabledExtensionNames;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int VkCreateInstance(ref VkInstanceCreateInfo info, IntPtr allocator, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int VkEnumeratePhysicalDevices(IntPtr instance, ref uint count, IntPtr[] devices);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void VkGetPhysicalDeviceProperties(IntPtr device, IntPtr props);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void VkDestroyInstance(IntPtr instance, IntPtr allocator);

    static void DetectVulkan(GpuCaps caps)
    {
        string lib = OperatingSystem.IsWindows() ? "vulkan-1.dll" : OperatingSystem.IsMacOS() ? "libvulkan.1.dylib" : "libvulkan.so.1";
        if (!NativeLibrary.TryLoad(lib, out var h)) { caps.Note += "sem carregador Vulkan; "; return; }
        T F<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(h, name));
        var create = F<VkCreateInstance>("vkCreateInstance");
        var enumerate = F<VkEnumeratePhysicalDevices>("vkEnumeratePhysicalDevices");
        var props = F<VkGetPhysicalDeviceProperties>("vkGetPhysicalDeviceProperties");
        var destroy = F<VkDestroyInstance>("vkDestroyInstance");
        var name = Marshal.StringToHGlobalAnsi("Crystal Excavator");
        var app = Marshal.AllocHGlobal(Marshal.SizeOf<VkApplicationInfo>());
        try
        {
            Marshal.StructureToPtr(new VkApplicationInfo { sType = 0, pApplicationName = name, apiVersion = 1u << 22 }, app, false);
            var info = new VkInstanceCreateInfo { sType = 1, pApplicationInfo = app };
            int res = create(ref info, IntPtr.Zero, out var inst);
            if (res != 0 || inst == IntPtr.Zero) { caps.Note += $"vkCreateInstance={res}; "; return; }
            try
            {
                uint count = 0;
                enumerate(inst, ref count, null);
                caps.VulkanDevices = (int)count;
                if (count > 0)
                {
                    var devs = new IntPtr[count];
                    enumerate(inst, ref count, devs);
                    // VkPhysicalDeviceProperties: apiVersion, driverVersion, vendorID, deviceID, deviceType, deviceName[256]...
                    var buf = Marshal.AllocHGlobal(1024);
                    try
                    {
                        foreach (var d in devs)
                        {
                            props(d, buf);
                            uint api = (uint)Marshal.ReadInt32(buf, 0);
                            int type = Marshal.ReadInt32(buf, 16);
                            string dn = Marshal.PtrToStringAnsi(buf + 20) ?? "";
                            // a Godot pede Vulkan 1.0+; GPU real (integrada, dedicada ou virtual). Tipo 4 = CPU (software)
                            bool cpuOk = OS.GetCmdlineUserArgs().Contains("--x-gpu-allow-cpu"); // só testes sem GPU
                            if ((api >> 22) >= 1 && (type != 4 || cpuOk)) { caps.Vulkan = true; caps.VulkanDevice = dn; }
                            else if (string.IsNullOrEmpty(caps.VulkanDevice)) caps.VulkanDevice = dn + (type == 4 ? " (CPU)" : "");
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { destroy(inst, IntPtr.Zero); }
        }
        finally { Marshal.FreeHGlobal(app); Marshal.FreeHGlobal(name); }
    }

    // ------------------------------------------------------------ Direct3D 12 (Windows)
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int D3D12CreateDevice(IntPtr adapter, int minFeatureLevel, ref Guid riid, IntPtr device);

    static bool DetectD3D12()
    {
        if (!NativeLibrary.TryLoad("d3d12.dll", out var h)) return false;
        var create = Marshal.GetDelegateForFunctionPointer<D3D12CreateDevice>(NativeLibrary.GetExport(h, "D3D12CreateDevice"));
        var iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7"); // ID3D12Device
        // sem ponteiro de saída: só pergunta se o adaptador padrão aceita o nível 11_0 (S_FALSE = sim)
        int hr = create(IntPtr.Zero, 0xb000, ref iid, IntPtr.Zero);
        return hr == 1 || hr == 0;
    }
}
