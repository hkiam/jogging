using System.Collections.Generic;

namespace Jogging.Route
{
    /// <summary>Starting points for new routes (the parameters stay editable).</summary>
    public static class RoutePresets
    {
        public static RouteDoc Create(string preset, int seed)
        {
            var d = new RouteDoc();
            d.generator.seed = seed;
            var p = d.@params;
            switch (preset)
            {
                case "flach":
                    d.meta.name = "Flach & schnell"; p.lightPollution = 0.55f;
                    p.lengthKm = 5f; p.loop = true; p.curviness = 0.3f; p.rolling = 0.15f;
                    p.vegetation = "meadow"; p.density = 0.5f; p.clearings = 0.3f; p.water = "lake"; p.relief = 0.2f; p.timeOfDay = 9.5f; p.haze = 0.25f; p.season = "spring";
                    break;
                case "huegelig":
                    d.meta.name = "Hügelrunde"; p.lightPollution = 0.3f;
                    p.lengthKm = 8f; p.loop = true; p.curviness = 0.55f; p.rolling = 0.6f;
                    p.vegetation = "mixed"; p.density = 0.7f; p.clearings = 0.35f; p.water = "stream"; p.relief = 0.6f; p.timeOfDay = 16.5f; p.haze = 0.35f; p.season = "autumn";
                    p.climbs = new List<RouteClimb>
                    {
                        new RouteClimb { atKm = 2.0f, heightM = 30f, grade = 0.06f },
                        new RouteClimb { atKm = 5.2f, heightM = 22f, grade = 0.07f },
                    };
                    break;
                case "bergauf":
                    d.meta.name = "Bergauf-Training"; p.lightPollution = 0.1f;
                    p.lengthKm = 6f; p.loop = false; p.curviness = 0.45f; p.rolling = 0.3f;
                    p.vegetation = "conifer"; p.density = 0.85f; p.clearings = 0.15f; p.rocks = 0.7f; p.water = "stream"; p.relief = 0.9f; p.timeOfDay = 10.5f; p.haze = 0.5f; p.season = "winter";
                    p.climbs = new List<RouteClimb>
                    {
                        new RouteClimb { atKm = 1.5f, heightM = 45f, grade = 0.08f },
                        new RouteClimb { atKm = 4.0f, heightM = 55f, grade = 0.09f },
                    };
                    break;
                case "wald":
                    d.meta.name = "Waldrunde"; p.lightPollution = 0.3f;
                    p.lengthKm = 10f; p.loop = true; p.curviness = 0.8f; p.rolling = 0.45f;
                    p.climbs = new List<RouteClimb> { new RouteClimb { atKm = 6.5f, heightM = 25f, grade = 0.05f } };
                    p.vegetation = "mixed"; p.density = 0.95f; p.clearings = 0.15f; p.water = "both"; p.relief = 0.5f; p.timeOfDay = 12.5f; p.haze = 0.2f;
                    break;
                case "see":
                    d.meta.name = "Seerunde"; p.lightPollution = 0.45f;
                    p.lengthKm = 7f; p.loop = true; p.curviness = 0.5f; p.rolling = 0.2f;
                    p.vegetation = "birch"; p.density = 0.55f; p.clearings = 0.5f; p.water = "lake"; p.relief = 0.3f; p.timeOfDay = 18.5f; p.haze = 0.35f; p.season = "summer";
                    break;
                case "pass":
                    d.meta.name = "Bergpass"; p.lightPollution = 0.05f;
                    p.lengthKm = 8f; p.loop = true; p.curviness = 0.6f; p.rolling = 0.25f;
                    p.climbs = new List<RouteClimb> { new RouteClimb { atKm = 3.0f, heightM = 90f, grade = 0.05f } }; // long and steady; the loop brings you back down
                    p.vegetation = "conifer"; p.density = 0.7f; p.clearings = 0.3f; p.rocks = 0.85f; p.water = "stream"; p.relief = 1f; p.timeOfDay = 14f; p.haze = 0.45f; p.season = "autumn";
                    break;
                case "wellen":
                    d.meta.name = "Wellen"; p.lightPollution = 0.35f;
                    p.lengthKm = 6f; p.loop = true; p.curviness = 0.45f; p.rolling = 0.9f;
                    p.climbs = new List<RouteClimb>();
                    for (int i = 0; i < 5; i++) p.climbs.Add(new RouteClimb { atKm = 6f * (i + 1f) / 6f, heightM = 12f, grade = 0.08f }); // short, steep, often
                    p.vegetation = "meadow"; p.density = 0.45f; p.clearings = 0.6f; p.water = "none"; p.relief = 0.6f; p.timeOfDay = 10f; p.haze = 0.2f; p.season = "spring";
                    break;
                case "winterwald":
                    d.meta.name = "Winterwald"; p.lightPollution = 0.15f;
                    p.lengthKm = 6f; p.loop = true; p.curviness = 0.7f; p.rolling = 0.35f;
                    p.climbs = new List<RouteClimb> { new RouteClimb { atKm = 3.5f, heightM = 25f, grade = 0.06f } };
                    p.vegetation = "conifer"; p.density = 0.9f; p.clearings = 0.2f; p.water = "stream"; p.relief = 0.55f; p.timeOfDay = 13f; p.haze = 0.45f; p.season = "winter"; p.weather = "snow";
                    break;
                case "mondnacht": // a clear winter night with a bright moon over snow, far from any town
                    d.meta.name = "Mondnacht"; p.lightPollution = 0.06f;
                    p.lengthKm = 6f; p.loop = true; p.curviness = 0.55f; p.rolling = 0.3f;
                    p.climbs = new List<RouteClimb> { new RouteClimb { atKm = 2.5f, heightM = 30f, grade = 0.05f } };
                    p.vegetation = "conifer"; p.density = 0.6f; p.clearings = 0.5f; p.water = "lake"; p.relief = 0.5f;
                    p.timeOfDay = 21f; p.haze = 0.15f; p.season = "winter"; p.weather = "clear"; p.moonAge = 11.5f;
                    p.spectators = "none"; // nobody out at night
                    break;
                default: // "frei"
                    d.meta.name = "Freies Laufen";
                    p.endless = true; p.loop = false; p.curviness = 0.55f; p.rolling = 0.5f;
                    p.vegetation = "mixed"; p.density = 0.7f; p.clearings = 0.3f; p.water = "both"; p.relief = 0.55f; p.timeOfDay = 11f; p.haze = 0.3f;
                    break;
            }
            RouteGenerator.Generate(d);
            return d;
        }

