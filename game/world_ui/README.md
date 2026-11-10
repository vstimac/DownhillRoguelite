# world_ui

Diegetic interfaces: in-world panels, signs and interaction prompts. No flat UI during runs.

The one exception is `PauseMenu` (Esc / P): a flat screen that exists only while the run is paused.

The start screen (`StartScreen`) is the other: the front end shown before the first run, over the real
mountain (`MenuCamera` drifts past the carrier and flies into the run's camera on New Run). Its pages
(`CharacterPage`, `OptionsPanel`, `CreditsPage`) are parchment sheets; `OptionsPanel` is shared with the
pause menu. The widgets live in `ui/menu/` (`MenuItem`, `OptionRow` and its kinds, `TabStrip`).
Render checks: `-- --menu-page=character|options|about` opens a page; `-- --skip-start` skips it.
