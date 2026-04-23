using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MySSH
{
    public class CustomAction
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
    }

    public class AppConfig
    {
        public string Host { get; set; } = "";
        public string Username { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public string LastLocalPath { get; set; } = "";
        public string LastRemotePath { get; set; } = "";
        public System.Collections.Generic.List<CustomAction> CustomActions { get; set; } = new System.Collections.Generic.List<CustomAction>();

        [JsonIgnore]
        public string Password
        {
            get => ConfigManager.Decrypt(EncryptedPassword);
            set => EncryptedPassword = ConfigManager.Encrypt(value);
        }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                catch
                {
                    return new AppConfig();
                }
            }
            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                return "";
            }
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return "";
            try
            {
                byte[] bytes = Convert.FromBase64String(encryptedText);
                byte[] decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return "";
            }
        }
    }
}