        /// <summary>A free run through a landscape nobody has seen yet (new seed every time).</summary>
        public static RouteDoc NewFreeRun()
        {
            var d = Create("frei", NewSeed());
            d.@params.weather = WeatherFor(d.generator.seed, d.@params.season);
            d.@params.timeOfDay = TimeFor(d.generator.seed);
            d.@params.lightPollution = 0.05f + (float)new System.Random(d.generator.seed * 31 + 5).NextDouble() * 0.55f;
            return d;
        }

        /// <summary>
        /// Time of day of a new free run from its seed: mostly by day, now and then early morning, the evening
        /// or a night run (with the sky of that hour: sunrise, sunset, moon and stars).
        /// </summary>
        public static float TimeFor(int seed)
        {
            var r = new System.Random(seed * 104729 + 3);
            double k = r.NextDouble(), f = r.NextDouble();
            float h = k < 0.08 ? 6f + (float)f * 2f          // early morning (mist)
                    : k < 0.76 ? 8f + (float)f * 9.5f        // day
                    : k < 0.92 ? 17.5f + (float)f * 3.5f     // evening, sunset
                    : 20.5f + (float)f * 2.5f;               // night
            return (float)System.Math.Round(h * 4f) / 4f;
        }

        /// <summary>Weather of a new free run from its seed: mostly clear, sometimes clouds or rain (snow in winter).</summary>
        public static string WeatherFor(int seed, string season)
        {
            int r = new System.Random(seed * 7919 + 17).Next(100);
            if (r < 55) return "clear";
            if (r < 80) return "cloudy";
            return season == "winter" ? "snow" : "rain";
        }

        public static int NewSeed() => new System.Random().Next(1, 1000000);

        /// <summary>An endless free run that hasn't been saved as a route (a discovery).</summary>
        public static bool IsUnsavedFreeRun(RouteDoc d) => d != null && d.@params.endless && string.IsNullOrEmpty(d.id);

        /// <summary>What the scene behind the menu shows after a run: a new landscape after a free run, else the same route.</summary>
        public static RouteDoc AfterRun(RouteDoc current) => current == null || IsUnsavedFreeRun(current) ? NewFreeRun() : current;

        public static readonly string[] All = { "frei", "flach", "huegelig", "bergauf", "wald", "see", "pass", "wellen", "winterwald", "mondnacht" };
    }
}
