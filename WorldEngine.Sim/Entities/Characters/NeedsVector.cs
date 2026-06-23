namespace WorldEngine.Sim.Entities.Characters;

/// <summary>7 dynamic needs. Decay per season, restored by actions and environment.</summary>
public record struct NeedsVector(
    float Safety, float Food, float Shelter,
    float Belonging, float Status, float Purpose, float Spiritual)
{
    public const float UrgentThreshold = 0.25f;

    public static NeedsVector Default => new(0.8f, 0.8f, 0.7f, 0.7f, 0.5f, 0.5f, 0.6f);

    /// <summary>The most-urgent unmet need (lowest value below UrgentThreshold), or null.</summary>
    public string? MostUrgentUnmet()
    {
        string? name = null;
        float worst = UrgentThreshold;
        if (Safety    < worst) { worst = Safety;    name = nameof(Safety); }
        if (Food      < worst) { worst = Food;      name = nameof(Food); }
        if (Shelter   < worst) { worst = Shelter;   name = nameof(Shelter); }
        if (Belonging < worst) { worst = Belonging; name = nameof(Belonging); }
        if (Status    < worst) { worst = Status;    name = nameof(Status); }
        if (Purpose   < worst) { worst = Purpose;   name = nameof(Purpose); }
        if (Spiritual < worst) { worst = Spiritual; name = nameof(Spiritual); }
        return name;
    }
}
