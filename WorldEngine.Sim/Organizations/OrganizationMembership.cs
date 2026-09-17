using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Sim.Organizations;

/// <summary>
/// The one place that mutates the two-sided membership invariant —
/// <see cref="Tier1Character.Memberships"/> and <see cref="Organization.Members"/> must always
/// agree. M12 built <c>CivTracker.SetCharacterCiv</c> for the civ case; M13–M15 then added nine
/// ad-hoc join/leave/loyalty sites that updated both lists by hand. M15.9 routes them all here.
/// </summary>
/// <remarks>
/// <c>SetLoyalty</c> rebuilds the membership record at its existing index rather than
/// removing and re-appending it. Ordering in <c>Memberships</c> is load-bearing:
/// <see cref="Tier1Character.CivId"/> returns the first civ-valid entry, and several lookups take
/// the first membership of a given kind. The old remove-then-add pattern silently reordered the
/// list on every loyalty change.
/// </remarks>
public static class OrganizationMembership
{
    /// <summary>
    /// Adds <paramref name="c"/> to <paramref name="org"/>, writing both sides. Returns the new
    /// membership record.
    /// </summary>
    public static Membership Join(
        Tier1Character c,
        Organization org,
        OrganizationRole role,
        float loyalty,
        CivId civId = default)
    {
        var membership = new Membership(org.Id, role, loyalty, civId);
        c.Memberships.Add(membership);
        org.Members[c.Id] = membership;
        return membership;
    }

    /// <summary>
    /// Records a Tier2 character in <paramref name="org"/>'s roster. Only the organization side
    /// exists for Tier2: <see cref="Tier2Character"/> carries no <c>Memberships</c> list (org
    /// affiliation is a Tier1 concern — see Tier1Character.Memberships), so there is no second
    /// side to keep in sync here.
    /// </summary>
    public static Membership JoinTier2(
        Tier2Character c,
        Organization org,
        OrganizationRole role,
        float loyalty)
    {
        var membership = new Membership(org.Id, role, loyalty);
        org.Members[c.Id] = membership;
        return membership;
    }

    /// <summary>Removes <paramref name="c"/> from <paramref name="org"/>, clearing both sides.</summary>
    public static void Leave(Tier1Character c, Organization org)
    {
        c.Memberships.RemoveAll(m => m.OrganizationId == org.Id);
        org.Members.Remove(c.Id);
    }

    /// <summary>
    /// Sets <paramref name="c"/>'s Loyalty within <paramref name="orgId"/> on both sides,
    /// preserving the membership's position in <c>Memberships</c> (see the type remarks).
    /// <paramref name="org"/> may be null when the Organization record is not present — the
    /// character side is still updated. Returns the updated record, or null if the character has
    /// no membership in that organization.
    /// </summary>
    public static Membership? SetLoyalty(
        Tier1Character c, OrganizationId orgId, float newLoyalty, Organization? org)
    {
        int idx = c.Memberships.FindIndex(m => m.OrganizationId == orgId);
        if (idx < 0) return null;

        var updated = c.Memberships[idx] with { Loyalty = newLoyalty };
        c.Memberships[idx] = updated;
        if (org != null) org.Members[c.Id] = updated;
        return updated;
    }

    /// <summary>Convenience overload of <c>SetLoyalty</c> for a known Organization.</summary>
    public static Membership? SetLoyalty(Tier1Character c, Organization org, float newLoyalty)
        => SetLoyalty(c, org.Id, newLoyalty, org);
}
