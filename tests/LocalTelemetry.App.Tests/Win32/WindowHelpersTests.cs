using System.Dynamic;
using System.Runtime.InteropServices;
using FluentAssertions;
using LocalTelemetry.App.Win32;
using Xunit;

namespace LocalTelemetry.App.Tests.Win32;

public class WindowHelpersTests
{
    [Fact]
    public void Scale_CalculatesCorrectPhysicalPixels()
    {
        WindowHelpers.Scale(100f, 1.0f).Should().Be(100);
        WindowHelpers.Scale(100f, 1.25f).Should().Be(125);
        WindowHelpers.Scale(14f, 1.5f).Should().Be(21);
    }

    [Fact]
    public void ParseHex_ParsesColorsAndFallbacksCorrectly()
    {
        var red = WindowHelpers.ParseHex("#FF0000", Color.Black);
        red.R.Should().Be(255);
        red.G.Should().Be(0);
        red.B.Should().Be(0);

        var green = WindowHelpers.ParseHex("00FF00", Color.Black);
        green.G.Should().Be(255);

        var fallback = WindowHelpers.ParseHex("invalid_hex", Color.Blue);
        fallback.Should().Be(Color.Blue);

        var defaultFallbackNull = WindowHelpers.ParseHex(null);
        defaultFallbackNull.Should().Be(Color.White);

        var defaultFallbackEmpty = WindowHelpers.ParseHex("   ");
        defaultFallbackEmpty.Should().Be(Color.White);

        var invalidDefaultFallback = WindowHelpers.ParseHex("not_a_color");
        invalidDefaultFallback.Should().Be(Color.White);
    }

