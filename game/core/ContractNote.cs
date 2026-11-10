namespace DownhillRoguelite.Core;

/// <summary>
/// What the rest of the game may know about a cargo contract (published by the contract system with
/// every change): journal, narrator, run records and UI read it and change nothing.
/// </summary>
/// <param name="InstanceId">Unique within the run; also the id of its cargo.</param>
/// <param name="Title">The job's name, as the board shows it.</param>
/// <param name="CargoName">What is carried.</param>
/// <param name="CargoMassKg">How heavy it is.</param>
/// <param name="FromStation">Where it was offered.</param>
/// <param name="ToStation">Where it must be handed over.</param>
/// <param name="Status">Where it stands now.</param>
/// <param name="Score">Score credited (only when delivered).</param>
/// <param name="Reason">Why it ended, in words, when it ended badly.</param>
/// <param name="Condition">The cargo's condition, 100 (intact) to 0 (broken).</param>
/// <param name="MinCondition">The condition it must arrive in (0: any).</param>
/// <param name="DeadlineHours">World-clock hour it must arrive by (WorldClock.TotalHours); negative: none.</param>
/// <param name="Breakable">Whether knocks damage it at all.</param>
public readonly record struct ContractNote(
    string InstanceId,
    string Title,
    string CargoName,
    float CargoMassKg,
    int FromStation,
    int ToStation,
    ContractStatus Status,
    int Score,
    string Reason,
    float Condition = 100.0f,
    float MinCondition = 0.0f,
    double DeadlineHours = -1.0,
    bool Breakable = false);
