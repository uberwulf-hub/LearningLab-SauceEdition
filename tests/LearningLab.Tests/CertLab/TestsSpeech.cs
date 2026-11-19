// Cert Lab - checks for reading aloud (Speech.cs, ReadText.cs, UiRead.cs): sentences, the pronunciation lists, what
// each kind of page reads, the order the sentences are said and lit up, pause / go on / back / forward / stop, the
// keys, and the settings file. A FakeSpeaker stands in for the voice: nothing is ever played.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static void SpeechTests()
        {
            // ---- sentences
            var s = ReadAloud.Sentences("Run `ls -l /etc/fstab.d` first. Then check it. Use e.g. the -h option. The server is 192.168.50.254. Done!");
            Check("speech: sentences split at . ! ? (not in file names, addresses or after e.g.)", s.Count == 5 && s[0] == "Run `ls -l /etc/fstab.d` first." && s[2] == "Use e.g. the -h option." && s[3] == "The server is 192.168.50.254." && s[4] == "Done!", string.Join(" | ", s.ToArray()));
            var s2 = ReadAloud.Sentences("1. Open the file. 2. Save it.");
            Check("speech: a list number at the start stays with its sentence", s2.Count == 2 && s2[0] == "1. Open the file.", string.Join(" | ", s2.ToArray()));
            int ms;
            Check("speech: a pause is a unit of its own, said as silence", ReadAloud.IsPause(ReadAloud.Pause(2000), out ms) && ms == 2000 && !ReadAloud.IsPause("Pause here.", out ms));

            // ---- pronunciation
            var courses = CourseLoader.LoadAll(Path.Combine(AppRootForTests, "Courses"));
            Func<string, CourseInfo> course = id => courses.FirstOrDefault(c => c.Id == id);
            var rh = Pronounce.Load(Path.Combine(course("rhcsa").Dir, "speech.txt"));
            string a1 = rh.Say("Edit /etc/fstab with vim.");
            Check("speech: /etc/fstab is said \"slash etc slash f-s-tab\"", a1 == "Edit slash etc slash f-s-tab with vim.", a1);
            string a2 = rh.Say("Use nmcli and sudo; SELinux must stay enforcing.");
            Check("speech: nmcli spelt, sudo and SELinux said the usual way", a2 == "Use N M C L I and sue doo; S E Linux must stay enforcing.", a2);
            string a3 = rh.Say("Logs are in /var/log/messages, and DNS answers on port 53.");
            Check("speech: any path with slashes and dots, and acronyms spelt", a3 == "Logs are in slash var slash log slash messages, and D N S answers on port 53.", a3);
            Check("speech: plain words and a sentence-final path keep their dot", rh.Say("Look at /etc/hosts.") == "Look at slash etc slash hosts." && rh.Say("The tar command.") == "The tar command.", rh.Say("Look at /etc/hosts."));
            var ps = Pronounce.Load(Path.Combine(course("powershell").Dir, "speech.txt"));
            string a4 = ps.Say("Get-ChildItem lists files; pipe it to Sort-Object and use $_ in Where-Object.");
            Check("speech: PowerShell names split into words", a4 == "Get Child Item lists files; pipe it to Sort Object and use dollar underscore in Where Object.", a4);
            var sec = course("secplus");
            var sp = Pronounce.Load(Path.Combine(sec.Dir, "speech.txt"));
            sp.AddAcronyms(sec.Decks.Where(d => d.Id.StartsWith("acronyms")).SelectMany(d => d.Cards));
            string a5 = sp.Say("Use AES with an HSM; NAT and RADIUS stay words, and 3DES is old. ABAC beats RBAC.");
            Check("speech: the Security+ acronym deck is spelt letter by letter (words stay words)", a5 == "Use A E S with an H S M; nat and radius stay words, and triple D E S is old. A B A C beats R B A C.", a5);
            Check("speech: every course has its list", courses.All(c => File.Exists(Path.Combine(c.Dir, "speech.txt")) && Pronounce.Load(Path.Combine(c.Dir, "speech.txt")).Count > 5), string.Join(", ", courses.Where(c => !File.Exists(Path.Combine(c.Dir, "speech.txt"))).Select(c => c.Id).ToArray()));
        }
    }
}