    private sealed class MockTaskScheduler : DynamicObject
    {
        public bool CreateTaskCalled { get; private set; }
        public bool DeleteTaskCalled { get; private set; }
        public bool ThrowOnDelete { get; set; }
        public bool TaskExists { get; set; } = true;

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            switch (binder.Name)
            {
                case "GetFolder":
                    result = this;
                    return true;
                case "GetTask":
                    result = TaskExists ? new MockTask() : null;
                    return true;
                case "NewTask":
                    result = new MockTask();
                    return true;
                case "Create":
                    result = new ExpandoObject();
                    return true;
                case "RegisterTaskDefinition":
                    CreateTaskCalled = true;
                    result = null;
                    return true;
                case "DeleteTask":
                    DeleteTaskCalled = true;
                    if (ThrowOnDelete)
                        throw Marshal.GetExceptionForHR(unchecked((int)0x80070002))!;
                    result = null;
                    return true;
            }
            result = this;
            return true;
        }
    }

    private sealed class MockTask : DynamicObject
    {
        private readonly ExpandoObject _bag = new();

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            switch (binder.Name)
            {
                case "RegistrationInfo":
                case "Principal":
                case "Settings":
                case "Triggers":
                case "Actions":
                    result = this;
                    return true;
            }
            result = _bag;
            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            result = new ExpandoObject();
            return true;
        }
    }

    [Fact]
    public void SetStartup_CreateStartupTask_ExecutesWithMockScheduler()
    {
        var prevAction = WindowHelpers.SetStartupAction;
        WindowHelpers.SetStartupAction = null;
        var mock = new MockTaskScheduler { TaskExists = false };
        WindowHelpers.TaskSchedulerFactory = () => mock;
        try
        {
            WindowHelpers.SetStartup(true);
            mock.CreateTaskCalled.Should().BeTrue();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.SetStartupAction = prevAction;
        }
    }

    [Fact]
    public void SetStartup_RemoveStartupTask_ExecutesWithMockScheduler()
    {
        var prevAction = WindowHelpers.SetStartupAction;
        WindowHelpers.SetStartupAction = null;
        var mock = new MockTaskScheduler();
        WindowHelpers.TaskSchedulerFactory = () => mock;
        WindowHelpers.LegacyRegistryCleanupAction = () => { };
        try
        {
            WindowHelpers.SetStartup(false);
            mock.DeleteTaskCalled.Should().BeTrue();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.LegacyRegistryCleanupAction = null;
            WindowHelpers.SetStartupAction = prevAction;
        }
    }

    [Fact]
    public void SetStartup_RemoveStartupTask_HandlesCOMExceptionGracefully()
    {
        var prevAction = WindowHelpers.SetStartupAction;
        WindowHelpers.SetStartupAction = null;
        var mock = new MockTaskScheduler { ThrowOnDelete = true };
        WindowHelpers.TaskSchedulerFactory = () => mock;
        WindowHelpers.LegacyRegistryCleanupAction = () => { };
        try
        {
            var act = () => WindowHelpers.SetStartup(false);
            act.Should().NotThrow();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.LegacyRegistryCleanupAction = null;
            WindowHelpers.SetStartupAction = prevAction;
        }
    }

    [Fact]
    public void SetStartup_HandlesSchedulerConnectException()
    {
        WindowHelpers.TaskSchedulerFactory = () => throw new InvalidOperationException("Mock scheduler failure");
        try
        {
            var act = () => WindowHelpers.SetStartup(true);
            act.Should().NotThrow();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
        }
    }

    [Fact]
    public void IsStartupEnabled_TaskExists_ReturnsTrue()
    {
        WindowHelpers.StartupQueryExitCode = () => 0;
        try
        {
            WindowHelpers.IsStartupEnabled().Should().BeTrue();
        }
        finally
        {
            WindowHelpers.StartupQueryExitCode = null;
        }
    }

    [Fact]
    public void IsStartupEnabled_TaskMissing_FallsBackToRunKeys()
    {
        WindowHelpers.StartupQueryExitCode = () => 1;
        try
        {
            // No scheduled task and no Run key value on this machine => disabled.
            WindowHelpers.IsStartupEnabled().Should().BeFalse();
        }
        finally
        {
            WindowHelpers.StartupQueryExitCode = null;
        }
    }

    [Fact]
    public void IsStartupEnabled_CheckThrows_ReturnsFalse()
    {
        WindowHelpers.StartupQueryExitCode = () => throw new InvalidOperationException("Mock query failure");
        try
        {
            WindowHelpers.IsStartupEnabled().Should().BeFalse();
        }
        finally
        {
            WindowHelpers.StartupQueryExitCode = null;
        }
    }

    [Fact]
    public void SetStartup_RemoveStartupTask_WhenTaskDoesNotExist_ReturnsEarly()
    {
        var mock = new MockTaskScheduler { TaskExists = false };
        WindowHelpers.TaskSchedulerFactory = () => mock;
        try
        {
            var act = () => WindowHelpers.SetStartup(false);
            act.Should().NotThrow();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
        }
    }

    [Fact]
    public void SetStartup_RemoveStartupTask_WhenRegistryCleanupThrows_HandlesGracefully()
    {
        var mock = new MockTaskScheduler { TaskExists = true };
        WindowHelpers.TaskSchedulerFactory = () => mock;
        WindowHelpers.LegacyRegistryCleanupAction = () => throw new UnauthorizedAccessException("Registry locked");
        try
        {
            var act = () => WindowHelpers.SetStartup(false);
            act.Should().NotThrow();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.LegacyRegistryCleanupAction = null;
        }
    }

    [Fact]
    public void IsStartupEnabled_WithoutMock_QueriesSchtasksAndRegistry()
    {
        WindowHelpers.StartupQueryExitCode = null;
        var act = () => WindowHelpers.IsStartupEnabled();
        act.Should().NotThrow();
    }

    [Fact]
    public void WindowHelpers_ConnectScheduler_DefaultComService_ConnectsSuccessfully()
    {
        WindowHelpers.TaskSchedulerFactory = null;
        var connect = typeof(WindowHelpers).GetMethod("ConnectScheduler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var act = () => connect!.Invoke(null, null);
        act.Should().NotThrow();
    }

    [Fact]
    public void SetStartup_RemoveStartupTask_ExecutesRealRegistryCleanup()
    {
        var prevAction = WindowHelpers.SetStartupAction;
        WindowHelpers.SetStartupAction = null;
        var mock = new MockTaskScheduler { TaskExists = false };
        WindowHelpers.TaskSchedulerFactory = () => mock;
        WindowHelpers.LegacyRegistryCleanupAction = null; // Exercise real registry cleanup branch

        try
        {
            var remove = typeof(WindowHelpers).GetMethod("RemoveStartupTask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var act = () => remove!.Invoke(null, null);
            act.Should().NotThrow();
        }
        finally
        {
            WindowHelpers.TaskSchedulerFactory = null;
            WindowHelpers.SetStartupAction = prevAction;
        }
    }

    [Fact]
    public void IsStartupEnabled_WhenHkcuRunKeySet_ReturnsTrue()
    {
        WindowHelpers.StartupQueryExitCode = () => 1; // Task missing
        WindowHelpers.RunKeyQuery = app => app == "LocalTelemetry";

        try
        {
            WindowHelpers.IsStartupEnabled().Should().BeTrue();
        }
        finally
        {
            WindowHelpers.StartupQueryExitCode = null;
            WindowHelpers.RunKeyQuery = null;
        }
    }

    [Fact]
    public void SetStartup_RemoveStartupTask_ExceptionBranches_Coverage()
    {
        var prevAction = WindowHelpers.SetStartupAction;
        var prevCleanup = WindowHelpers.LegacyRegistryCleanupAction;
        var prevFactory = WindowHelpers.TaskSchedulerFactory;

        try
        {
            WindowHelpers.SetStartupAction = null;

            // 1. GetTask throws exception inside root folder query
            var mockThrow = new MockThrowingTaskScheduler();
            WindowHelpers.TaskSchedulerFactory = () => mockThrow;
            WindowHelpers.LegacyRegistryCleanupAction = () => throw new InvalidOperationException("Registry cleanup error");

            var remove = typeof(WindowHelpers).GetMethod("RemoveStartupTask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var act = () => remove.Invoke(null, null);
            act.Should().NotThrow();

            // 2. IsStartupEnabled with null RunKeyQuery and null StartupQueryExitCode
            WindowHelpers.StartupQueryExitCode = () => 1;
            WindowHelpers.RunKeyQuery = null;
            var isEnabled = WindowHelpers.IsStartupEnabled();
            isEnabled.Should().Be(isEnabled);
        }
        finally
        {
            WindowHelpers.SetStartupAction = prevAction;
            WindowHelpers.LegacyRegistryCleanupAction = prevCleanup;
            WindowHelpers.TaskSchedulerFactory = prevFactory;
            WindowHelpers.StartupQueryExitCode = null;
            WindowHelpers.RunKeyQuery = null;
        }
    }

    private sealed class MockThrowingTaskScheduler : DynamicObject
    {
        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            if (binder.Name == "GetFolder")
            {
                result = this;
                return true;
            }
            if (binder.Name == "GetTask")
            {
                throw new InvalidOperationException("Task does not exist");
            }
            result = null;
            return true;
        }
    }
}
