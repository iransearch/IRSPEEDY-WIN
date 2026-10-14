using IRSpeedyVPN.Services.Libcore;
using System.Linq;

namespace IRSpeedyVPN.Services
{
    internal static class MainPoolRecoveryProof
    {
        internal static bool Confirmed(QueryAutoSelectorsResponse pools, long sinceMs)
        {
            var main = pools?.Groups.FirstOrDefault(group => group.Tag == "proxy");
            return main != null && !main.Suspended && main.MembersQualified > 0
                && main.Members.Any(member => member.Qualified && member.LastProbeMs >= sinceMs
                    && member.LastProbeMs > 0 && member.LastOkMs == member.LastProbeMs);
        }
    }
}
