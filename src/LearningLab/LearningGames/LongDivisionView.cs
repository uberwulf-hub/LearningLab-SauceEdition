// Learning Games - the Long Division Workshop sheet: the bracket, the digits written so far, the cell to fill next, and
// a coach beside it (Divide, Multiply, Subtract, Bring down; the question for this step; kind words after a mistake).
// Guided mode explains with the numbers and highlights the part being divided; practice mode only names the step.
// Ported from App Hub's Learning Games (Engine\LongDivisionView.cs, WPF) to Avalonia. Wide windows look as in App Hub
// (the sheet on the left, the coach in a 430-wide column on the right). Narrow screens (phones, under WideMin) put the
// coach under the sheet, shrink the sheet to fit the width and show a number pad, so it all works by touch; Enter in
// the answer box still checks. Right and wrong answers play Sound.Correct / Sound.Wrong as in App Hub.
using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using LearningLab.Views;
using Path = Avalonia.Controls.Shapes.Path;

namespace LearningGames
{
    public sealed class LdEventArgs : EventArgs
    {
        public bool Ok, Done;
        public string Kind = "";
    }

    public sealed class LongDivisionView : Border
    {
        public LongDivision Ld;
        public bool Guided = true;
        public event EventHandler<LdEventArgs> Answered;
        public event EventHandler Finished;
        /// <summary>The number pad under the answer box: null = automatic (shown in the narrow layout), true / false = always / never.</summary>
        public bool? ShowKeypad
        {
            get { return showKeypad; }
            set { showKeypad = value; ApplyLayout(); }
        }
        /// <summary>Narrower than this, the coach goes under the sheet.</summary>
        public const double WideMin = 760;

        readonly Grid root = new Grid();
        readonly Canvas sheet = new Canvas();
        readonly LayoutTransformControl sheetHost = new LayoutTransformControl();
        readonly ScrollViewer sheetScroll = new ScrollViewer();
        readonly Border side = new Border();
        readonly StackPanel coach = new StackPanel();
        readonly TextBlock stepNo = new TextBlock(), prompt = new TextBlock(), feedback = new TextBlock(), hint = new TextBlock(), story = new TextBlock();
        readonly Border storyBox = new Border(), hintBox = new Border(), feedbackBox = new Border();
        readonly StackPanel chips = new StackPanel();
        readonly TextBox input = new TextBox();
        readonly Button check, hintBtn, next;
        readonly StackPanel inputRow = new StackPanel();
        readonly UniformGrid keypad = new UniformGrid();
        string lastFeedback = "";
        bool lastOk = true;
        bool? showKeypad;
        bool narrow;
        double scale = 1;

        const double CW = 44, RH = 50, FontSz = 34, SheetMargin = 20;

