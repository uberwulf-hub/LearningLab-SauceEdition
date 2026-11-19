// Cert Lab - the pretend tenant as every lab starts it: Riverbend Outfitters (riverbend.example, a made-up company),
// its people, groups and devices. Device makers and models are made up too (Lumen Devices), except the phones.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public partial class EpTenant
    {
        public static EpTenant Create(PsNet net)
        {
            EpSchemas.Init();
            var t = new EpTenant { Net = net };
            t.Settings = t.Add(new EpObj { Kind = "tenant", Name = "Tenant settings", Id = "tenant" });
            var s = t.Settings;
            s["mdmScope"] = "All"; s["joinScope"] = "All"; s["lapsEnabled"] = "No"; s["noPolicyMark"] = "Compliant"; s["validityDays"] = "30";
            s["enrollLimit"] = "5"; s["winPersonal"] = "Allow"; s["mdeConnector"] = "Off"; s["windowsData"] = "Off"; s["adConnector"] = "Installed";
            t.SeedUsers();
            t.SeedDevices();
            t.SeedGroups();
            foreach (var o in t.All) { o.Created = o.Modified = t.Now.AddDays(-30); }
            foreach (var d in t.Devices.Where(x => x.Enrolled).ToList()) { t.Sync(d); d.LastSync = t.Now.AddHours(-3); d.NextSync = t.Now.AddHours(5); d.Log.Clear(); }
            return t;
        }

        EpObj U(string alias, string name, string dept, string job, string city, string lic, string mfa, string roles)
        {
            var u = Add(new EpObj { Kind = "user", Name = name });
            u["alias"] = alias; u["upn"] = alias + "@" + Domain; u["department"] = dept; u["jobTitle"] = job; u["city"] = city;
            u["country"] = "United States"; u["usageLocation"] = "US"; u["accountEnabled"] = "Yes"; u["licenses"] = lic; u["mfa"] = mfa; u["roles"] = roles;
            return u;
        }

        void SeedUsers()
        {
            U("alex", "Alex Morgan", "IT", "Endpoint administrator", "Portland", "Microsoft 365 E5", "Registered", "Intune Administrator");
            U("chen", "Chen Wei", "IT", "Help desk technician", "Portland", "Microsoft 365 E3", "Registered", "Helpdesk Administrator");
            U("jordan", "Jordan Lee", "Sales", "Account manager", "Portland", "Microsoft 365 E3", "Registered", "");
            U("priya", "Priya Shah", "Sales", "Sales lead", "Denver", "Microsoft 365 Business Premium", "Registered", "");
            U("sam", "Sam Ortiz", "Finance", "Accountant", "Portland", "Microsoft 365 E3", "Not registered", "");
            U("nils", "Nils Fischer", "Warehouse", "Warehouse lead", "Denver", "Microsoft 365 E3", "Registered", "");
            U("ava", "Ava Brooks", "HR", "HR advisor", "Denver", "Office 365 E3", "Registered", "");
            var bg = U("breakglass", "Emergency Admin", "IT", "Emergency access account", "", "", "Registered", "Global Administrator");
            bg["department"] = "";
        }

        EpDevice D(string name, string os, string ver, string edition, string join, string owner, string user, string model)
        {
            var d = Add(new EpDevice { Kind = "device", Name = name });
            d["os"] = os; d["osVersion"] = ver; d["edition"] = edition; d["join"] = join; d["ownership"] = owner; d["user"] = user;
            d["manufacturer"] = os == "iOS" ? "Apple" : os == "Android" ? "Google" : "Lumen Devices"; d["model"] = model;
            d["managed"] = "Intune"; d["state"] = "running";
            if (os == "Windows")
            {
                d["osType"] = "Windows"; d["tpm"] = "2.0"; d["secureBoot"] = "Yes"; d["codeIntegrity"] = "Yes"; d["bitlocker"] = "Off"; d["firewall"] = "On";
                d["av"] = "On"; d["rtp"] = "On"; d["sigAgeDays"] = "0"; d["arch"] = "x64"; d["win11Capable"] = "Yes"; d["localPassword"] = "Yes";
                d["mde"] = "No"; d["risk"] = ""; d["telemetry"] = "Basic";
            }
            d["serial"] = "LB" + (Math.Abs(FwHash.Of(name)) % 90000 + 10000);
            return d;
        }

        void SeedDevices()
        {
            var a = D("RB-LT-001", "Windows", "10.0.26100.7840", "Enterprise", "Entra joined", "Corporate", "jordan", "Lumen Book 14");
            a["patch"] = "2026-02"; a["featureVersion"] = "24H2"; a["serial"] = "LB14-40117";
            var b = D("RB-LT-002", "Windows", "10.0.26100.7840", "Pro", "Entra joined", "Corporate", "priya", "Lumen Book 13");
            b["patch"] = "2026-02"; b["featureVersion"] = "24H2"; b["serial"] = "LB13-28804";
            var c = D("RB-LT-003", "Windows", "10.0.26100.7840", "Pro", "None", "Corporate", "", "Lumen Book 14");
            c["patch"] = "2026-02"; c["featureVersion"] = "24H2"; c["serial"] = "LB14-55821"; c["managed"] = "None"; c["state"] = "oobe";
            c["note"] = "New laptop, still in the out-of-box experience (OOBE). Its hardware hash is in C:\\Autopilot on your admin PC.";
            var dt = D("RB-DT-010", "Windows", "10.0.19045.6456", "Pro", "Hybrid joined", "Corporate", "sam", "Lumen Tower 5");
            dt["patch"] = "2025-10"; dt["featureVersion"] = "22H2"; dt["serial"] = "LT5-10377"; dt["bitlocker"] = "On"; dt["domain"] = "riverbend.local";
            dt["note"] = "Joined to the on-premises domain riverbend.local and synced to Microsoft Entra ID (hybrid joined); enrolled through Group Policy.";
            var old = D("RB-DT-007", "Windows", "10.0.19045.6456", "Pro", "Entra joined", "Corporate", "nils", "Lumen Tower 3");
            old["patch"] = "2025-10"; old["featureVersion"] = "22H2"; old["serial"] = "LT3-00912"; old["tpm"] = "1.2"; old["win11Capable"] = "No";
            old["note"] = "An older desktop: TPM 1.2, so it can't run Windows 11.";
            var k = D("RB-KIOSK-01", "Windows", "10.0.26100.7840", "Enterprise", "Entra joined", "Corporate", "", "Lumen Kiosk 22");
            k["patch"] = "2026-02"; k["featureVersion"] = "24H2"; k["serial"] = "LK22-00451"; k["enrollmentProfile"] = "Kiosk self-deploying"; k["localPassword"] = "No";
            k["physicalIds"] = "[ZTDid]:LK22-00451\n[OrderID]:Kiosk";
            var home = D("SAM-HOME-PC", "Windows", "10.0.26100.7840", "Home", "Entra registered", "Personal", "sam", "Home-built PC");
            home["patch"] = "2026-02"; home["featureVersion"] = "24H2"; home["managed"] = "None"; home["manufacturer"] = "Unknown"; home["serial"] = "HB-77310";
            home["note"] = "Sam's own PC at home: Sam added the work account (Entra registered); it isn't enrolled in Intune.";
            var ip = D("Jordan's iPhone", "iOS", "17.4", "", "Entra registered", "Personal", "jordan", "iPhone 15");
            ip["osType"] = "iPhone"; ip["jailbroken"] = "No"; ip["passcode"] = "Yes"; ip["serial"] = "F2LX93KQ1P";
            var px = D("Priya's Pixel", "Android", "14", "", "Entra registered", "Personal", "priya", "Pixel 8");
            px["osType"] = "AndroidForWork"; px["rooted"] = "No"; px["passcode"] = "Yes"; px["profile"] = "Work profile"; px["serial"] = "38AX1Z07"; px["integrity"] = "Basic and device";
            foreach (var d in Devices.Where(x => x.Enrolled)) { d.LastSync = Now.AddHours(-3); d.NextSync = Now.AddHours(5); }
        }

        EpObj G(string name, string membership, string rule, params string[] members)
        {
            var g = Add(new EpObj { Kind = "group", Name = name });
            g["type"] = "Security"; g["membership"] = membership; g["rule"] = rule ?? ""; g["members"] = EpText.Join(members);
            return g;
        }

        void SeedGroups()
        {
            G("IT Admins", "Assigned", null, "alex", "chen");
            G("Sales", "Assigned", null, "jordan", "priya");
            G("Finance", "Assigned", null, "sam");
            G("Intune pilot users", "Assigned", null, "alex", "jordan");
            G("Pilot devices", "Assigned", null, "RB-LT-002");
            G("Kiosk devices", "Assigned", null, "RB-KIOSK-01");
            G("Windows devices", "Dynamic device", "(device.deviceOSType -eq \"Windows\")");
            G("Corporate Windows devices", "Dynamic device", "(device.deviceOSType -eq \"Windows\") -and (device.deviceOwnership -eq \"Company\")");
        }
    }
}
