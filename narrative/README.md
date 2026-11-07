# narrative

Story data for the narrator companion. See docs/narrative.md for the setting.

`data/metadata.csv`: 500 narrator lines with trigger, circle, phase and pacing metadata. Imported with the "keep" importer so Godot treats it as a plain file, not a translation table. Read it with `FileAccess.GetCsvLine`.

`NarratorDirector` plays the voiced lines (`audio/narrator/`) on game events: it maps events (falls, stumbles, stations, weather, depth, circles, idling, pickups, load, death) to the CSV triggers, then picks a line that fits the circle and is off cooldown, preferring the current narrative phase. Triggers for systems that don't exist yet (traders, ruins, routes, relics) wait for those systems. Live conversation through NobodyWho is the next stage (M11).
