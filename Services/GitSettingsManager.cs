using System;
using System.IO;
using System.Text.Json;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public class GitSettingsManager
    {
        private readonly string _settingsPath;
        private GitSettings _currentSettings;

        public GitSettings CurrentSettings => _currentSettings;

        public GitSettingsManager()
        {
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "git_settings.json");
            _currentSettings = new GitSettings();
            LoadSettings();
        }

        public void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<GitSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (settings != null)
                    {
                        settings.EnsureAliasTabs();
                        _currentSettings = settings;
                    }
                }
                catch { }
            }
            else
            {
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                _currentSettings.EnsureAliasTabs();
                string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }
    }
}