        public LongDivisionView()
        {
            Background = Visuals.B("#0F1218");
            sheetScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sheetScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            sheet.Margin = new Thickness(SheetMargin);
            // centred in the space left, as WPF placed it in App Hub
            sheetHost.Child = sheet;
            sheetHost.HorizontalAlignment = HorizontalAlignment.Center; sheetHost.VerticalAlignment = VerticalAlignment.Center;
            sheetScroll.Content = sheetHost;
            root.Children.Add(sheetScroll);

            side.Background = Visuals.B("#13161D"); side.BorderBrush = Visuals.B("#262B36");
            ScrollViewer coachScroll = new ScrollViewer(); coachScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            coachScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; coachScroll.Content = coach;
            side.Child = coachScroll;
            root.Children.Add(side);
            Child = root;

            storyBox.Background = Visuals.B("#FFF7DB"); storyBox.BorderBrush = Visuals.B("#E0B84A"); storyBox.BorderThickness = new Thickness(2); storyBox.CornerRadius = new CornerRadius(12);
            storyBox.Padding = new Thickness(14, 10, 14, 10); storyBox.Margin = new Thickness(0, 0, 0, 14);
            story.FontSize = 16; story.Foreground = Visuals.B("#2A2112"); story.TextWrapping = TextWrapping.Wrap; storyBox.Child = story;
            coach.Children.Add(storyBox);

            stepNo.FontSize = 13; stepNo.Foreground = Visuals.B(Visuals.Muted); coach.Children.Add(stepNo);
            chips.Orientation = Orientation.Horizontal; chips.Margin = new Thickness(0, 8, 0, 12); coach.Children.Add(chips);
            prompt.FontSize = 20; prompt.FontWeight = FontWeight.SemiBold; prompt.Foreground = Visuals.B(Visuals.TextC); prompt.TextWrapping = TextWrapping.Wrap;
            coach.Children.Add(prompt);

            inputRow.Orientation = Orientation.Horizontal; inputRow.Margin = new Thickness(0, 16, 0, 0);
            input.Width = 210; input.FontSize = 30; input.FontWeight = FontWeight.SemiBold; input.TextAlignment = TextAlignment.Center;
            input.HorizontalContentAlignment = HorizontalAlignment.Center; input.VerticalContentAlignment = VerticalAlignment.Center;
            input.Padding = new Thickness(6, 4, 6, 4); input.MinHeight = 58; input.CornerRadius = new CornerRadius(8);
            input.Background = Visuals.B("#1A1E27"); input.BorderBrush = Visuals.B("#353C4B"); input.Foreground = Visuals.B(Visuals.TextC);
            input.AcceptsReturn = false;
            // keep App Hub's dark box while it has the cursor or the pointer (the theme would turn it black); the blue edge stays
            foreach (string state in new[] { ":focus", ":pointerover" })
            {
                Style keep = new Style(x => x.OfType<TextBox>().Class(state).Template().Name("PART_BorderElement"));
                keep.Setters.Add(new Setter(Border.BackgroundProperty, Visuals.B("#1A1E27")));
                input.Styles.Add(keep);
            }
            input.AddHandler(InputElement.KeyDownEvent, (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit(input.Text); } }, RoutingStrategies.Tunnel);
            input.TextChanged += (s, e) => Redraw();
            inputRow.Children.Add(input);
            check = Ui.Button("Check", false, 20); check.Padding = new Thickness(22, 8, 22, 8); check.Margin = new Thickness(10, 0, 0, 0); check.MinHeight = 58;
            check.Click += (s, e) => Submit(input.Text);
            inputRow.Children.Add(check);
            coach.Children.Add(inputRow);

            // the number pad (phones): digits, a point, a slash and a space for mixed numbers, and rubbing out
            keypad.Columns = 5; keypad.Margin = new Thickness(0, 10, 0, 0); keypad.HorizontalAlignment = HorizontalAlignment.Stretch; keypad.MaxWidth = 460;
            foreach (string k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", ".", "/", "space", "\u232B" })
            {
                Button b = Ui.Button(k, false, k == "space" ? 15 : 22); b.Margin = new Thickness(0, 0, 6, 6); b.MinHeight = 48; b.Padding = new Thickness(0);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                if (k == "\u232B") ToolTip.SetTip(b, "Rub out");
                string key = k;
                b.Click += (s, e) => PadKey(key);
                keypad.Children.Add(b);
            }
            coach.Children.Add(keypad);

            feedbackBox.CornerRadius = new CornerRadius(10); feedbackBox.Padding = new Thickness(12, 8, 12, 8); feedbackBox.Margin = new Thickness(0, 14, 0, 0); feedbackBox.BorderThickness = new Thickness(1);
            feedback.FontSize = 16; feedback.TextWrapping = TextWrapping.Wrap; feedbackBox.Child = feedback;
            coach.Children.Add(feedbackBox);

            hintBtn = Ui.Button("Show a hint", false, 14); hintBtn.Padding = new Thickness(14, 6, 14, 6); hintBtn.Margin = new Thickness(0, 14, 0, 0); hintBtn.HorizontalAlignment = HorizontalAlignment.Left;
            hintBtn.Click += (s, e) => { hintBox.IsVisible = true; };
            coach.Children.Add(hintBtn);
            hintBox.Background = Visuals.B("#33280E"); hintBox.BorderBrush = Visuals.B(Visuals.Yellow); hintBox.BorderThickness = new Thickness(1); hintBox.CornerRadius = new CornerRadius(10);
            hintBox.Padding = new Thickness(12, 8, 12, 8); hintBox.Margin = new Thickness(0, 8, 0, 0);
            hint.FontSize = 15; hint.Foreground = Visuals.B("#FDE68A"); hint.TextWrapping = TextWrapping.Wrap; hintBox.Child = hint;
            coach.Children.Add(hintBox);

            next = Ui.Button("Next problem", false, 20); next.Padding = new Thickness(24, 10, 24, 10); next.Margin = new Thickness(0, 18, 0, 0); next.HorizontalAlignment = HorizontalAlignment.Left;
            next.Click += (s, e) => { if (Finished != null) Finished(this, EventArgs.Empty); };
            coach.Children.Add(next);

            TextBlock memo = new TextBlock();
            memo.Text = "Dad, Mom, Sister, Brother:\nDivide, Multiply, Subtract, Bring down. Then again!";
            memo.FontSize = 13; memo.Foreground = Visuals.B(Visuals.Muted); memo.Margin = new Thickness(0, 26, 0, 0); memo.TextWrapping = TextWrapping.Wrap;
            coach.Children.Add(memo);

            SizeChanged += (s, e) => ApplyLayout();
            ApplyLayout();
        }

