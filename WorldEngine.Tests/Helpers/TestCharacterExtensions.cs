using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;

namespace WorldEngine.Tests.Helpers;

/// <summary>
/// M12 12.2: IdentityData.CivId was replaced by Tier1Character.Memberships. Most test fixtures
/// only need a character to report a given CivId (equality/validity checks), not a fully wired
/// Organization — this gives them the old one-liner back without constructing an Organization
/// they don't need. Tests that DO need the Organization side (alliance/succession behavior)
/// should go through CivTracker.SetCharacterCiv instead, same as production code.
/// </summary>
public static class TestCharacterExtensions
{
    public static Tier1Character WithCiv(this Tier1Character c, CivId civId, OrganizationRole role = OrganizationRole.Member)
    {
        c.Memberships.Add(new Membership(OrganizationId.None, role, 1f, civId));
        return c;
    }
}
