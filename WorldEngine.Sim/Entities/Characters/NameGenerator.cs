using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Deterministic syllable-assembly name generator seeded via WorldRng. Same world seed + same
/// (seq, salt) produce identical names (M15.x namespace expansion — replaces the old flat
/// first-name-list lookup, whose ~50-name pool per ancestry visibly repeated once population
/// climbed into the thousands over millennia-long runs).
///
/// Given names assemble as onset + [middle, ~40% of rolls] + coda. Surnames assemble as
/// surname_onset + surname_coda. With N onsets, M middles, and K codas this yields roughly
/// N*K + 0.4*N*M*K combinations per ancestry — tens of thousands rather than a few dozen —
/// while staying inside each ancestry's own phonetic pool so the ancestry "feel" (elvish,
/// dwarvish, orcish, ...) is preserved.
/// </summary>
public static class NameGenerator
{
    private const float MiddleChance = 0.4f;

    /// <summary>Generates a given name from an ancestry's syllable pools (falls back to <paramref name="fallback"/> if the ancestry has none).</summary>
    public static string GenerateGivenName(AncestryConfig ancestry, CharacterNamesConfig fallback, int worldSeed, int seq, int salt)
    {
        var onsets  = ancestry.NameOnsets.Length > 0 ? ancestry.NameOnsets  : fallback.NameOnsets;
        var middles = ancestry.NameOnsets.Length > 0 ? ancestry.NameMiddles : fallback.NameMiddles;
        var codas   = ancestry.NameOnsets.Length > 0 ? ancestry.NameCodas   : fallback.NameCodas;
        return AssembleGivenName(onsets, middles, codas, worldSeed, seq, salt);
    }

    /// <summary>Generates a given name directly from syllable pools (ancestry-agnostic callers, e.g. Tier 2 specialists).</summary>
    public static string AssembleGivenName(string[] onsets, string[] middles, string[] codas, int worldSeed, int seq, int salt)
    {
        if (onsets.Length == 0 || codas.Length == 0) return "Unknown";

        string onset = Pick(onsets, worldSeed, seq, salt);
        string coda  = Pick(codas, worldSeed, seq, salt + 2);

        string middle = "";
        if (middles.Length > 0 && WorldRng.FloatAt(worldSeed, 0, seq, 1, salt + 1) < MiddleChance)
            middle = Pick(middles, worldSeed, seq, salt + 1);

        return Capitalize(onset + middle + coda);
    }

    /// <summary>Generates a fresh surname (house/clan/family name) from an ancestry's surname syllable pools.</summary>
    public static string GenerateSurname(AncestryConfig ancestry, CharacterNamesConfig fallback, int worldSeed, int seq, int salt)
    {
        var onsets = ancestry.SurnameOnsets.Length > 0 ? ancestry.SurnameOnsets : fallback.SurnameOnsets;
        var codas  = ancestry.SurnameCodas.Length  > 0 ? ancestry.SurnameCodas  : fallback.SurnameCodas;
        if (onsets.Length == 0 || codas.Length == 0) return "";

        string onset = Pick(onsets, worldSeed, seq, salt);
        string coda  = Pick(codas, worldSeed, seq, salt + 1);
        return Capitalize(onset) + coda;
    }

    private static string Pick(string[] pool, int worldSeed, int seq, int salt)
    {
        int idx = (int)(WorldRng.FloatAt(worldSeed, 0, seq, 0, salt) * pool.Length);
        return pool[Math.Clamp(idx, 0, pool.Length - 1)];
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
