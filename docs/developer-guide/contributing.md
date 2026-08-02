# Contributing & Coding Standards

We welcome open-source contributions to **LocalTelemetry**! Whether you are fixing bugs, improving documentation or implementing new hardware sensors, please adhere to these coding standards.


## 📜 Coding Standards & Guidelines

### 1. C# Backend Guidelines (.NET 10 / C# 13)

- **Class Sealing**: Use `sealed` by default on all concrete C# classes unless the class is designed for inheritance.
- **Type Inference**: Avoid excessive `var` usage. Only use `var` when the right-hand side type is explicitly obvious.
- **Exception Handling**: **Never swallow exceptions**. Every `catch` block must log via `Log.Error(...)`, handle the error visibly or rethrow.
- **XML Documentation**: Provide XML documentation comments (`/// <summary>`) on all public classes, methods and properties.

```csharp
/// <summary>Reads CPU core temperature via PawnIo MSR registers.</summary>
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
            Log.Error(ex, "Failed to query CPU temperature register.");
            return 0;
        }
    }
}
```

### 2. Svelte 5 Frontend Guidelines

- **Bun Only**: Always use **Bun** for managing JS dependencies. Never run `npm install` or `yarn install`.
- **Svelte 5 Runes**: Use `$state()`, `$derived()`, `$effect()`. Do NOT use legacy `$:` reactive statements or `export let`.

```svelte
<script lang="ts">
  let count = $state(0);
  let double = $derived(count * 2);

  $effect(() => {
    console.log(`Count changed to ${count}`);
  });
</script>
```


## 📝 Commit Message Convention

This repository enforces the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) standard:

```
<type>(<scope>): <summary>
```

### Types
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation updates
- `style`: Formatting, missing semi-colons, UI layout tweaks
- `refactor`: Refactoring code without changing behavior
- `perf`: Performance optimizations
- `test`: Adding or updating tests
- `chore`: Maintenance, updating dependencies or build scripts

### Scopes
`core`, `app`, `overlay`, `settings`, `tray`, `monitor`, `config`, `deps`, `ci`

### Examples
```powershell
feat(overlay): add GPU VRAM usage indicator
fix(core): handle missing PawnIo driver initialization failure gracefully
docs(vitepress): add website analytics tracking guide
```


## 🔀 Pull Request Process

1. Fork the repository and create a feature branch (`feature/my-cool-sensor`).
2. Verify frontend builds: `cd src/LocalTelemetry.App/Settings/wwwroot && bun run build`.
3. Verify backend builds: `dotnet build` from root solution.
4. Open a Pull Request on GitHub. Keep PRs focused on one logical change.
