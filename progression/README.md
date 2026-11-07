# progression

What changes a run and what carries over between runs (M12). See docs/survival-loop.md.

- `BoonDefinition`: a blessing offered at stations; multiplies `RunState.Modifiers` for the rest of the run.
- `BoonOfferings` and `BoonStone`: three seeded offerings by a station's fire the first time the carrier reaches it; E chooses one.
- `StartingKit`: gear unlocked by the best depth ever (`RunState.Records`), granted at the start of every run.

The run loop itself (death, summary, new mountain) and the records live in core/ (`RunState`, `RunRecords`, `RunSummary`, `RunModifiers`).
