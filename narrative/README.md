# narrative

Story data for the narrator companion. See docs/narrative.md for the setting.

`data/metadata.csv`: 500 narrator lines with trigger, circle, phase and pacing metadata. Imported with the "keep" importer so Godot treats it as a plain file, not a translation table. Read it with `FileAccess.GetCsvLine`.