        /// <summary>Starts a problem.</summary>
        public void Load(LongDivision ld, bool guided)
        {
            Ld = ld; Guided = guided; lastFeedback = ""; lastOk = true;
            input.Text = "";
            UpdateCoach();
            Redraw();
            FocusInput();
        }

        /// <summary>Puts the cursor in the answer box (not with the number pad, so a phone's own keyboard stays away).</summary>
        public void FocusInput() { if (Ld != null && !Ld.Done && !(showKeypad ?? narrow)) input.Focus(); }

        /// <summary>Checks an answer for the current step (also used by tests and pictures).</summary>
        public void Submit(string given)
        {
            if (Ld == null || Ld.Done) return;
            LdStep s = Ld.Step;
            LdCheck r = Ld.Check(given ?? "", Guided);
            lastOk = r.Ok; lastFeedback = r.Message;
            if (!r.Ok && Ld.WrongThisStep >= 3)
            {
                lastFeedback = r.Message + "  The answer is " + s.Expected + ". I wrote it in for you; let's keep going.";
                Ld.GiveUp();
            }
            if (r.Ok) Sound.Correct(); else Sound.Wrong();
            input.Text = "";
            UpdateCoach();
            Redraw();
            if (Answered != null) Answered(this, new LdEventArgs { Ok = r.Ok, Done = Ld.Done, Kind = s.Kind });
            FocusInput();
        }
        /// <summary>Answers the next n steps correctly (pictures and tests).</summary>
        public void Advance(int n) { for (int i = 0; i < n && !Ld.Done; i++) Submit(Ld.Step.Expected); }
        /// <summary>What the child has typed so far (pictures).</summary>
        public void Type(string text) { input.Text = text; Redraw(); }
        public string FeedbackText { get { return lastFeedback; } }
        /// <summary>What is in the answer box now.</summary>
        public string Typed { get { return input.Text ?? ""; } }
        /// <summary>A number-pad key: a digit, ".", "/", "space" or "\u232B" (rub out the last character).</summary>
        public void PadKey(string key)
        {
            string t = input.Text ?? "";
            if (key == "\u232B") t = t.Length > 0 ? t.Substring(0, t.Length - 1) : t;
            else if (key == "space") t += " ";
            else t += key;
            input.Text = t;
            Redraw();
        }
        /// <summary>True when the coach sits under the sheet (narrow screens).</summary>
        public bool IsNarrow { get { return narrow; } }

