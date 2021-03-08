using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace WikiBulkInsert
{
    public class NamuWiki
    {
        [JsonProperty("namespace")]
        public string Namespace { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("contributors")]
        public List<string> Contributors { get; set; }
    }

}
