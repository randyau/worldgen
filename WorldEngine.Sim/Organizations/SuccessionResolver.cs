using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Organizations;

/// <summary>
/// M12 12.3: the reusable "leadership succession" kernel — a vacant seat, a pool of eligible
/// heirs, a scoring function — generalized off Civilization onto Organization.Members/LeaderId so
/// it can back civ rulers (today, via CharacterBehaviorPhase.KillCharacter), and from M13-M15
/// family heads, guild/merchant-house heads, and religious leaders, without three more bespoke
/// succession-crisis implementations. See docs/phases/m12_organization_model.md.
/// </summary>
public static class SuccessionResolver
{
    /// <summary>
    /// Picks the highest-scoring living, age-eligible member of an Organization to fill a vacant
    /// leader seat. Does not mutate anything — callers apply the result (LeaderId, seat-specific
    /// bookkeeping like RulerOrdinal/crisis events) themselves, since that bookkeeping differs per
    /// Organization Kind.
    /// </summary>
    public static EntityId? SelectSuccessor(
        Organization org, WorldState world, int minAgeSeasons, Func<Tier1Character, float> score)
    {
        EntityId? best = null;
        float bestScore = float.MinValue;
        foreach (var memberId in org.Members.Keys)
        {
            if (world.GetEntity(memberId) is not Tier1Character member || !member.IsAlive) continue;
            if (member.AgeSeason < minAgeSeasons) continue; // no infant/toddler monarchs
            float candidateScore = score(member);
            if (candidateScore > bestScore) { bestScore = candidateScore; best = memberId; }
        }
        return best;
    }
}
