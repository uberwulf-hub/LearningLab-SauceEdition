// Cert Lab - checks of the AZ-800 network's file server (NTFS and share permissions, DFS), storage (Storage Spaces,
// Data Deduplication), Hyper-V and containers, and the Azure side (Arc, Azure Network Adapter, sync, Azure File Sync).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        static void WsFileTests()
        {
            var w = new W();
            w.Yes("access(user=bcole, path=\"\\\\FS1\\Sales\") == Modify");
            w.Yes("access(user=ewalsh, path=\"\\\\FS1\\Sales\") == \"No access\"");
            w.Yes("access(user=ewalsh, path=\"\\\\FS1\\Finance\") == \"Read and execute\"");
            w.Yes("access(user=ewalsh, path=\"\\\\FS1\\Finance\").ntfs == Modify");
            w.Yes("access(user=ewalsh, path=\"FS1:D:\\Shares\\Finance\") == Modify");
            w.Yes("access(user=gkim, path=\"\\\\FS1\\Public\\Phone list.xlsx\").rights has Write");
            w.Yes("access(user=arivera, path=\"\\\\FS1\\IT\") == \"Full control\"");
            w.Do("share grant server=FS1 Finance account=Everyone right=Change");
            w.Yes("access(user=ewalsh, path=\"\\\\FS1\\Finance\") == Modify");
            // an explicit deny on a subfolder beats the inherited allow
            w.Ps("FS1", "New-Item D:\\Shares\\Sales\\Contracts -ItemType Directory | Out-Null");
            w.Do("ntfs deny server=FS1 path=D:\\Shares\\Sales\\Contracts account=CORP\\dpatel rights=Write");
            w.Yes("access(user=dpatel, path=\"\\\\FS1\\Sales\\Contracts\").rights !has Write");
            w.Yes("access(user=dpatel, path=\"\\\\FS1\\Sales\\Contracts\").rights has Read");
            w.Yes("access(user=bcole, path=\"\\\\FS1\\Sales\\Contracts\") == Modify");
            // an explicit allow on a child beats an inherited deny from the parent
            w.Do("ntfs deny server=FS1 path=D:\\Shares\\Public account=CORP\\knovak rights=Write");
            w.Ps("FS1", "New-Item D:\\Shares\\Public\\Recruiting -ItemType Directory | Out-Null");
            w.Do("ntfs grant server=FS1 path=D:\\Shares\\Public\\Recruiting account=CORP\\knovak rights=Modify");
            w.Yes("access(user=knovak, path=\"\\\\FS1\\Public\\Recruiting\") == Modify");
            w.Yes("access(user=knovak, path=\"\\\\FS1\\Public\") == \"Read and execute, delete\"");
            w.Yes("access(user=knovak, path=\"\\\\FS1\\Public\").rights !has Write");
            // inheritance off: convert or remove
            w.Do("ntfs inheritance server=FS1 path=D:\\Shares\\Public\\Recruiting remove");
            w.Yes("ntfs(FS1, \"D:\\Shares\\Public\\Recruiting\").protected == Yes");
            w.Yes("ntfs(FS1, \"D:\\Shares\\Public\\Recruiting\").inherited !has \"CORP\\Domain Users:M\"");
            w.Yes("access(user=gkim, path=\"\\\\FS1\\Public\\Recruiting\") == \"No access\"");
            w.Do("share new server=FS1 name=Recruiting path=D:\\Shares\\Public\\Recruiting access=\"CORP\\HR:Change\" abe=yes");
            w.Yes("share[\"FS1\\Recruiting\"].abe == Yes");
            w.Bad("share new server=FS1 name=Nowhere path=D:\\Nope", "doesn't exist");
            // DFS Namespaces: a folder with targets in two sites
            w.Bad("dfsn root path=\\\\corp.example\\Files target=\\\\FS1\\Files", "share");
            w.Ps("FS1", "New-Item D:\\Shares\\Root -ItemType Directory | Out-Null");
            w.Do("share new server=FS1 name=Files path=D:\\Shares\\Root access=Everyone:Read");
            w.Bad("dfsn root path=\\\\corp.example\\Files target=\\\\FS1\\Files", "DFS Namespaces");
            w.Do("feature install FS1 FS-DFS-Namespace");
            w.Do("dfsn root path=\\\\corp.example\\Files target=\\\\FS1\\Files");
            w.Do("dfsn folder path=\\\\corp.example\\Files\\Sales target=\\\\FS1\\Sales");
            w.Yes("namespace[\"\\\\corp.example\\Files\"].folders has Sales");
            w.Yes("dfsresolve(\"\\\\corp.example\\Files\\Sales\\Customers.csv\") == \"\\\\FS1\\Sales\\Customers.csv\"");
            w.Yes("access(user=bcole, path=\"\\\\corp.example\\Files\\Sales\") == Modify");
            var abe = w.Ps("FS1", "Set-DfsnRoot -Path \\\\corp.example\\Files -EnableAccessBasedEnumeration $true | Out-Null; (Get-DfsnRoot \\\\corp.example\\Files).Properties");
            Check("Set-DfsnRoot turns on access-based enumeration", abe.Contains("AccessBased"), abe);
            w.Dispose();
        }

        static void WsStorageTests()
        {
            var w = new W();
            w.Do("storage pool server=FS1 name=Pool1 disks=3,4,5,6");
            w.Bad("storage pool server=FS1 name=Pool2 disks=1", "can't be pooled");
            w.Bad("storage vdisk server=FS1 pool=Pool1 name=Triple resiliency=Mirror copies=3 sizegb=10", "5 physical disks");
            w.Do("storage vdisk server=FS1 pool=Pool1 name=Mirror1 resiliency=Mirror sizegb=100 letter=F fs=ReFS label=Projects");
            w.Yes("vdisk[\"Mirror1\"].copies == 2");
            w.Yes("volume(FS1, F).fs == ReFS");
            w.Bad("storage vdisk server=FS1 pool=Pool1 name=Big resiliency=Simple sizegb=250", "Not enough");
            w.Do("storage vdisk server=FS1 pool=Pool1 name=Thin1 provisioning=Thin sizegb=500");
            w.Do("storage vdisk server=FS1 pool=Pool1 name=Par1 resiliency=Parity sizegb=50");
            Check("FS1 sees F: as a drive", w.Ps("FS1", "Test-Path F:\\").Trim() == "True", w.Ps("FS1", "Test-Path F:\\"));
            w.Bad("storage dedup server=FS1 E:", "isn't installed");
            w.Do("feature install FS1 FS-Data-Deduplication");
            w.Bad("storage dedup server=FS1 C:", "system");
            w.Do("storage dedup server=FS1 E: usage=Default");
            w.Do("storage optimize server=FS1 E:");
            w.Yes("dedup(FS1, E).savedgb > 10");
            var rate = w.Ps("FS1", "(Get-DedupVolume E:).SavingsRate");
            Check("Get-DedupVolume shows a savings rate", rate.Trim() != "0" && rate.Trim().Length > 0, rate);
            w.Dispose();
        }

        static void WsHyperVTests()
        {
            var w = new W();
            w.Yes("vm[\"APP-OLD\"].state == Running");
            w.Yes("vm[\"LAB-HOST\"].dynamic == Yes");
            w.Bad("vm set LAB-HOST nested=yes", "dynamic memory");
            w.Do("vm set LAB-HOST dynamic=no");
            w.Do("vm set LAB-HOST nested=yes spoofing=on");
            w.Do("vm start LAB-HOST");
            w.Bad("vm set LAB-HOST nested=no", "turned off");
            w.Yes("vm[\"LAB-HOST\"].nested == Yes");
            w.Yes("vm[\"LAB-HOST\"].spoofing == On");
            w.Do("vm checkpoint LAB-HOST \"Before Hyper-V\"");
            w.Do("vm stop LAB-HOST turnoff=yes");
            w.Do("vm set LAB-HOST cpus=4");
            w.Do("vm restore LAB-HOST \"Before Hyper-V\"");
            w.Yes("vm[\"LAB-HOST\"].cpus == 2");
            w.Yes("vm[\"LAB-HOST\"].state == Off");
            w.Bad("vswitch new name=Second adapter=\"Ethernet 2\"", "already bound");
            w.Do("vswitch new name=Lab-Internal type=Internal");
            w.Do("vswitch new name=Isolated type=Private");
            w.Do("vm new name=CORE-TEST generation=2 memorygb=2 disksizegb=40 switch=Isolated");
            w.Yes("vm[\"CORE-TEST\"].generation == 2");
            w.Yes("vhd[\"D:\\Hyper-V\\Virtual Hard Disks\\CORE-TEST.vhdx\"].type == Dynamic");
            w.Do("vm new name=BIG generation=2 memorygb=60");
            w.Bad("vm start BIG", "not enough memory");
            string o = w.Ps("HV1", "docker run -d --name web -p 8080:80 mcr.microsoft.com/windows/servercore/iis:windowsservercore-ltsc2022");
            w.Yes("container[\"web\"].isolation == process");
            o = w.Ps("HV1", "docker run --name old mcr.microsoft.com/windows/servercore:ltsc2019 cmd /c ver");
            Check("an ltsc2019 image can't use process isolation on Server 2022", o.Contains("does not match the host"), o);
            o = w.Ps("HV1", "docker run --isolation=hyperv --name old mcr.microsoft.com/windows/servercore:ltsc2019 cmd /c ver");
            w.Yes("container[\"old\"].isolation == hyperv");
            w.Dispose();
        }

        static void WsHybridTests()
        {
            var w = new W();
            w.Yes("arc[\"SRV1\"].status == \"Not onboarded\"");
            w.Do("arc onboard SRV1 rg=rg-hybrid");
            w.Yes("arc[\"SRV1\"].status == Connected");
            w.Ps("SRV1", "Stop-Service himds");
            w.Yes("arc[\"SRV1\"].status == Disconnected");
            w.Ps("SRV1", "Start-Service himds");
            w.Bad("update install SRV1", "Assess");
            w.Do("update assess SRV1");
            w.Do("update install SRV1 classes=Security");
            w.Yes("updates(SRV1).missing !has KB5055526");
            w.Yes("hotfix(SRV1) has KB5055526");
            // Azure Network Adapter: the gateway takes about 25 minutes
            w.Bad("azure netadapter SRV1", "Register");
            w.Do("wac register");
            w.Do("azure netadapter SRV1");
            w.Yes("azure.reach[\"SRV1\"] == No");
            w.Wait(26 * 60);
            w.Yes("azure.reach[\"SRV1\"] == Yes");
            Check("SRV1 now reaches an Azure VM's address", w.L.Delivers(w.L.M("SRV1"), "10.50.1.4"));
            Check("FS1 doesn't", !w.L.Delivers(w.L.M("FS1"), "10.50.1.4"));
            var tnc = w.Ps("SRV1", "(Test-NetConnection 10.50.1.4 -Port 3389).TcpTestSucceeded");
            Check("Test-NetConnection reaches the Azure VM over the adapter", tnc.Trim() == "True", tnc);
            // Microsoft Entra Connect Sync: UPN suffixes, OU filtering, PHS and PTA
            w.Do("connect install server=SRV1 signin=PHS ous=\"OU=Staff,DC=corp,DC=example\"");
            w.Yes("cloud.user[\"bcole\"].upn == bcole@harborline.onmicrosoft.example");
            w.Yes("cloud.users !has svc-backup");
            w.Do("upnsuffix add harborline.example");
            w.Do("set user bcole upn=bcole@harborline.example");
            w.Wait(31 * 60);
            w.Yes("cloud.user[\"bcole\"].upn == bcole@harborline.example");
            w.Yes("signin(user=bcole) == Allowed");
            w.Do("password bcole \"N3w#Harbor2026\"");
            w.Yes("signin(user=bcole, password=\"N3w#Harbor2026\") == \"Wrong password\"");
            w.Wait(3 * 60);
            w.Yes("signin(user=bcole, password=\"N3w#Harbor2026\") == Allowed");
            // Azure File Sync with cloud tiering
            w.Do("filesync service name=sss-harbor");
            w.Do("filesync register FS1");
            w.Do("filesync group name=SalesArchive account=stharborfiles share=salesarchive");
            w.Bad("filesync endpoint group=SalesArchive server=FS1 path=C:\\Data", "system volume");
            w.Do("filesync endpoint group=SalesArchive server=FS1 path=E:\\Archive\\Sales tiering=yes days=60");
            w.Yes("filesync[\"SalesArchive\"].cloudfiles has \"2023\\Report Q1.pdf\"");
            w.Yes("file(FS1, \"E:\\Archive\\Sales\\2023\\Report Q1.pdf\").tiered == Yes");
            w.Dispose();
        }
    }
}
