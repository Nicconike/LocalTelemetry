# :git-branch: Contributing & Coding Standards

Contributions to **LocalTelemetry** are welcome! As an independently developed open-source project, community input is appreciated. Whether you are fixing bugs, improving documentation, or implementing new hardware sensors, please adhere to these coding standards.

## :file-text: Coding Standards & Guidelines

### :file-code: C# Backend Guidelines (.NET 10 / C# 13)

- **Class Sealing**: Use `sealed` by default on all concrete C# classes unless the class is explicitly designed for inheritance
- **Type Inference**: Avoid excessive `var` usage. Only use `var` when the right-hand side type is explicitly obvious
- **Exception Handling**: **Never swallow exceptions**. Every `catch` block must log via `Log.Error(...)`, handle the error visibly, or rethrow
- **XML Documentation**: Provide XML documentation comments (`/// <summary>`) on all public classes, methods, and properties
- **Formatting**: Run Microsoft official `dotnet format --verify-no-changes` before opening a pull request

```csharp
/// <summary>Reads CPU core temperature via PawnIO MSR registers.</summary>
public sealed class CpuTemperatureReader
{
    public double GetTemperatureCelsius()
    {
        try
        {
            // Hardware query logic
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to query CPU temperature register");
            return 0;
        }
    }
}
```

### :code-xml: Svelte 5 Frontend Guidelines

- **Bun Only**: Always use **Bun** for managing JavaScript/TypeScript dependencies. Never run `npm install` or `yarn install`
- **Svelte 5 Runes**: Use `$state()`, `$derived()`, `$effect()`. Do NOT use legacy `$:` reactive statements or `export let`

```svelte
<script lang="ts">
  let count = $state(0);
  let double = $derived(count * 2);

  $effect(() => {
    console.log(`Count changed to ${count}`);
  });
</script>
```

## :palette: Contributing Hardware Brand Colors

LocalTelemetry automatically detects installed hardware components (CPUs, GPUs, RAM, Disks, Network Adapters, Batteries, and OEMs) and styles their metrics using official vendor brand colors defined in `BrandColorDefaults.cs`.

If your hardware component uses a generic fallback color because its manufacturer is not yet in the database, community pull requests are welcome!

### :shield-check: Contribution Guardrails & Standards

To maintain high data accuracy and visual quality, all brand color additions must adhere to the following guardrails:

1. :shield-check: **Authoritative Source Required**:
   - The hex code must originate from an official brand portal, press kit, SVG logo asset, or brand guidelines (e.g. brand style guides, official vector files).
   - In your Pull Request description, **provide a direct citation link** or reference verifying the color code.
2. :sun-medium: **Contrast & Taskbar Legibility**:
   - The overlay renders against dark and light taskbar themes.
   - Avoid near-black tones (e.g. `#000000` or `#0a0a0a`) or low-contrast muted pastels that become invisible on default taskbars.
3. :file-code: **Codebase Implementation (`BrandColorDefaults.cs`)**:
   - **`_brand` Dictionary**: Register the canonical manufacturer name and upper-case 6-character hex code (`["BrandName"] = "#RRGGBB"`).
   - **`_category` Lists**: Add the brand key into the appropriate hardware category array (`cpu`, `gpu`, `ram`, `disk`, `nic`, `oem`, or `battery`).
   - **`_alias` Map (If Applicable)**: If Windows WMI or vendor query APIs return common abbreviations (e.g. `WD` or `WDC` for `Western Digital`, `SKHYNIX` for `SK Hynix`), map the alias to the canonical name.

```csharp
// Example: Adding a new storage manufacturer in BrandColorDefaults.cs
private static readonly Dictionary<string, string> _brand = new(StringComparer.OrdinalIgnoreCase)
{
    ["Solidigm"] = "#7F35B2", // Official Solidigm Violet
};

private static readonly Dictionary<string, string[]> _category = new(StringComparer.OrdinalIgnoreCase)
{
    ["disk"] = ["Solidigm", ...],
};
```

1. :test-tube: **Verification & Testing**:
   - Run the unit test suite before opening your PR:

     ```powershell
     dotnet test tests/LocalTelemetry.Core.Tests
     ```

   - Verify that `BrandColorDefaultsTests` passes cleanly.

## :git-commit-horizontal: Commit Message Convention

This repository enforces the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) standard:

```bash
<type>(<scope>): <summary>
```

### :list-tree: Commit Types

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation updates
- `style`: Formatting, missing semicolons, UI layout tweaks
- `refactor`: Refactoring code without changing behavior
- `perf`: Performance optimizations
- `test`: Adding or updating tests
- `ci`: CI/CD pipeline changes
- `build`: Build system or dependency changes
- `chore`: Maintenance

### :crosshair: Scopes

`core`, `app`, `overlay`, `monitor`, `config`, `ci`, `docs`

### :code: Commit Message Examples

```powershell
feat(overlay): add GPU VRAM usage indicator
fix(core): handle missing PawnIo driver initialization failure gracefully
build(deps): bump NuGet packages
```

## :git-pull-request: Pull Request Process

1. Fork the repository and create a feature branch (`feature/my-cool-sensor`).
2. Verify frontend builds and tests:

   ```powershell
   cd src/LocalTelemetry.App/Settings/wwwroot
   bun install
   bun run build
   cd ../../../../tests/LocalTelemetry.App.Tests/Settings/wwwroot
   bun install
   bun run test
   cd ../../../..
   ```

3. Verify backend builds and tests:

   ```powershell
   dotnet format --verify-no-changes
   dotnet build LocalTelemetry.sln
   dotnet test LocalTelemetry.sln
   ```

4. Open a Pull Request on GitHub. Keep PRs focused on one logical change.
