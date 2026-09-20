namespace SS14Utility.CharacterCard;

/// <summary>Everything needed to render one character card - the 4 sprite exports plus free-text fields.</summary>
public sealed class CharacterCardData
{
    public string Name = "";
    public string Age = "";
    public string Position = "";
    public string Lore = "";

    /// <summary>One skill per line.</summary>
    public string Skills = "";

    public Bitmap? Front;
    public Bitmap? Back;
    public Bitmap? Left;
    public Bitmap? Right;
}
