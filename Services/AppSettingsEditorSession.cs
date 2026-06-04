using System.Collections.Generic;
using System.Linq;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public class AppSettingsEditorSession
    {
        public double ButtonWidth { get; set; }
        public double ButtonHeight { get; set; }
        public double MovementSpeed { get; set; }
        public double FontSize { get; set; } = 15.0;
        public string GlobalHotkey { get; set; } = "Win+Alt+Z";
        public string SettingsShortcut { get; set; } = "Ctrl+Comma";
        public string CreateButtonShortcut { get; set; } = "Ctrl+Plus";
        public List<ConstantEntry> Constants { get; set; } = new();
        public List<PairEntryFolder> ConstantFolders { get; set; } = new();
        public List<SelectTemplate> SelectTemplates { get; set; } = new();
        public List<PairEntryFolder> SelectTemplateFolders { get; set; } = new();

        public static AppSettingsEditorSession Create(AppSettings source)
        {
            return new AppSettingsEditorSession
            {
                ButtonWidth = source.ButtonWidth,
                ButtonHeight = source.ButtonHeight,
                MovementSpeed = source.MovementSpeed,
                FontSize = source.FontSize,
                GlobalHotkey = source.GlobalHotkey,
                SettingsShortcut = source.SettingsShortcut,
                CreateButtonShortcut = source.CreateButtonShortcut,
                Constants = CloneConstants(source.Constants),
                ConstantFolders = CloneFolders(source.ConstantFolders),
                SelectTemplates = CloneSelectTemplates(source.SelectTemplates),
                SelectTemplateFolders = CloneFolders(source.SelectTemplateFolders)
            };
        }

        public void ApplyTo(AppSettings target)
        {
            target.ButtonWidth = ButtonWidth;
            target.ButtonHeight = ButtonHeight;
            target.MovementSpeed = MovementSpeed;
            target.FontSize = FontSize;
            target.GlobalHotkey = GlobalHotkey;
            target.SettingsShortcut = SettingsShortcut;
            target.CreateButtonShortcut = CreateButtonShortcut;
            target.Constants = NormalizeConstants(Constants);
            target.ConstantFolders = new List<PairEntryFolder>();
            target.SelectTemplates = NormalizeSelectTemplates(SelectTemplates);
            target.SelectTemplateFolders = new List<PairEntryFolder>();
        }

        private static List<ConstantEntry> CloneConstants(IEnumerable<ConstantEntry> source)
        {
            return (source ?? new List<ConstantEntry>())
                .Select(c => new ConstantEntry
                {
                    Name = c.Name,
                    Value = c.Value,
                    ParentFolderId = c.ParentFolderId,
                    SortOrder = c.SortOrder
                })
                .ToList();
        }

        private static List<SelectTemplate> CloneSelectTemplates(IEnumerable<SelectTemplate> source)
        {
            return (source ?? new List<SelectTemplate>())
                .Select(t => new SelectTemplate
                {
                    Name = t.Name,
                    Options = new List<string>(t.Options ?? new List<string>()),
                    ParentFolderId = t.ParentFolderId,
                    SortOrder = t.SortOrder
                })
                .ToList();
        }

        private static List<PairEntryFolder> CloneFolders(IEnumerable<PairEntryFolder> source)
        {
            return (source ?? new List<PairEntryFolder>())
                .Select(f => new PairEntryFolder
                {
                    Id = f.Id,
                    Name = f.Name,
                    ParentFolderId = f.ParentFolderId,
                    SortOrder = f.SortOrder
                })
                .ToList();
        }

        private static List<ConstantEntry> NormalizeConstants(IEnumerable<ConstantEntry> source)
        {
            return (source ?? new List<ConstantEntry>())
                .Select((c, index) => new ConstantEntry
                {
                    Name = c.Name,
                    Value = c.Value,
                    ParentFolderId = string.Empty,
                    SortOrder = index
                })
                .ToList();
        }

        private static List<SelectTemplate> NormalizeSelectTemplates(IEnumerable<SelectTemplate> source)
        {
            return (source ?? new List<SelectTemplate>())
                .Select((t, index) => new SelectTemplate
                {
                    Name = t.Name,
                    Options = new List<string>(t.Options ?? new List<string>()),
                    ParentFolderId = string.Empty,
                    SortOrder = index
                })
                .ToList();
        }
    }
}