        // ---------------------------------------------------------------- the layout (wide: side by side; narrow: one above the other)
        void ApplyLayout()
        {
            double w = Bounds.Width, h = Bounds.Height;
            bool nowNarrow = w > 0 && w < WideMin;
            bool changed = nowNarrow != narrow || root.ColumnDefinitions.Count == 0;
            narrow = nowNarrow;
            if (changed)
            {
                root.ColumnDefinitions.Clear(); root.RowDefinitions.Clear();
                if (narrow)
                {
                    root.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
                    root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    Grid.SetColumn(side, 0); Grid.SetRow(side, 1);
                    side.BorderThickness = new Thickness(0, 1, 0, 0);
                    side.Padding = new Thickness(14, 10, 14, 10);
                }
                else
                {
                    root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                    root.ColumnDefinitions.Add(new ColumnDefinition(430, GridUnitType.Pixel));
                    Grid.SetColumn(side, 1); Grid.SetRow(side, 0);
                    side.BorderThickness = new Thickness(1, 0, 0, 0);
                    side.Padding = new Thickness(22, 18, 22, 18);
                }
            }
            side.MaxHeight = narrow && h > 0 ? Math.Max(260, h * 0.62) : double.PositiveInfinity;
            keypad.IsVisible = (showKeypad ?? narrow) && (Ld == null || !Ld.Done);
            FitSheet();
        }
        /// <summary>Narrow screens shrink the sheet to the width (down to 55%); wide ones show it at full size.</summary>
        void FitSheet()
        {
            double w = Bounds.Width;
            double s = 1;
            if (narrow && sheet.Width > 0 && w > 0) s = Math.Max(0.55, Math.Min(1, (w - 2 * SheetMargin - 8) / sheet.Width));
            if (Math.Abs(s - scale) > 0.001 || sheetHost.LayoutTransform == null)
            {
                scale = s;
                sheetHost.LayoutTransform = new ScaleTransform(s, s);
            }
        }

        static readonly string[] Dmsb = { "Divide", "Multiply", "Subtract", "BringDown" };
        void UpdateCoach()
        {
            LdStep s = Ld.Step;
            bool hasStory = Ld.Story.Length > 0;
            storyBox.IsVisible = hasStory;
            story.Text = Ld.Story;
            chips.Children.Clear();
            foreach (string k in Dmsb)
            {
                bool on = s != null && s.Kind == k;
                Border chip = new Border(); chip.CornerRadius = new CornerRadius(12); chip.Padding = new Thickness(10, 4, 10, 4); chip.Margin = new Thickness(0, 0, 6, 0);
                chip.Background = Visuals.B(on ? Visuals.Blue : "#1B1F28"); chip.BorderBrush = Visuals.B(on ? Visuals.Blue2 : "#353C4B"); chip.BorderThickness = new Thickness(1);
                TextBlock t = new TextBlock(); t.Text = k == "BringDown" ? "Bring down" : k; t.FontSize = 13; t.FontWeight = FontWeight.SemiBold; t.Foreground = Visuals.B(on ? "#FFFFFF" : Visuals.Muted);
                chip.Child = t; chips.Children.Add(chip);
            }
            if (s == null)
            {
                stepNo.Text = "Done!";
                prompt.Text = Ld.Summary() + "\n" + (Ld.Mistakes == 0 ? "Perfect, not a single slip!" : "Finished! You fixed " + Ld.Mistakes + " slip" + (Ld.Mistakes == 1 ? "" : "s") + " on the way.");
                inputRow.IsVisible = false; hintBtn.IsVisible = false; hintBox.IsVisible = false;
                next.IsVisible = true;
            }
            else
            {
                stepNo.Text = "Step " + (Ld.Current + 1) + " of " + Ld.StepCount + (s.Kind == "Story" ? "  |  back to the story" : "");
                prompt.Text = Guided || s.Kind == "Story" || s.Row == -2 ? s.Prompt : PracticePrompt(s);
                inputRow.IsVisible = true; next.IsVisible = false;
                hint.Text = s.Hint;
                hintBtn.IsVisible = s.Hint.Length > 0;
                hintBox.IsVisible = Guided && Ld.WrongThisStep > 1 && s.Hint.Length > 0;
            }
            keypad.IsVisible = (showKeypad ?? narrow) && s != null;
            feedbackBox.IsVisible = lastFeedback.Length > 0;
            feedback.Text = lastFeedback;
            feedback.Foreground = Visuals.B(lastOk ? "#A7F3D0" : "#FDE68A");
            feedbackBox.Background = Visuals.B(lastOk ? "#0E2F24" : "#33280E");
            feedbackBox.BorderBrush = Visuals.B(lastOk ? Visuals.Green : Visuals.Yellow);
        }
        static string PracticePrompt(LdStep s)
        {
            switch (s.Kind)
            {
                case "Divide": return "Divide: what digit goes on top?";
                case "Multiply": return "Multiply: what goes under the part you're dividing?";
                case "Subtract": return "Subtract: what's left?";
                case "BringDown": return "Bring down: which digit comes down?";
                default: return s.Prompt;
            }
        }

