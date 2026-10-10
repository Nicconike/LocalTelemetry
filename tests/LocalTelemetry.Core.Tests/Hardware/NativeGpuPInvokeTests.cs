using System.Reflection;
using FluentAssertions;
using LocalTelemetry.Core.Hardware;
using Xunit;

namespace LocalTelemetry.Core.Tests.Hardware;

public class NativeGpuPInvokeTests
{
    [Fact]
    public void IntelGpuNativePInvoke_AllMethods_ExecuteSafely()
    {
        var pinvokeType = typeof(IntelGpuMonitor).Assembly.GetType("LocalTelemetry.Core.Hardware.IntelGpuNativePInvoke");
        pinvokeType.Should().NotBeNull();

        var instanceField = pinvokeType!.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        var instance = instanceField!.GetValue(null);
        instance.Should().NotBeNull();

        // 1. Init & Close
        var initMethod = pinvokeType.GetMethod("Init");
        object?[] initArgs = [IntPtr.Zero];
        initMethod!.Invoke(instance, initArgs);

        var closeMethod = pinvokeType.GetMethod("Close");
        closeMethod!.Invoke(instance, [IntPtr.Zero]);

        // 2. EnumerateDevices
        var enumMethod = pinvokeType.GetMethod("EnumerateDevices");
        object?[] enumArgs = [IntPtr.Zero, null];
        enumMethod!.Invoke(instance, enumArgs);

        // 3. GetDeviceName
        var nameMethod = pinvokeType.GetMethod("GetDeviceName");
        object?[] nameArgs = [IntPtr.Zero, null];
        nameMethod!.Invoke(instance, nameArgs);

        // 4. Sensors & Domains
        var tempSensorMethod = pinvokeType.GetMethod("GetTempSensor");
        object?[] tempSensorArgs = [IntPtr.Zero, IntPtr.Zero];
        tempSensorMethod!.Invoke(instance, tempSensorArgs);

        var freqDomainMethod = pinvokeType.GetMethod("GetFreqDomain");
        object?[] freqDomainArgs = [IntPtr.Zero, IntPtr.Zero];
        freqDomainMethod!.Invoke(instance, freqDomainArgs);

        var memModuleMethod = pinvokeType.GetMethod("GetMemModule");
        object?[] memModuleArgs = [IntPtr.Zero, IntPtr.Zero];
        memModuleMethod!.Invoke(instance, memModuleArgs);

        // 5. Telemetry metrics
        var tempMethod = pinvokeType.GetMethod("GetTempC");
        object?[] tempArgs = [IntPtr.Zero, 0.0];
        tempMethod!.Invoke(instance, tempArgs);

        var freqMethod = pinvokeType.GetMethod("GetClockMHz");
        object?[] freqArgs = [IntPtr.Zero, 0.0];
        freqMethod!.Invoke(instance, freqArgs);

        var vramMethod = pinvokeType.GetMethod("GetVram");
        object?[] vramArgs = [IntPtr.Zero, 0UL, 0UL];
        vramMethod!.Invoke(instance, vramArgs);
    }

    [Fact]
    public void NvGpuNativePInvoke_AllMethods_ExecuteSafely()
    {
        var pinvokeType = typeof(NvGpuMonitor).Assembly.GetType("LocalTelemetry.Core.Hardware.NvGpuNativePInvoke");
        pinvokeType.Should().NotBeNull();

        var instanceField = pinvokeType!.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        var instance = instanceField!.GetValue(null);
        instance.Should().NotBeNull();

        // 1. Init & Shutdown
        var initMethod = pinvokeType.GetMethod("Init");
        initMethod!.Invoke(instance, null);

        var shutdownMethod = pinvokeType.GetMethod("Shutdown");
        shutdownMethod!.Invoke(instance, null);

        // 2. GetDeviceHandleByIndex
        var devMethod = pinvokeType.GetMethod("GetDeviceHandleByIndex");
        object?[] devArgs = [0, IntPtr.Zero];
        devMethod!.Invoke(instance, devArgs);

        // 3. GetDeviceName
        var nameMethod = pinvokeType.GetMethod("GetDeviceName");
        object?[] nameArgs = [IntPtr.Zero, null];
        nameMethod!.Invoke(instance, nameArgs);

        // 4. Metrics with dummy device
        var verMethod = pinvokeType.GetMethod("GetDriverVersion");
        object?[] verArgs = [null];
        verMethod!.Invoke(instance, verArgs);

        var tempMethod = pinvokeType.GetMethod("GetTemperature");
        object?[] tempArgs = [IntPtr.Zero, 0, 0u];
        tempMethod!.Invoke(instance, tempArgs);

        var utilMethod = pinvokeType.GetMethod("GetUtilizationRates");
        object?[] utilArgs = [IntPtr.Zero, 0u, 0u];
        utilMethod!.Invoke(instance, utilArgs);

        var memMethod = pinvokeType.GetMethod("GetMemoryInfo");
        object?[] memArgs = [IntPtr.Zero, 0ul, 0ul, 0ul];
        memMethod!.Invoke(instance, memArgs);

        var clockMethod = pinvokeType.GetMethod("GetClockInfo");
        object?[] clockArgs = [IntPtr.Zero, 0, 0u];
        clockMethod!.Invoke(instance, clockArgs);

        var powerMethod = pinvokeType.GetMethod("GetPowerUsage");
        object?[] powerArgs = [IntPtr.Zero, 0u];
        powerMethod!.Invoke(instance, powerArgs);

        var limitMethod = pinvokeType.GetMethod("GetPowerManagementLimit");
        object?[] limitArgs = [IntPtr.Zero, 0u];
        limitMethod!.Invoke(instance, limitArgs);
    }

    [Fact]
    public void AmdAdlNativePInvoke_AllMethods_ExecuteSafely()
    {
        var pinvokeType = typeof(AmdGpuMonitor).Assembly.GetType("LocalTelemetry.Core.Hardware.AmdAdlNativePInvoke");
        pinvokeType.Should().NotBeNull();

        var instanceField = pinvokeType!.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        var instance = instanceField!.GetValue(null);
        instance.Should().NotBeNull();

        // 1. Create & Destroy
        var createMethod = pinvokeType.GetMethod("Create");
        createMethod!.Invoke(instance, null);

        var destroyMethod = pinvokeType.GetMethod("Destroy");
        destroyMethod!.Invoke(instance, null);

        // 2. GetAdapterInfo
        var infoMethod = pinvokeType.GetMethod("GetAdapterInfo");
        object?[] infoArgs = [0, null];
        infoMethod!.Invoke(instance, infoArgs);

        // 3. Metrics
        var tempMethod = pinvokeType.GetMethod("GetTemperature");
        object?[] tempArgs = [0, 0];
        tempMethod!.Invoke(instance, tempArgs);

        var actMethod = pinvokeType.GetMethod("GetActivity");
        object?[] actArgs = [0, 0, 0];
        actMethod!.Invoke(instance, actArgs);

        var memMethod = pinvokeType.GetMethod("GetMemoryInfo");
        object?[] memArgs = [0, 0L, 0L];
        memMethod!.Invoke(instance, memArgs);

        var limitMethod = pinvokeType.GetMethod("GetPowerLimit");
        object?[] limitArgs = [0, 0, 0];
        limitMethod!.Invoke(instance, limitArgs);
    }
}
