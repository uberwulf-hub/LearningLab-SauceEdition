// Cert Lab - who may manage what in the pretend tenant: Microsoft Entra roles (Global Administrator and Intune
// Administrator manage everything) and Intune role assignments (a role, the admin groups who get it, the scope groups
// they manage, and scope tags that decide which objects they see).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public partial class EpTenant
    {
        static readonly string[] ManageRoles = { "Help Desk Operator", "Policy and Profile manager", "School Administrator", "Endpoint Security Manager" };

        public bool HasEntraRole(EpObj user, string role) { return user != null && user.List("roles").Any(r => EpText.Eq(r, role)); }

        /// <summary>Can this admin run remote actions on the device?</summary>
        public bool CanManage(EpObj user, EpDevice d)
        {
            if (user == null || d == null) return false;
            if (HasEntraRole(user, "Global Administrator") || HasEntraRole(user, "Intune Administrator")) return true;
            foreach (var ra in Of("roleAssignment"))
            {
                if (!ManageRoles.Any(r => EpText.Eq(r, ra["role"]))) continue;
                if (!ra.List("admins").Any(g => InGroup(g, user))) continue;
                var owner = User(d.PrimaryUser);
                bool inScope = ra.List("scopeGroups").Any(g => InGroup(g, d) || owner != null && InGroup(g, owner));
                if (!inScope) continue;
                if (TagsMatch(ra, d)) return true;
            }
            return false;
        }
        /// <summary>Can the admin see the object (scope tags)? Objects without tags have Default.</summary>
        public bool CanSee(EpObj user, EpObj o)
        {
            if (user == null) return false;
            if (HasEntraRole(user, "Global Administrator") || HasEntraRole(user, "Intune Administrator") || HasEntraRole(user, "Global Reader")) return true;
            return Of("roleAssignment").Any(ra => ra.List("admins").Any(g => InGroup(g, user)) && TagsMatch(ra, o));
        }
        static bool TagsMatch(EpObj ra, EpObj o)
        {
            var mine = ra.List("scopeTags"); if (mine.Count == 0) mine.Add("Default");
            var theirs = o.List("scopeTags"); if (theirs.Count == 0) theirs.Add("Default");
            return mine.Any(m => theirs.Any(x => EpText.Eq(x, m)));
        }
    }
}
