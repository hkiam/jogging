using System;
using System.Collections.Generic;

namespace Jogging.Route
{
    /// <summary>
    /// A route as stored in a <c>.jogroute</c> file (JSON): the recipe — parameters + seed — from
    /// which the course is generated deterministically, plus metadata and the generated profile
    /// (authoritative for stats/leaderboards even if a later generator version places scenery
    /// differently). Plain fields so <see cref="UnityEngine.JsonUtility"/> can read/write it.
    /// See docs/Streckeneditor-Plan.md.
    /// </summary>
    [Serializable]
    public class RouteDoc
    {
        public const int CurrentSchema = 1;
        public const int MaxRevision = 1_000_000;

        public int schemaVersion = CurrentSchema;
        public string id = "";          // ULID-like, globally unique (shareable later)
        public int revision = 1;
        public RouteMeta meta = new RouteMeta();
        public RouteGeneratorInfo generator = new RouteGeneratorInfo();
        public RouteParams @params = new RouteParams();
        public RouteProfile profile = new RouteProfile(); // filled by the generator
        public List<RouteEdit> edits = new List<RouteEdit>(); // workshop adjustments along the route
    }

    /// <summary>
    /// A workshop adjustment, anchored to the arc length so it stays valid when parameters change:
    ///   • "trees": tree density ×factor on [fromM, toM] (0.05 = clearing, 0.45 = sparser, 1.8 = denser)
    ///   • "spectators": a group of <c>count</c> spectators at atM
    ///   • "lake": a lake beside the trail at atM
    ///   • "stream": a stream crossing the trail at atM (flows with the terrain)
    /// side: "both" | "left" | "right".
    /// </summary>
    [Serializable]
    public class RouteEdit
    {
        public string type = "trees";
        public float fromM, toM, atM;
        public string side = "both";
        public float factor = 1f;
        public int count = 10;
        public string what = "";        // type "object": which wayside object (World/Wayside.Kind name, e.g. "Bench")

        public float Centre => type == "trees" ? (fromM + toM) * 0.5f : atM;
    }

    [Serializable]
    public class RouteMeta
    {
        public string name = "Neue Strecke";
        public string author = "";
        public string created = "";     // ISO 8601 UTC
        public string updated = "";
        public string description = "";
        public List<string> tags = new List<string>();
        public string license = "CC-BY-4.0";
    }

    [Serializable]
    public class RouteGeneratorInfo
    {
        public string version = RouteGenerator.Version;
        public int seed = 1;
    }

    [Serializable]
    public class RouteParams
    {
        // Course
        public float lengthKm = 5f;

        // From a GPX file (Route/GpxImport): the real elevation profile instead of climbs + rolling
        public string source = "";          // "" generated | "gpx"
        public float gpxStepM = 20f;
        public int[] gpxHeightsDm = new int[0]; // height every gpxStepM, decimetres relative to the start
        public bool loop = true;
        public bool endless;                // free run: no finish line (open course, very long)
        public float curviness = 0.5f;      // 0 straight … 1 winding

        // Profile
        public List<RouteClimb> climbs = new List<RouteClimb>();
        public float rolling = 0.4f;        // 0 flat … 1 very rolling between the climbs
        public float maxGrade = 0.10f;      // runnable limit (fraction)
        public float flatStartEndM = 150f;

        // Landscape
        public float relief = 0.5f;
        public string vegetation = "mixed"; // mixed | conifer | birch | meadow
        public float density = 0.7f;        // 0 sparse … 1 dense
        public float clearings = 0.3f;      // share of open sections
        public float rocks = 0.4f;
        public string water = "none";       // none | lake | stream | both

        // Mood
        public float timeOfDay = 11f;       // hours, 5 … 23.5 (night runs: moon and stars)
        public float haze = 0.3f;           // 0 clear … 1 hazy
        public string season = "summer";    // spring | summer | autumn | winter
        public string weather = "clear";    // clear | cloudy | rain | snow
        public float lightPollution = 0.3f; // night sky: 0 remote and dark (Milky Way) … 1 near a town (few stars, glow)
        public float moonAge = -1f;         // days since new moon (0 … 29.5); < 0 = today's real phase

        // People
        public string spectators = "few";   // none | few | some | many – people at the trailside (workshop groups always show)
    }

    [Serializable]
    public class RouteClimb
    {
        public float atKm;                  // centre of the climb
        public float heightM;               // + up, − down
        public float grade = 0.06f;         // typical grade on the climb (fraction)
    }

    [Serializable]
    public class RouteProfile
    {
        public float stepM = 2f;
        public float[] heightsM = new float[0]; // relative to the start (0)
        public float ascentM, descentM, maxGradePercent;
    }
}
