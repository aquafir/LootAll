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
    /// <summary>
    /// No restrictions
    /// </summary>
    None        = 0,
    /// <summary>
    /// Fellow must be in same landblock
    /// </summary>
    Landblock   = 1,
    /// <summary>
    /// Fellow must be within 2x max range
    /// </summary>
    Range       = 2,
}