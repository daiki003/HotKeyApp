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
        private string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        private string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);

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
                return JsonConvert.DeserializeObject<List<CommandEntry>>(json, settings) ?? new List<CommandEntry>();
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
            list.RemoveAll(c => c.Type == CommandType.Command && (c.Value?.StartsWith("ADD_") ?? false));

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



        private void CreateDefaultConfig()
        {
            var defaults = new List<CommandEntry>
            {
                new CommandEntry
                {
                    Name = "URLを開く",
                    Type = CommandType.Menu,
                    Children = new List<CommandEntry>
                    {
                        new CommandEntry { Name = "Google", Type = CommandType.URL, Value = "https://www.google.com" },
                        new CommandEntry { Name = "Youtube", Type = CommandType.URL, Value = "https://www.youtube.com" }
                    }
                },
                new CommandEntry
                {
                    Name = "フォルダを開く",
                    Type = CommandType.Menu,
                    Children = new List<CommandEntry>
                    {
                        new CommandEntry { Name = "フォルダ1", Type = CommandType.Folder, Value = "C:\\" }
                    }
                },
                new CommandEntry
                {
                    Name = "バッチ実行",
                    Type = CommandType.Menu,
                    Children = new List<CommandEntry>
                    {
                        new CommandEntry { Name = "JSON同期", Type = CommandType.Batch, Value = @"C:\Users\daiki\AIApp\HotKeyApp\sync_json.bat" },
                        new CommandEntry { Name = "バッチ1", Type = CommandType.Batch, Value = "reload.bat" }
                    }
                },
                new CommandEntry
                {
                    Name = "ファイルを開く",
                    Type = CommandType.Menu,
                    Children = new List<CommandEntry>
                    {
                    }
                }
            };

            SaveCommands(defaults);
        }
    }
}
