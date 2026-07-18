# Config Profiles

Profile files overlay the base `sim_config.toml` without modifying it.
They are useful for tuning runs, A/B testing, and one-off experiments.

## How profiles work

1. The base config is loaded normally from `config/sim_config.toml`.
2. The profile file is merged over it — only keys present in the profile override the base.
3. Any `--set key=value` CLI flags are applied last (winning over both base and profile).

All keys in a profile must be bound to a C# config property (the strict loader checks them).

## Using profiles

From the headless runner (Phase A):

```
WorldEngine.Sim --seed 12345 --years 500 --profile fast_history
```

From code (tests, tools):

```csharp
var config = SimConfigLoader.Load(profileName: "fast_history");
```

## Creating a profile

Profile files use the same TOML syntax as `sim_config.toml` but only include the keys
you want to override. Example: `config/profiles/fast_history.toml`

```toml
[sim_loop]
ticks_per_seasonal_change = 1   # 1 tick/season → 4 ticks/year (4× faster)
```

All missing keys fall back to the base config values. The strict loader will reject
any profile key that does not correspond to a C# config property.
