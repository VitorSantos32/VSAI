using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VSAI.AILogic
{
    public static class LicenseValidator
    {
        private const string SupabaseUrl = "https://gyqjxnjlpyzsygnrscqh.supabase.co/rest/v1/licenses";
        private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imd5cWp4bmpscHl6c3lnbnJzY3FoIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzY3NzM0MTMsImV4cCI6MjA5MjM0OTQxM30.k3r5SlgL5HIbJQZrFvCUgGQyWO7WkPHd_gRg0Jl43b8";

        public static string GetHWID()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var cryptoKey = key.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    {
                        if (cryptoKey != null)
                        {
                            return cryptoKey.GetValue("MachineGuid")?.ToString() ?? "UNKNOWN_HWID";
                        }
                    }
                }
            }
            catch
            {
                try
                {
                    using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    {
                        using (var cryptoKey = key.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                        {
                            if (cryptoKey != null)
                            {
                                return cryptoKey.GetValue("MachineGuid")?.ToString() ?? "UNKNOWN_HWID";
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return "UNKNOWN_HWID";
        }

        public static string LoadSavedLicenseKey()
        {
            try
            {
                string path = Path.Combine("bin", "license.cfg");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch { }
            return "";
        }

        public static void SaveLicenseKey(string key)
        {
            try
            {
                string dir = "bin";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string path = Path.Combine(dir, "license.cfg");
                File.WriteAllText(path, key.Trim());
            }
            catch { }
        }

        public class LicenseRow
        {
            public string? id { get; set; }
            public string? license_key { get; set; }
            public string? hwid { get; set; }
            public int duration_days { get; set; }
            public string? activated_at { get; set; }
            public string? expires_at { get; set; }
            public bool is_active { get; set; }
        }

        public static async Task<(bool success, string message)> ValidateLicenseAsync(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return (false, "Por favor, insira uma chave de licença válida.");

            string currentHwid = GetHWID();

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("apikey", AnonKey);
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

                    string url = $"{SupabaseUrl}?license_key=eq.{Uri.EscapeDataString(licenseKey)}";
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        return (false, $"Erro ao conectar ao servidor de licenças (HTTP {response.StatusCode}).");
                    }

                    string responseContent = await response.Content.ReadAsStringAsync();
                    var licenses = JsonSerializer.Deserialize<List<LicenseRow>>(responseContent);

                    if (licenses == null || licenses.Count == 0)
                    {
                        return (false, "Licença inválida ou inexistente.");
                    }

                    var license = licenses[0];

                    // 1. Check if the license has never been activated (activated_at is null or empty)
                    if (string.IsNullOrEmpty(license.activated_at))
                    {
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        DateTimeOffset expiresAt = now.AddDays(license.duration_days);

                        var patchBody = new
                        {
                            is_active = true,
                            activated_at = now.ToString("o"),
                            expires_at = expiresAt.ToString("o"),
                            hwid = currentHwid
                        };

                        string jsonBody = JsonSerializer.Serialize(patchBody);
                        using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                        {
                            var patchUrl = $"{SupabaseUrl}?id=eq.{license.id}";
                            var patchResponse = await client.PatchAsync(patchUrl, content);
                            if (patchResponse.IsSuccessStatusCode)
                            {
                                return (true, "Licença ativada com sucesso!");
                            }
                            else
                            {
                                return (false, "Falha ao ativar a licença no servidor.");
                            }
                        }
                    }

                    // 2. If already activated, check if it's currently marked as inactive in database
                    if (!license.is_active)
                    {
                        return (false, "Esta licença está inativa ou expirou.");
                    }

                    // 3. Check for expiration with culture-insensitive parsing (handles UTC, offsets like +00)
                    if (!string.IsNullOrEmpty(license.expires_at))
                    {
                        bool isExpired = false;
                        if (DateTimeOffset.TryParse(license.expires_at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset expiresAtOffset))
                        {
                            if (expiresAtOffset < DateTimeOffset.UtcNow)
                            {
                                isExpired = true;
                            }
                        }
                        else if (DateTime.TryParse(license.expires_at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime expiresAtLegacy))
                        {
                            if (expiresAtLegacy < DateTime.UtcNow)
                            {
                                isExpired = true;
                            }
                        }

                        if (isExpired)
                        {
                            var patchBody = new { is_active = false };
                            string jsonBody = JsonSerializer.Serialize(patchBody);
                            using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                            {
                                var patchUrl = $"{SupabaseUrl}?id=eq.{license.id}";
                                await client.PatchAsync(patchUrl, content);
                            }
                            return (false, "Esta licença expirou.");
                        }
                    }

                    // 4. Validate Hardware ID (HWID) binding
                    if (string.IsNullOrEmpty(license.hwid))
                    {
                        var patchBody = new { hwid = currentHwid };
                        string jsonBody = JsonSerializer.Serialize(patchBody);
                        using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                        {
                            var patchUrl = $"{SupabaseUrl}?id=eq.{license.id}";
                            var patchResponse = await client.PatchAsync(patchUrl, content);
                            if (patchResponse.IsSuccessStatusCode)
                            {
                                return (true, "Licença vinculada a este computador e validada!");
                            }
                        }
                    }
                    else if (!license.hwid.Equals(currentHwid, StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, "Esta licença já está vinculada a outro computador (HWID incorreto).");
                    }

                    return (true, "Licença validada com sucesso!");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro na validação da licença: {ex.Message}");
            }
        }
    }
}
