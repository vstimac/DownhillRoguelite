# environment

Light and atmosphere driven by the run clock: the day/night cycle (sun, sky, ambient light, fog) and the weather (`weather/`).

- `WeatherSystem` publishes `GameEvents.CurrentWeather` every physics frame from the seed, the circle (RunState.Depth) and the clock, using `WeatherForecast` (stateless) and the profiles in `weather/default_weather.tres`.
- `DayNightCycle` and `WeatherParticles` show the weather.
- Gameplay systems read `GameEvents.CurrentWeather` themselves: grip and snow speed (PlayerController), wind (BalanceController), chill and heat (CarrierNeeds). See docs/survival-loop.md.
- Foliage and life (TerrainStreamer adds them): `FoliageWind` sets the shader globals the grass and trees sway by (wind direction, strength, gust noise, the carrier parting the grass; `--wind=0..1`, `--gale`); `FallingLeaves` drifts leaves round the camera where `Biome.LeafFall` says (thick in Greed; `--leaves` everywhere); `Fireflies` glow on dry nights where `Biome.Fireflies` says (Lust most; `--fireflies` everywhere, any hour). Look at the grass and trees in `debug/FoliageLab.tscn`.
