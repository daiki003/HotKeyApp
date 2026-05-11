using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public class ConfigService
    {
        private static readonly string ConfigFileName = "commands.json";
        private static readonly string SettingsFileName = "settings.json";
        private static readonly string PresetsFileName = "presets.json";
        private string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        private string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        private string PresetsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PresetsFileName);

        private List<CommandPreset>? _cachedPresets;

        public List<CommandEntry> LoadCommands()
        {
            if (!File.Exists(ConfigPath))
            {
                CreateDefaultConfig();
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                var commands = JsonConvert.DeserializeObject<List<CommandEntry>>(json, settings) ?? new List<CommandEntry>();

                return commands;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
                return new List<CommandEntry>();
            }
        }


        public void SaveCommands(List<CommandEntry> commands)
        {
            try
            {
                // Ensure no system commands (ADD_BUTTON) are persisted
                CleanupForSave(commands);

                var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                var json = JsonConvert.SerializeObject(commands, settings);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        private void CleanupForSave(List<CommandEntry> list)
        {
            if (list == null) return;

            // Remove system buttons
            list.RemoveAll(c => c.IsSystemButton);

            // Recurse into children
            foreach (var item in list)
            {
                if (item.Children != null)
                {
                    CleanupForSave(item.Children);
                }
            }
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public List<CommandPreset> LoadPresets()
        {
            if (_cachedPresets != null) return _cachedPresets;

            if (!File.Exists(PresetsPath))
            {
                _cachedPresets = CommandPreset.GetDefaults();
                SavePresets(_cachedPresets);
                return _cachedPresets;
            }

            try
            {
                var json = File.ReadAllText(PresetsPath);
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                _cachedPresets = JsonConvert.DeserializeObject<List<CommandPreset>>(json, settings) ?? CommandPreset.GetDefaults();
                return _cachedPresets;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading presets: {ex.Message}");
                return CommandPreset.GetDefaults();
            }
        }

        public void SavePresets(List<CommandPreset> presets)
        {
            try
            {
                var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                var json = JsonConvert.SerializeObject(presets, settings);
                File.WriteAllText(PresetsPath, json);
                _cachedPresets = presets;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving presets: {ex.Message}");
            }
        }

        private void CreateDefaultConfig()
        {
            var defaults = new List<CommandEntry>
            {
                new CommandEntry
                {
                    Name = "URLを開く",
                    Category = CommandCategory.Hierarchy,
                    Children = new List<CommandEntry>
                    {
                        new CommandEntry { Name = "Google", Category = CommandCategory.Open, Value = "https://www.google.com" },
                        new CommandEntry { Name = "Youtube", Category = CommandCategory.Open, Value = "https://www.youtube.com" }
                    }
                },
                new CommandEntry
                {
                    Name = "フォルダを開く",
                    Category = CommandCategory.Hierarchy,
                    Children = new List<CommandEntry>
                    {
                        new CommandEntry 
                        { 
                            Name = "フォルダ1", 
                            Category = CommandCategory.Open, 
                            Value = "C:\\", 
                            Behavior = new CommandBehavior { UseWindowFocusLogic = true } 
                        }
                    }
                },
                new CommandEntry
                {
                    Name = "バッチ実行",
                    Category = CommandCategory.Hierarchy,
                    Children = new List<CommandEntry>
                    {
                        new CommandEntry 
                        { 
                            Name = "JSON同期", 
                            Category = CommandCategory.Open, 
                            Value = @"C:\Users\daiki\AIApp\HotKeyApp\sync_json.bat", 
                            Behavior = new CommandBehavior { IsBatchMode = true } 
                        },
                        new CommandEntry 
                        { 
                            Name = "バッチ1", 
                            Category = CommandCategory.Open, 
                            Value = "reload.bat", 
                            Behavior = new CommandBehavior { IsBatchMode = true } 
                        }
                    }
                },
                new CommandEntry
                {
                    Name = "ファイルを開く",
                    Category = CommandCategory.Hierarchy,
                    Children = new List<CommandEntry>()
                }
            };

            SaveCommands(defaults);
        }
    }
}
