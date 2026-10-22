namespace Game.Rules
{
    /// <summary>The side a unit fights for. Units on different teams are hostile to each other.</summary>
    public enum Team
    {
        /// <summary>Units controlled by the player.</summary>
        Player,
        /// <summary>Units controlled by the AI.</summary>
        Enemy,
    }
}
