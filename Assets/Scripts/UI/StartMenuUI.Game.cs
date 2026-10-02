using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;

namespace Jogging.UI
{
    /// <summary>
    /// "Abenteuer" (from the runner's home): level and shirt colour, the streak with rest days, the week's
    /// quests for the whole family, the family journey, climb records ("Kronen") and the album.
    /// Everything comes from the logbook (Profile/Game).
    /// </summary>
    public partial class StartMenuUI
    {
        private void ShowAdventure()
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            var me = ps.Profile;
            var now = DateTime.Now;
            var family = new Dictionary<string, List<SessionSummary>>();
            foreach (var r in ps.Runners) family[r.id] = ps.SummariesOf(r.id);
            var mine = family.TryGetValue(me.id, out var mm) ? mm : new List<SessionSummary>();
            var gold = new Color(1f, 0.82f, 0.35f);

            UiControls.Label(p, Jogging.Core.Loc.F("Abenteuer · {0}", me.playerName), 34, 40f, -24f, 800f, 48f, UiTheme.TextPrimary, bold: true);

            // ---- level, shirt colour, streak
            float x = 40f, cw = 600f;
            int xp = Game.Xp(mine), level = Game.Level(xp);
            int from = Game.XpFor(level), to = Game.XpFor(level + 1);
            UiControls.Label(p, Jogging.Core.Loc.F("Level {0}", level), 30, x, -92f, 220f, 44f, gold, bold: true);
            UiControls.Label(p, Jogging.Core.Loc.F("{0} Punkte · noch {1} bis Level {2}", xp, to - xp, level + 1), 18, x + 200f, -96f, cw - 200f, 36f, UiTheme.TextMuted);
            Bar(p, x, -142f, cw, 10f, (xp - from) / (float)Mathf.Max(1, to - from), gold);

            var unlocked = Game.Shirts.Where(s => s.level <= level).ToList();
            int si = Mathf.Max(0, unlocked.FindIndex(s => s.id == (me.shirt ?? "")));
            var nextShirt = Game.Shirts.FirstOrDefault(s => s.level > level);
            UiControls.Label(p, "Shirtfarbe", 20, x, -168f, 210f, 44f, UiTheme.TextMuted);
            Text shirtLabel = null;
            shirtLabel = UiControls.Button(p, "", () =>
            {
                si = (si + 1) % unlocked.Count;
                me.shirt = unlocked[si].id;
                ps.SaveActive(false);
                shirtLabel.text = "◀  " + Jogging.Core.Loc.T(unlocked[si].name) + "  ▶";
                FindFirstObjectByType<Jogging.World.RealPlayerFigure>()?.Refresh();
            }, x + 215f, -166f, 250f, 42f, null, 20).GetComponentInChildren<Text>();
            shirtLabel.text = "◀  " + Jogging.Core.Loc.T(unlocked[si].name) + "  ▶";
            if (nextShirt.name != null)
                UiControls.Label(p, Jogging.Core.Loc.F("{0} ab Level {1}", Jogging.Core.Loc.T(nextShirt.name), nextShirt.level), 17, x + 480f, -168f, cw - 480f, 44f, UiTheme.TextMuted);

            var (days, runDays) = Game.Streak(mine, now);
            UiControls.Label(p, days == 1 ? Jogging.Core.Loc.T("Serie: 1 Tag – bis zu 2 Ruhetage am Stück sind erlaubt")
                              : days > 0 ? Jogging.Core.Loc.F("Serie: {0} Tage ({1} Lauftage) – bis zu 2 Ruhetage am Stück erlaubt", days, runDays)
                                         : Jogging.Core.Loc.T("Serie: lauf heute, dann beginnt sie – bis zu 2 Ruhetage am Stück sind erlaubt"),
                18, x, -222f, cw, 30f, days > 0 ? new Color(1f, 0.7f, 0.3f) : UiTheme.TextMuted);

            // ---- weekly quests (the same for the whole family)
            float y = -272f;
            UiControls.Label(p, Jogging.Core.Loc.F("WOCHENAUFGABEN · KW {0}", RunnerStats.IsoWeek(now)), 16, x, y, cw, 24f, UiTheme.TextMuted, bold: true);
            y -= 30f;
            var quests = Game.QuestProgress(mine, now);
            for (int i = 0; i < quests.Count; i++)
            {
                var (q, f, done) = quests[i];
                var others = ps.Runners.Where(r => r.id != me.id && Game.QuestProgress(family[r.id], now)[i].done).Select(r => r.playerName).ToList();
                UiControls.Label(p, (done ? "✓  " : "") + Jogging.Core.Loc.T(q.text), 20, x, y, cw, 30f, done ? UiTheme.Success : UiTheme.TextPrimary, bold: done);
                Bar(p, x, y - 34f, 300f, 8f, f, done ? UiTheme.Success : UiTheme.Accent);
                if (others.Count > 0) UiControls.Label(p, Jogging.Core.Loc.F("geschafft: {0}", string.Join(", ", others)), 16, x + 320f, y - 26f, cw - 320f, 24f, UiTheme.TextMuted);
                y -= 62f;
            }
            UiControls.Label(p, Jogging.Core.Loc.F("je {0} Punkte · jeden Montag neue", Game.XpPerQuest), 15, x, y + 10f, cw, 22f, UiTheme.TextMuted);

