namespace PickleballGenie.Api.Services;

/// <summary>
/// The "who's training?" answer a client sends with a workout or drill
/// request. Compared against <c>Drill.MinPlayers</c>: a drill is offered when
/// <c>MinPlayers &lt;= playersAvailable</c>, so a group can run partner and solo
/// drills, a pair can run partner and solo drills, and a solo player only
/// sees drills that need nobody else. Null means the client didn't say, which
/// leaves the catalog unfiltered (the pre-tagging behaviour).
/// </summary>
public static class PlayersAvailable
{
    public const int Solo = 1;
    public const int Partner = 2;
    public const int Group = 4;

    public const string ValidationMessage =
        "playersAvailable must be 1 (just me), 2 (with a partner) or 4 (group of 3-4).";

    public static bool IsValid(int? value) => value is null or Solo or Partner or Group;

    public static string Describe(int? value) => value switch
    {
        Solo => "Solo",
        Partner => "Partner",
        Group => "Group",
        _ => "Unknown"
    };

    /// <summary>Prompt tag for a drill's own requirement (1, 2 or 4 players).</summary>
    public static string TagForMinPlayers(int minPlayers) => minPlayers switch
    {
        <= Solo => "Solo",
        <= Partner => "Partner",
        _ => "Group"
    };
}
