namespace Timekeeper.Core;

public enum ActivityState
{
    /// <summary>The user is working in a window.</summary>
    Active,

    /// <summary>No keyboard or mouse input for longer than the idle threshold.</summary>
    Idle,

    /// <summary>The workstation is locked.</summary>
    Locked,
}
