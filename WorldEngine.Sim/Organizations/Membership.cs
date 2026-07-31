using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Organizations;

/// <summary>
/// One character's stake in an Organization. Loyalty is a continuous value analogous to
/// RelationshipEdge.Trust; when a character belongs to multiple Organizations whose interests
/// conflict, goal/utility scoring weighs by whichever Organization has the higher Loyalty stake
/// (see roadmap M12 design decision 2 — the actual weighted-scoring logic lands with M13).
/// </summary>
/// <param name="OrganizationId">The Organization this membership belongs to.</param>
/// <param name="Role">The character's role within the Organization.</param>
/// <param name="Loyalty">Continuous stake weight, analogous to RelationshipEdge.Trust.</param>
/// <param name="CivId">
/// Denormalized convenience copy of the owning Civilization's CivId, set only when the
/// Organization's Kind is Civilization. CivId and OrganizationId are independently-counted ID
/// spaces (see docs/phases/m12_organization_model.md 12.2), and the ~70 existing per-tick reads
/// of "what civ is this character in" (UtilityScorer, GoalManager, CharacterBehaviorPhase, etc.)
/// need an O(1) answer with no WorldState lookup available at some call sites
/// (Tier1Character.ToCharacterSnapshot has none) — so it's carried here instead of reverse-looked-up.
/// </param>
public sealed record Membership(OrganizationId OrganizationId, OrganizationRole Role, float Loyalty, CivId CivId = default);
