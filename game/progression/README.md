# progression

What changes a run and what carries over between runs (M12). See docs/survival-loop.md.

- `BoonDefinition`: a blessing offered at stations; multiplies `RunState.Modifiers` for the rest of the run.
- `BoonOfferings` and `BoonStone`: three seeded offerings by a station's fire the first time the carrier reaches it; E chooses one.
- `StartingKit`: gear unlocked by the best depth ever (`RunState.Records`), granted at the start of every run.
- `contracts/`: cargo jobs (roguelite Phase 1). `ContractDirector` offers one seeded job per station on a `ContractBoard`, asks the inventory to pack the cargo (`GameEvents.RequestCargoGrant`; the inventory packs its own copy of the item, tagged with the contract's id) and takes it back at the destination's `DeliveryPost` (`RequestCargoHandOver`). `ContractResolver` holds the rules (Offered, Accepted, Delivered / Failed / Abandoned; an ending is final), `ContractDefinition` resources in `contracts/resources/` hold the jobs. `RunState.Telemetry` counts deliveries for the run summary and score.

The run loop itself (death, summary, new mountain) and the records live in core/ (`RunState`, `RunRecords`, `RunSummary`, `RunModifiers`).
