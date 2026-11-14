namespace DownhillRoguelite.Core;

/// <summary>
/// How a run ended: published once when the carrier dies (GameEvents.RunEnded), shown in the world
/// and kept in the records. Death is permanent; the next run is a new mountain.
/// </summary>
/// <param name="RunNumber">1 for the first run of this save.</param>
/// <param name="Seed">The mountain's seed (replay it with --seed).</param>
/// <param name="Depth">Metres descended from the summit.</param>
/// <param name="Station">Deepest station reached (0 = none).</param>
/// <param name="Day">In-game day the run ended on.</param>
/// <param name="CircleName">The circle the carrier died in.</param>
/// <param name="Cause">What killed the carrier (damage source).</param>
/// <param name="BestDepth">Deepest descent of any run, this one included.</param>
/// <param name="NewRecord">Whether this run set BestDepth.</param>
/// <param name="ContractsDelivered">Cargo contracts handed over at their station.</param>
/// <param name="ContractsFailed">Contracts lost, abandoned or unfinished.</param>
/// <param name="CargoScore">Score earned by delivering cargo.</param>
/// <param name="Score">Depth, stations and cargo together (RunState.ScoreOf); depth stays the headline.</param>
/// <param name="RelicsDelivered">Relics left at an altar and studied.</param>
/// <param name="Boons">The boons and bargains taken in this run, by name ("" when none).</param>
public readonly record struct RunSummary(
    int RunNumber,
    ulong Seed,
    int Depth,
    int Station,
    int Day,
    string CircleName,
    string Cause,
    int BestDepth,
    bool NewRecord,
    int ContractsDelivered = 0,
    int ContractsFailed = 0,
    int CargoScore = 0,
    int Score = 0,
    int RelicsDelivered = 0,
    string Boons = "");
