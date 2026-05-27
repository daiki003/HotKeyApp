using System;

namespace HotKeyCommandApp.Models
{
    public class PairEntryFolder : IPairEntryEditable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新しいフォルダ";
        public string ParentFolderId { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string FirstValue
        {
            get => Name;
            set => Name = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string SecondValue
        {
            get => string.Empty;
            set { }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFolder => true;
    }
}
