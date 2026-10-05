using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using VoryWindows.Models;
using VoryWindows.Services;

namespace VoryWindows.Auth
{
    /// <summary>
    /// DPAPI (CurrentUser) encrypted store for gateway credentials.
    /// File: %APPDATA%\VoryWindows\gateways.dat. Secrets are never logged.
    /// </summary>
    public class GatewayStore
    {
        private readonly string _filePath;

        public GatewayStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoryWindows");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "gateways.dat");
        }

        public List<GatewayCredential> Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new List<GatewayCredential>();
                var protectedBytes = File.ReadAllBytes(_filePath);
                var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plain);
                var list = JsonConvert.DeserializeObject<List<GatewayCredential>>(json);
                return list ?? new List<GatewayCredential>();
            }
            catch (Exception ex)
            {
                Log.Info("GatewayStore load failed: " + ex.Message);
                return new List<GatewayCredential>();
            }
        }

        public void Save(List<GatewayCredential> gateways)
        {
            try
            {
                var json = JsonConvert.SerializeObject(gateways ?? new List<GatewayCredential>(),
                    Formatting.None);
                var plain = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                var tmp = _filePath + ".tmp";
                File.WriteAllBytes(tmp, protectedBytes);
                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(tmp, _filePath);
            }
            catch (Exception ex)
            {
                Log.Info("GatewayStore save failed: " + ex.Message);
                throw;
            }
        }
    }
}
