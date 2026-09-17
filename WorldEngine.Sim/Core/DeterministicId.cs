namespace WorldEngine.Sim.Core;

/// <summary>
/// Identifies every call site that derives an EntityId from a deterministic formula (tick/year +
/// position, etc.) rather than the sequential <see cref="IdGenerator"/> counter — needed so a
/// world regenerated from the same seed gets byte-identical entity IDs. See
/// <see cref="DeterministicId.Seq"/> for why each site needs a distinct tag.
/// </summary>
public enum DeterministicIdSystem : byte
{
    CivFloorSpawn           = 1,  // CivTracker.Diplomacy.RunCivFloorSpawns
    UnrestSecessionLeader   = 2,  // CivTracker.Unrest.RunUnrestAndSecession
    AuthoringSpawn          = 3,  // AuthoringResolver.ResolveSpawn (God Mode)
    FamilyBirth             = 4,  // CharacterBehaviorPhase.TrySpawnFamilyBirths
    CivBorn                 = 5,  // CharacterBehaviorPhase.TrySpawnCivBorn
    LeaderlessResurrection  = 6,  // CharacterBehaviorPhase.UpdateLifecycle
    Crystallization         = 7,  // PopulationDynamicsPhase.TryCrystallize (Tier2 specialists)
    BeastEmergence          = 8,  // EntityBehaviorPhase.ProcessEmergenceSchedule
    BeastReproduction       = 9,  // EntityBehaviorPhase.Reproduce
}

/// <summary>
/// Collision-free deterministic EntityId derivation. Before M15.9 each spawn site added its own
/// small additive "base offset" (400_000, 9_000_000, ...) to a tick/year+position hash to keep
/// systems apart — but the offset doesn't bound the hash's growth, so two sites can still collide
/// once ticks/years get large, and two sites did in fact reuse the same 400_000 offset with
/// different multiplier schemes (CivTracker.Unrest's secession-leader spawn and
/// PopulationDynamicsPhase's Tier2 crystallization) — a real collision bug fixed in the
/// 2026-09-17 review pass. Folding a per-system tag into the top byte instead makes collision
/// structurally impossible: two different systems can never produce the same value regardless of
/// how large their hash grows, since consumers only ever read the low 31 bits (see
/// CharacterFactory.Spawn's entitySeq-masking) for WorldRng draws, so the tag never perturbs
/// simulation randomness — it only changes the raw ID number.
/// </summary>
public static class DeterministicId
{
    public static long Seq(DeterministicIdSystem system, long hash) =>
        ((long)system << 56) | (hash & 0x00FF_FFFF_FFFF_FFFFL);
}
