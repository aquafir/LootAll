namespace LootAll;

public class Settings
{
    public LootStyle LootStyle { get; set; } = LootStyle.RoundRobin;
    public LooterRequirements LooterRequirements { get; set; } = LooterRequirements.Range;
}

public enum LootStyle
{
    /// <summary>
    /// Finder keeps all
    /// </summary>
    Finder      = 0,
    /// <summary>
    /// Duplicate items through fellow
    /// </summary>
    OneForAll   = 1,
    /// <summary>
    /// Chooses random start and cycles through
    /// </summary>
    RoundRobin  = 2,
}

public enum LooterRequirements
{
    None        = 0,
    Landblock   = 1,
    Range       = 2,
}