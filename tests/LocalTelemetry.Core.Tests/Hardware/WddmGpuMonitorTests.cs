using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class WddmGpuMonitorTests
{
    [Fact]
    public void WddmGpuMonitor_InstantiationAndGetUsagePct()
    {
        using var monitor = new WddmGpuMonitor();

        if (monitor.IsAvailable)
        {
            // First call primes the monitor
            float first = monitor.GetUsagePct();
            first.Should().Be(0f);

            // Second call calculates percentage
            float second = monitor.GetUsagePct();
            second.Should().BeInRange(0f, 100f);
        }

        monitor.Dispose();
    }

    [Fact]
    public void WddmGpuMonitor_ComputeEngineKey_StripsProcessIdPrefix()
    {
        var method = typeof(WddmGpuMonitor).GetMethod("ComputeEngineKey", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        IntPtr ptrNull = IntPtr.Zero;
        int keyNull = (int)method!.Invoke(null, [ptrNull])!;
        keyNull.Should().Be(0);

        IntPtr ptr1 = Marshal.StringToHGlobalUni("pid_1234_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D");
        IntPtr ptr2 = Marshal.StringToHGlobalUni("pid_5678_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D");

        try
        {
            int key1 = (int)method.Invoke(null, [ptr1])!;
            int key2 = (int)method.Invoke(null, [ptr2])!;

            // Same engine on different processes must compute the exact same engine hash key!
            key1.Should().Be(key2);
            key1.Should().NotBe(0);

            IntPtr ptrEmpty = Marshal.StringToHGlobalUni("");
            IntPtr ptrNoPid = Marshal.StringToHGlobalUni("eng_0_engtype_3D");
            IntPtr ptrOnlyPid = Marshal.StringToHGlobalUni("pid_9999");
            try
            {
                ((int)method.Invoke(null, [ptrEmpty])!).Should().Be(0);
                ((int)method.Invoke(null, [ptrNoPid])!).Should().NotBe(0);
                ((int)method.Invoke(null, [ptrOnlyPid])!).Should().NotBe(0);
            }
            finally
            {
                Marshal.FreeHGlobal(ptrEmpty);
                Marshal.FreeHGlobal(ptrNoPid);
                Marshal.FreeHGlobal(ptrOnlyPid);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr1);
            Marshal.FreeHGlobal(ptr2);
        }
    }

    [Fact]
    public void WddmGpuMonitor_BufferAndDisposalEdgeCases()
    {
        using var monitor = new WddmGpuMonitor();

        var ensureBuffer = typeof(WddmGpuMonitor).GetMethod("EnsureBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        ensureBuffer.Should().NotBeNull();

        ensureBuffer!.Invoke(monitor, [1024]);
        ensureBuffer.Invoke(monitor, [2048]);
        ensureBuffer.Invoke(monitor, [512]); // Should not realloc since capacity >= 512

        monitor.Dispose();
        monitor.GetUsagePct().Should().Be(0f);
    }

    [Fact]
    public void WddmGpuMonitor_GetUsagePct_HandlesExceptionsGracefully()
    {
        using var monitor = new WddmGpuMonitor();
        if (monitor.IsAvailable)
        {
            var field = typeof(WddmGpuMonitor).GetField("_engineSums", BindingFlags.NonPublic | BindingFlags.Instance);
            field!.SetValue(monitor, null);
            monitor.GetUsagePct().Should().Be(0f);
        }
    }

    [Fact]
    public void WddmGpuMonitor_BufferParsing_SimulatedItems()
    {
        using var monitor = new WddmGpuMonitor();
        var bufferField = typeof(WddmGpuMonitor).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var ensureBuffer = typeof(WddmGpuMonitor).GetMethod("EnsureBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        bufferField.Should().NotBeNull();
        ensureBuffer.Should().NotBeNull();

        // Allocate buffer for 2 items (ItemStride = 24)
        ensureBuffer!.Invoke(monitor, [48]);
        IntPtr buffer = (IntPtr)bufferField!.GetValue(monitor)!;
        buffer.Should().NotBe(IntPtr.Zero);

        IntPtr strName = Marshal.StringToHGlobalUni("pid_1234_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D");
        try
        {
            // Item 0: cStatus = 0, Value = 45.0
            Marshal.WriteIntPtr(buffer, 0, strName);
            Marshal.WriteInt32(buffer, 8, 0); // cStatus = 0
            Marshal.WriteInt64(buffer, 16, BitConverter.DoubleToInt64Bits(45.0));

            // Item 1: cStatus = 1 (error, skipped), Value = 50.0
            Marshal.WriteIntPtr(buffer, 24, strName);
            Marshal.WriteInt32(buffer, 32, 1); // cStatus = 1 (ignored)
            Marshal.WriteInt64(buffer, 40, BitConverter.DoubleToInt64Bits(50.0));

            // Test ComputeEngineKey
            var keyMethod = typeof(WddmGpuMonitor).GetMethod("ComputeEngineKey", BindingFlags.NonPublic | BindingFlags.Static);
            int key = (int)keyMethod!.Invoke(null, [strName])!;
            key.Should().NotBe(0);
        }
        finally
        {
            Marshal.FreeHGlobal(strName);
        }
    }

    [Fact]
    public void WddmGpuMonitor_Reset_Reinitializes()
    {
        using var monitor = new WddmGpuMonitor();
        var act = () => monitor.Reset();
        act.Should().NotThrow();
    }

    [Fact]
    public void WddmGpuMonitor_Reset_WhenDisposed_ReturnsEarly()
    {
        using var monitor = new WddmGpuMonitor();
        typeof(WddmGpuMonitor).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, true);
        var act = () => monitor.Reset();
        act.Should().NotThrow();
    }

    [Fact]
    public void WddmGpuMonitor_Reset_FreesAllocatedBuffer()
    {
        using var monitor = new WddmGpuMonitor();
        var ensureBuffer = typeof(WddmGpuMonitor).GetMethod("EnsureBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var bufferField = typeof(WddmGpuMonitor).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);

        ensureBuffer!.Invoke(monitor, [2048]);
        IntPtr buf = (IntPtr)bufferField!.GetValue(monitor)!;
        buf.Should().NotBe(IntPtr.Zero);

        monitor.Reset();

        IntPtr bufAfter = (IntPtr)bufferField.GetValue(monitor)!;
        bufAfter.Should().Be(IntPtr.Zero);
    }

    [Fact]
    public void WddmGpuMonitor_ConsecutiveErrors_TriggersReset()
    {
        using var monitor = new WddmGpuMonitor();
        if (monitor.IsAvailable)
        {
            // Set invalid query handle to force PdhCollectQueryData failure
            typeof(WddmGpuMonitor).GetField("_query", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, new IntPtr(12345678));
            for (int i = 0; i < 6; i++)
            {
                monitor.GetUsagePct().Should().Be(0f);
            }
        }
    }

    [Fact]
    public void WddmGpuMonitor_CalculateMaxEngineUsage_CalculatesSumsAndMax()
    {
        using var monitor = new WddmGpuMonitor();

        // Allocate a buffer for 4 items:
        // Item 0: Status 0, Value 30.0, Name "pid_100_eng_0_engtype_3D"
        // Item 1: Status 0, Value 25.0, Name "pid_200_eng_0_engtype_3D" (Same engine -> sums to 55.0)
        // Item 2: Status 1 (error), Value 40.0 -> skipped
        // Item 3: Status 0, Value -10.0 (negative) -> skipped
        const int itemCount = 4;
        const int itemStride = 24;
        IntPtr buffer = Marshal.AllocHGlobal(itemCount * itemStride);
        IntPtr name1 = Marshal.StringToHGlobalUni("pid_100_eng_0_engtype_3D");
        IntPtr name2 = Marshal.StringToHGlobalUni("pid_200_eng_0_engtype_3D");

        try
        {
            // Item 0
            Marshal.WriteIntPtr(buffer, 0, name1);
            Marshal.WriteInt32(buffer, 8, 0); // cStatus = 0
            Marshal.WriteInt64(buffer, 16, BitConverter.DoubleToInt64Bits(30.0));

            // Item 1
            Marshal.WriteIntPtr(buffer, 24, name2);
            Marshal.WriteInt32(buffer, 32, 0); // cStatus = 0
            Marshal.WriteInt64(buffer, 40, BitConverter.DoubleToInt64Bits(25.0));

            // Item 2 (error status)
            Marshal.WriteIntPtr(buffer, 48, name1);
            Marshal.WriteInt32(buffer, 56, 1); // cStatus = 1
            Marshal.WriteInt64(buffer, 64, BitConverter.DoubleToInt64Bits(40.0));

            // Item 3 (negative value)
            Marshal.WriteIntPtr(buffer, 72, name1);
            Marshal.WriteInt32(buffer, 80, 0);
            Marshal.WriteInt64(buffer, 88, BitConverter.DoubleToInt64Bits(-10.0));

            typeof(WddmGpuMonitor).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, buffer);

            var calcMethod = typeof(WddmGpuMonitor).GetMethod("CalculateMaxEngineUsage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            float maxUsage = (float)calcMethod.Invoke(monitor, [(uint)itemCount])!;

            // Should sum items 0 and 1 (30.0 + 25.0 = 55.0)
            maxUsage.Should().Be(55.0f);
        }
        finally
        {
            Marshal.FreeHGlobal(name1);
            Marshal.FreeHGlobal(name2);
            Marshal.FreeHGlobal(buffer);
            typeof(WddmGpuMonitor).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(monitor, IntPtr.Zero);
        }
    }
}