        // ---------------------------------------------------------------- the sheet
        void Cell(string text, double x, double y, string color, bool bold)
        {
            TextBlock t = new TextBlock(); t.Text = text; t.FontSize = FontSz; t.Foreground = Visuals.B(color); t.FontFamily = Visuals.Mono;
            if (bold) t.FontWeight = FontWeight.Bold;
            Size sz = Visuals.Measure(t);
            Canvas.SetLeft(t, x + (CW - sz.Width) / 2); Canvas.SetTop(t, y + (RH - sz.Height) / 2);
            sheet.Children.Add(t);
        }
        /// <summary>A number written right-aligned so its last digit sits in column col.</summary>
        void Number(string text, int col, double y, double x0, string color)
        {
            for (int i = 0; i < text.Length; i++) Cell(text[i].ToString(), x0 + (col - text.Length + 1 + i) * CW, y, color, false);
        }

        public void Redraw()
        {
            sheet.Children.Clear();
            if (Ld == null) return;
            string divisor = Ld.Divisor.ToString(CultureInfo.InvariantCulture);
            double x0 = 30 + (divisor.Length + 1) * CW, y0 = 10;
            int cols = Ld.Digits.Length;
            double yQ = y0, yD = y0 + RH;
            int rows = Math.Max(1, Ld.WorkRows);
            sheet.Width = x0 + (cols + 4) * CW + 20;
            sheet.Height = yD + RH * (rows + 2) + 20;
            FitSheet();

            // the part being divided now (guided mode)
            LdStep cur = Ld.Step;
            if (Guided && cur != null && (cur.Kind == "Divide" || cur.Kind == "Multiply" || cur.Kind == "Subtract"))
            {
                int mrow = MultiplyRowFor(cur.Col);
                string partial = cur.Partial.ToString(CultureInfo.InvariantCulture);
                int len = partial.Length;
                if (mrow == 0 && Ld.FirstCol + 1 > len) len = Ld.FirstCol + 1;   // dividend < divisor: the leading digits
                double py = mrow == 0 ? yD : yD + RH * mrow;
                Visuals.Box(sheet, x0 + (cur.Col - len + 1) * CW - 2, py + 2, len * CW + 4, RH - 4, Visuals.Yellow, 50, Visuals.Yellow, 1.5);
            }

            // divisor, bracket, dividend
            TextBlock dv = new TextBlock(); dv.Text = divisor; dv.FontSize = FontSz; dv.FontWeight = FontWeight.Bold; dv.Foreground = Visuals.B(Visuals.Blue2); dv.FontFamily = Visuals.Mono;
            Size dvs = Visuals.Measure(dv); Canvas.SetLeft(dv, x0 - 18 - dvs.Width); Canvas.SetTop(dv, yD + (RH - dvs.Height) / 2); sheet.Children.Add(dv);
            Path bracket = new Path(); bracket.Stroke = Visuals.B(Visuals.TextC); bracket.StrokeThickness = 3;
            bracket.Data = Geometry.Parse(string.Format(CultureInfo.InvariantCulture, "M {0},{1} Q {2},{3} {0},{4} M {0},{1} L {5},{1}", x0 - 8, yD + 2, x0 + 4, yD + RH / 2, yD + RH, x0 + cols * CW + 8));
            sheet.Children.Add(bracket);
            for (int i = 0; i < cols; i++) Cell(Ld.Digits[i].ToString(), x0 + i * CW, yD, i >= Ld.DigitsBeforePoint ? Visuals.Muted : Visuals.TextC, true);
            if (Ld.Places > 0)
            {
                // the dividend's point (the zeros after it are drawn grey); the answer's point once the division reaches it
                Visuals.Dot2(sheet, x0 + Ld.DigitsBeforePoint * CW, yD + RH - 9, 3.5, Visuals.TextC);
                int at = Ld.Steps.FindIndex(s => s.Kind == "BringDown" && s.Col == Ld.DigitsBeforePoint);
                if (at >= 0 && Ld.Current > at) Visuals.Dot2(sheet, x0 + Ld.DigitsBeforePoint * CW, yQ + RH - 9, 3.5, Visuals.Yellow);
            }

            // what has been written so far
            for (int i = 0; i < Ld.Current && i < Ld.StepCount; i++)
            {
                LdStep s = Ld.StepAt(i);
                double y = s.Row == -1 ? yQ : yD + RH * (s.Row + 1);
                switch (s.Kind)
                {
                    case "Divide": Cell(s.Expected, x0 + s.Col * CW, yQ, Visuals.Yellow, true); break;
                    case "Multiply":
                        {
                            Number(s.Expected, s.Col, y, x0, Visuals.Blue2);
                            int span = Math.Max(s.Expected.Length, s.Partial.ToString(CultureInfo.InvariantCulture).Length);
                            Cell("\u2212", x0 + (s.Col - span) * CW, y, Visuals.Muted, false);
                            Visuals.Ln(sheet, x0 + (s.Col - span + 1) * CW, y + RH - 2, x0 + (s.Col + 1) * CW, y + RH - 2, Visuals.TextC, 2);
                            break;
                        }
                    case "Subtract": Number(s.Expected, s.Col, y, x0, Visuals.Green); break;
                    case "BringDown":
                        Cell(s.Expected, x0 + s.Col * CW, y, Visuals.Orange, true);
                        Line l = Visuals.Ln(sheet, x0 + s.Col * CW + CW / 2, yD + RH - 4, x0 + s.Col * CW + CW / 2, y + 6, Visuals.Orange, 1.5);
                        l.StrokeDashArray = new AvaloniaList<double> { 3, 3 }; l.Opacity = 0.6;
                        break;
                }
            }
            if (Ld.Places == 0 && Ld.Remainder > 0 && Ld.Steps.Take(Ld.Current).Any(s => s.Kind == "Remainder"))
            {
                LdStep lastDiv = Ld.Steps.Last(s => s.Kind == "Divide");
                TextBlock r = new TextBlock(); r.Text = "R " + Ld.Remainder; r.FontSize = FontSz; r.FontWeight = FontWeight.Bold; r.Foreground = Visuals.B(Visuals.Yellow); r.FontFamily = Visuals.Mono;
                Canvas.SetLeft(r, x0 + (lastDiv.Col + 1) * CW + 10); Canvas.SetTop(r, yQ + 2); sheet.Children.Add(r);
            }

            // the cell to fill now
            if (cur != null && cur.Row >= -1)
            {
                double y = cur.Row == -1 ? yQ : yD + RH * (cur.Row + 1);
                string shown = (input.Text ?? "").Trim();
                int width = cur.Kind == "Multiply" || cur.Kind == "Subtract" ? Math.Max(cur.Expected.Length, Math.Max(1, shown.Length)) : 1;
                Rectangle target = Visuals.Box(sheet, x0 + (cur.Col - width + 1) * CW + 1, y + 2, width * CW - 2, RH - 4, Visuals.Blue, 60, Visuals.Blue2, 2.5);
                target.RadiusX = 6; target.RadiusY = 6;
                if (shown.Length > 0) Number(shown.Length > width ? shown.Substring(shown.Length - width) : shown, cur.Col, y, x0, "#FFFFFF");
                else Cell("?", x0 + cur.Col * CW, y, Visuals.Blue2, true);
                if (cur.Kind == "Multiply")
                {
                    int span = Math.Max(width, cur.Partial.ToString(CultureInfo.InvariantCulture).Length);
                    Cell("\u2212", x0 + (cur.Col - span) * CW, y, Visuals.Muted, false);
                }
                // keep it in view (the sheet may be shrunk on a phone)
                double top = SheetMargin + y * scale, bottom = SheetMargin + (y + RH) * scale;
                double view = sheetScroll.Viewport.Height, off = sheetScroll.Offset.Y;
                if (view > 0 && (bottom > off + view || top < off)) sheetScroll.Offset = new Vector(sheetScroll.Offset.X, Math.Max(0, top - 120 * scale));
            }
        }
        /// <summary>The work row of the multiply step in column col (the partial dividend sits one row above it).</summary>
        int MultiplyRowFor(int col)
        {
            for (int k = 0; k < Ld.StepCount; k++) if (Ld.StepAt(k).Kind == "Multiply" && Ld.StepAt(k).Col == col) return Ld.StepAt(k).Row;
            return 0;
        }
    }
}
