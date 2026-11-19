// Learning Lab - Languages: the verb trainer's questions. A question is a verb, a tense and a person ("nosotros · comer
// · preterite"); the answer comes from the Conjugator. Weather verbs and verbs like gustar are only asked in the persons
// they are used in (LangVerb.Persons). Typed answers forgive a missing accent with a note, except when the form without
// it is another form of the same verb (hablo is not habló).
using System;
using System.Collections.Generic;
using System.Linq;

namespace LearningLab.Languages
{
    public sealed class VerbQuestion
    {
        public LangVerb Verb;
        public Tense Tense;
        public int Person;
        public VerbForm Answer;
        public string Prompt => Conjugator.Prompt(Verb, Tense, Person);
    }

    public sealed class VerbQuiz
    {
        readonly List<LangVerb> verbs;
        readonly List<Tense> tenses;
        readonly Random rnd;
        VerbQuestion last;

        public VerbQuiz(IEnumerable<LangVerb> verbs, IEnumerable<Tense> tenses, Random rnd)
        {
            this.verbs = verbs.ToList();
            this.tenses = tenses.ToList();
            this.rnd = rnd;
        }

        public int VerbCount => verbs.Count;

        /// <summary>A new question (never the same one twice in a row); null when no verb has the chosen tenses.</summary>
        public VerbQuestion Next()
        {
            var pool = verbs.SelectMany(v => tenses.Where(t => v.Tenses.Contains(t) || t == Tense.NearFuture).Select(t => (v, t))).ToList();
            if (pool.Count == 0) return null;
            for (int tries = 0; tries < 20; tries++)
            {
                var (v, t) = pool[rnd.Next(pool.Count)];
                int p = v.Persons[rnd.Next(v.Persons.Length)];
                var q = new VerbQuestion { Verb = v, Tense = t, Person = p, Answer = Conjugator.Form(v, t, p) };
                if (last != null && last.Verb == v && last.Tense == t && last.Person == p && pool.Count * v.Persons.Length > 1) continue;
                return last = q;
            }
            return last;
        }

        /// <summary>Four choices: the answer, other persons' forms and the other tense's form, all different.</summary>
        public static List<string> Choices(VerbQuestion q, Random rnd)
        {
            var set = new List<string> { q.Answer.Text };
            var others = new List<string>();
            for (int p = 0; p < 5; p++) if (p != q.Person) others.Add(Conjugator.Form(q.Verb, q.Tense, p).Text);
            foreach (var t in Conjugator.AllTenses) if (t != q.Tense) others.Add(Conjugator.Form(q.Verb, t, q.Person).Text);
            foreach (var o in others.OrderBy(_ => rnd.Next()))
            {
                if (set.Count >= 4) break;
                if (!string.IsNullOrEmpty(o) && !set.Any(s => Answers.Same(s, o))) set.Add(o);
            }
            for (int i = set.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var x = set[i]; set[i] = set[j]; set[j] = x; }
            return set;
        }

        /// <summary>Checks a typed form.</summary>
        public static AnswerResult Check(VerbQuestion q, string given)
        {
            var r = Answers.CheckTyped(given, q.Answer.Text);
            if (!r.Right || r.Note == null) return r;
            // forgiven an accent: not when what was typed is another form of this verb
            string g = Answers.Normalize(given, true);
            foreach (var t in Conjugator.AllTenses)
                for (int p = 0; p < 5; p++)
                {
                    var f = Conjugator.Form(q.Verb, t, p).Text;
                    if (f != null && Answers.Normalize(f, true) == g && !Answers.Same(f, q.Answer.Text))
                        return new AnswerResult { Right = false, Expected = q.Answer.Text, Note = "\"" + f + "\" is " + Conjugator.Persons[p] + " in the " + Conjugator.TenseName(t) + ". The accent matters here: " + q.Answer.Text };
                }
            return r;
        }

        /// <summary>Why a form isn't regular, in plain words (null for a regular form).</summary>
        public static string Why(VerbForm f)
        {
            if (f.Irregular) return "Irregular: this form has to be learned by heart.";
            if (f.Change != null) return "Follows a pattern: " + f.Change + ".";
            return null;
        }
    }
}
