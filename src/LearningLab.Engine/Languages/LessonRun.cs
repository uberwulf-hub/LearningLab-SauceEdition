// Learning Lab - Languages: going through one lesson's exercises. Each is answered once; the ones answered wrong come
// back once at the end. Points: one for each exercise right the first time, five more for finishing. The score (0-100)
// is the share right the first time. Finish() marks the lesson's new items as met, grades every item the exercises
// practised (right only when every exercise on it was right the first time) and saves the lesson's result.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LearningLab.Languages
{
    public sealed class LessonRun
    {
        public const int FinishPoints = 5;
        public readonly LangLesson Lesson;
        readonly List<LangExercise> queue;
        readonly HashSet<LangExercise> missed = new HashSet<LangExercise>();
        readonly Dictionary<LangExercise, bool> first = new Dictionary<LangExercise, bool>();
        int at;
        bool redoAdded;

        public LessonRun(LangLesson l)
        {
            Lesson = l;
            queue = new List<LangExercise>(l.Exercises);
        }

        public LangExercise Current => at < queue.Count ? queue[at] : null;
        public int Index => at;
        public int Count => queue.Count;
        public bool Done => at >= queue.Count;
        /// <summary>True while going through the ones that came back.</summary>
        public bool Redo => at >= Lesson.Exercises.Count;
        public bool Answered { get; private set; }
        public int FirstRight => first.Count(kv => kv.Value);
        public int Score => Lesson.Exercises.Count == 0 ? 100 : (int)Math.Round(100.0 * FirstRight / Lesson.Exercises.Count);
        public int Points => FirstRight + (Done ? FinishPoints : 0);
        public IReadOnlyCollection<LangExercise> Missed => missed;

        /// <summary>Records the answer to the current exercise.</summary>
        public void Answer(bool right)
        {
            if (Done || Answered) return;
            var e = queue[at];
            if (!first.ContainsKey(e)) first[e] = right;
            if (!right) missed.Add(e);
            Answered = true;
        }

        /// <summary>On to the next exercise; when the lesson's own are done, the ones answered wrong come back once.</summary>
        public void Next()
        {
            if (Done || !Answered) return;
            Answered = false;
            at++;
            if (at == queue.Count && !redoAdded)
            {
                redoAdded = true;
                queue.AddRange(Lesson.Exercises.Where(x => first.TryGetValue(x, out var ok) && !ok));
            }
        }

        bool met;
        /// <summary>Marks the lesson's new items as met (once, when they are shown).</summary>
        public void MeetNew(LangCourse c, LangProgress p)
        {
            if (met) return;
            met = true;
            foreach (var it in LangContent.NewItems(c, Lesson)) p.Meet(c.Language, it.Id);
        }

        /// <summary>Saves the results: items met and graded, the lesson's best, the points of the day.</summary>
        public void Finish(LangCourse c, LangProgress p)
        {
            string lang = c.Language;
            var byText = new Dictionary<string, LangItem>();
            foreach (var it in c.AllItems) { var k = Answers.Normalize(it.Target, true); if (!byText.ContainsKey(k)) byText[k] = it; }
            var graded = new Dictionary<LangItem, bool>();
            foreach (var kv in first)
                foreach (var t in kv.Key.SpanishTexts())
                {
                    LangItem it;
                    if (t == null || !byText.TryGetValue(Answers.Normalize(t, true), out it)) continue;
                    graded[it] = graded.TryGetValue(it, out var was) ? was && kv.Value : kv.Value;
                }
            MeetNew(c, p);
            foreach (var kv in graded) p.Grade(lang, kv.Key.Id, kv.Value);
            p.FinishLesson(lang, Lesson.Id, Score);
            p.AddPoints(lang, FirstRight + FinishPoints);
        }
    }
}