            // ---- family journey
            y -= 30f;
            var j = Game.JourneyNow(family);
            UiControls.Label(p, "FAMILIENREISE", 16, x, y, cw, 24f, UiTheme.TextMuted, bold: true);
            UiControls.Label(p, Jogging.Core.Loc.T(j.journey.name) + (j.round > 0 ? Jogging.Core.Loc.F("  ·  {0} Reisen geschafft", j.round) : ""), 22, x, y - 28f, cw, 32f, UiTheme.TextPrimary, bold: true);
            float by = y - 70f;
            Bar(p, x, by, cw, 12f, j.Fraction, gold);
            foreach (var s in j.journey.stops) // stops as ticks on the bar
            {
                var tick = UiTheme.Panel(p, s.at <= j.done ? gold : new Color(1f, 1f, 1f, 0.35f));
                UiControls.Place(tick.GetComponent<RectTransform>(), x + cw * s.at / j.journey.Total - 1f, by + 4f, 2f, 20f);
            }
            string Amount(float v) => j.journey.elevation ? Jogging.Core.Units.FmtElev(v) : Jogging.Core.Units.FmtDist(v * 1000f);
            UiControls.Label(p, Jogging.Core.Loc.F("{0} von {1} · zuletzt {2} · noch {3} bis {4}", Amount(j.done), Amount(j.journey.Total), Jogging.Core.Loc.T(j.last.name), Amount(j.next.at - j.done), Jogging.Core.Loc.T(j.next.name)),
                17, x, by - 22f, cw, 26f, UiTheme.TextMuted);
            UiControls.Label(p, j.journey.elevation ? "alle Höhenmeter der Familie zählen" : "alle Kilometer der Familie zählen", 15, x, by - 48f, cw, 22f, UiTheme.TextMuted);

            // ---- climb records
            y = by - 92f;
            UiControls.Label(p, "BERGWERTUNGEN · KRONEN", 16, x, y, cw, 24f, UiTheme.TextMuted, bold: true);
            var crowns = ps.Runners.Select(r => (r.playerName, n: Game.Crowns(family, r.id))).OrderByDescending(t => t.n).ToList();
            UiControls.Label(p, crowns.Any(c => c.n > 0) ? string.Join("   ·   ", crowns.Select(c => $"👑 {c.playerName} {c.n}"))
                                                         : Jogging.Core.Loc.T("Noch keine – lauf eine gespeicherte Strecke mit Anstieg: oben gibt es die Zeit"),
                19, x, y - 28f, cw, 30f, UiTheme.TextPrimary);

            // ---- album (right)
            float ax = 690f, aw = 590f;
            var album = Game.Album(mine);
            int have = Game.Cards.Count(c => album.ContainsKey(c.key));
            UiControls.Label(p, Jogging.Core.Loc.F("ALBUM · {0} / {1} KARTEN", have, Game.Cards.Length), 16, ax, -92f, aw, 24f, UiTheme.TextMuted, bold: true);
            const int cols = 4;
            float cwid = (aw - (cols - 1) * 8f) / cols, chei = 86f;
            for (int i = 0; i < Game.Cards.Length; i++)
            {
                var c = Game.Cards[i];
                bool got = album.TryGetValue(c.key, out var col);
                float cx = ax + (i % cols) * (cwid + 8f), cy = -124f - (i / cols) * (chei + 8f);
                var card = UiTheme.Panel(p, got ? (c.rarity == 3 ? new Color(0.42f, 0.33f, 0.12f) : c.rarity == 2 ? new Color(0.20f, 0.27f, 0.42f) : UiTheme.PanelSoft)
                                                : new Color(1f, 1f, 1f, 0.04f));
                UiControls.Place(card.GetComponent<RectTransform>(), cx, cy, cwid, chei);
                UiControls.Label(card.transform, got ? Jogging.Core.Loc.T(c.key) : "?", 17, 8f, -6f, cwid - 16f, 28f, got ? UiTheme.TextPrimary : UiTheme.TextMuted, TextAnchor.MiddleLeft, bold: got);
                UiControls.Label(card.transform, new string('★', c.rarity), 15, 8f, -34f, cwid - 16f, 22f, c.rarity == 3 ? gold : UiTheme.TextMuted);
                UiControls.Label(card.transform, got ? $"{col.count}×" : Jogging.Core.Loc.T(c.group), 15, 8f, -56f, cwid - 16f, 22f, UiTheme.TextMuted);
            }
            UiControls.Label(p, Jogging.Core.Loc.F("je {0} Punkte für eine neue Karte · ★★★ sind selten (der Fuchs zeigt sich am ehesten in der Dämmerung)", Game.XpPerCard),
                15, ax, -124f - 6 * (chei + 8f) - 4f, aw, 40f, UiTheme.TextMuted).horizontalOverflow = HorizontalWrapMode.Wrap;

            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowHome(), w - 240f, -820f, 200f, 56f);
        }
    }
}
