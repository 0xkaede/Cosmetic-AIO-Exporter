//this is so bad lmao

using Newtonsoft.Json;

namespace Cosmetic.Exporter.Models
{
    public class ExportData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("emote", NullValueHandling = NullValueHandling.Ignore)]
        public EmoteData Emote { get; set; }

        [JsonProperty("pickaxe", NullValueHandling = NullValueHandling.Ignore)]
        public EmoteData Pickaxe { get; set; }

        [JsonProperty("backbling", NullValueHandling = NullValueHandling.Ignore)]
        public EmoteData Backbling { get; set; }

        [JsonProperty("skin", NullValueHandling = NullValueHandling.Ignore)]
        public EmoteData Skin { get; set; }
    }

    public class EmoteData
    {
        [JsonProperty("blend")]
        public Dictionary<string, float> Blend { get; set; } = new Dictionary<string, float>();

        [JsonProperty("floatCurves")]
        public List<string> FloatCurves { get; set; } = new List<string>();

        [JsonProperty("isAddictive")]
        public bool IsAddictive { get; set; }

        [JsonProperty("isMovingEmote")]
        public bool IsMovingEmote { get; set; } = false;

        [JsonProperty("isMoveForwardOnly")]
        public bool IsMoveForwardOnly { get; set; } = false;

        [JsonProperty("walkForwardSpeed")]
        public float WalkForwardSpeed { get; set; } = 0;
    }
}
