using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Organizations;

/// <summary>
/// One character's stake in an Organization. Loyalty is a continuous value analogous to
/// RelationshipEdge.Trust; when a character belongs to multiple Organizations whose interests
/// conflict, goal/utility scoring weighs by whichever Organization has the higher Loyalty stake
/// (see roadmap M12 design decision 2 — the actual weighted-scoring logic lands with M13).
/// </summary>
public sealed record Membership(OrganizationId OrganizationId, OrganizationRole Role, float Loyalty)
{
    public static Membership Founding(OrganizationId orgId) => new(orgId, OrganizationRole.Leader, 1.0f);
}
