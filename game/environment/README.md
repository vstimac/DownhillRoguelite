# environment

Light and atmosphere driven by the run clock: the day/night cycle (sun, sky, ambient light, fog) and the weather (`weather/`).

- `WeatherSystem` publishes `GameEvents.CurrentWeather` every physics frame from the seed, the circle (RunState.Depth) and the clock, using `WeatherForecast` (stateless) and the profiles in `weather/default_weather.tres`.
- `DayNightCycle` and `WeatherParticles` show the weather.
- Gameplay systems read `GameEvents.CurrentWeather` themselves: grip and snow speed (PlayerController), wind (BalanceController), chill and heat (CarrierNeeds). See docs/survival-loop.md.
