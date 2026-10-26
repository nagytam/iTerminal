namespace iConsole.Data;

public record MouseEvent
{
    public Position Position { get; set; } = new Position(0, 0);
    public bool LeftClicked { get; set; }
    public bool RightClicked { get; set; }

    /// <summary>SGR mouse release (<c>m</c> suffix) or other release semantics.</summary>
    public bool IsRelease { get; set; }

    /// <summary>The pointer moved while the left button was held (SGR motion with button 0, or a Windows move with the button down).</summary>
    public bool IsDrag { get; set; }

    /// <summary>Vertical wheel: +1 up, -1 down, 0 none (SGR buttons 64/65).</summary>
    public int WheelDelta { get; set; }

    /// <summary>The pointer moved with no button held. Nothing is clicked; tooltips use it to find what is under the pointer.</summary>
    public bool IsHover { get; set; }
}
