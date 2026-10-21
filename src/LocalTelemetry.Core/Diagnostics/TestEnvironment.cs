using System.Reflection;

namespace LocalTelemetry.Core.Diagnostics;

/// <summary>
/// Detects whether the current process is a unit-test host so that privileged operations
/// (elevated relaunch, driver install, service control) fail closed instead of prompting UAC
/// or mutating the system while tests are running.
/// </summary>
public static class TestEnvironment
{
    /// <summary>Returns <c>true</c> when running inside a test runner process.</summary>
    public static bool IsTestHost()
    {
        string processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (processName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("vstest.console", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? name = asm.GetName().Name;
            if (name == "xunit.core" || name == "xunit.execution" || name == "xunit.assert"
                || name == "Microsoft.VisualStudio.TestPlatform.TestFramework"
                || name == "nunit.framework")
            {
                return true;
            }
        }

        return false;
    }
}
