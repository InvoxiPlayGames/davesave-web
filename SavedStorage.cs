using davesave.Saves;
using davesave.Saves.RB4;
using LibForge.Util;
using System.Buffers.Text;
using System.IO;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace davesave_web
{
    public class LoadedSaveMetadata
    {
        [JsonPropertyName("Source")] public string? Source { get; set; }
        [JsonPropertyName("Size")] public uint Size { get; set; }
        [JsonPropertyName("Sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("LastModified")] public DateTime LastModified { get; set; }
        [JsonPropertyName("LoadedAt")] public DateTime LoadedAt { get; set; }
    }

    // https://stackoverflow.com/a/1344255
    public class KeyGenerator
    {
        internal static readonly char[] chars =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();

        public static string GetUniqueKey(int size)
        {
            byte[] data = new byte[4 * size];
            using (var crypto = RandomNumberGenerator.Create())
            {
                crypto.GetBytes(data);
            }
            StringBuilder result = new StringBuilder(size);
            for (int i = 0; i < size; i++)
            {
                var rnd = BitConverter.ToUInt32(data, i * 4);
                var idx = rnd % chars.Length;

                result.Append(chars[idx]);
            }

            return result.ToString();
        }
    }

    public class SavedStorage
    {
        public static bool DoingAuthentication = false;

        public static RedeemAuthResponse? xboxLogin = null;
        public static LoadedSaveMetadata? loadedSaveMeta = null;
        public static RBProfile? loadedSave = null;
        public static byte[]? loadedSaveBinary = null;

        private readonly BrowserLocalStorage localStorage;

        public RBProfile? GetProfile()
        {
            return loadedSave;
        }

        public LoadedSaveMetadata? GetMetadata()
        {
            return loadedSaveMeta;
        }

        public RedeemAuthResponse? GetLogin()
        {
            return xboxLogin;
        }

        public SavedStorage(BrowserLocalStorage storage)
        {
            localStorage = storage;
        }

        // Returns a random string to be used as a cookie with the authentication server
        public async Task<string> StartAuthGetCookie()
        {
            string cookie = KeyGenerator.GetUniqueKey(32);
            await localStorage.SetItem("AuthenticationCookie", cookie);
            return cookie;
        }

        // Saves the "state" parameter 
        public async Task StartAuthSaveState(string state)
        {
            await localStorage.SetItem("AuthenticationState", state);
        }

        // If the "state" parameter is correct, returns the cookie value used for redeeming. Otherwise NULL.
        public async Task<string?> RedeemAuthVerifyState(string state)
        {
            string? cookieValue = await localStorage.GetItem("AuthenticationCookie");
            string? expectedState = await localStorage.GetItem("AuthenticationState");
            if (cookieValue == null || expectedState == null)
            {
                return null;
            }
            if (expectedState != state)
            {
                return null;
            }
            await localStorage.RemoveItem("AuthenticationCookie");
            await localStorage.RemoveItem("AuthenticationState");
            return cookieValue;
        }

        public async Task SaveXboxLogin(RedeemAuthResponse xboxAuth)
        {
            string encoded = JsonSerializer.Serialize(xboxAuth);
            await localStorage.SetItem("XboxLogin", encoded);
            xboxLogin = xboxAuth;
        }

        public async Task<bool> LoadXboxLogin()
        {
            try
            {
                string? xboxLoginJson = await localStorage.GetItem("XboxLogin");
                if (xboxLoginJson == null)
                    return false;
                RedeemAuthResponse? resp = JsonSerializer.Deserialize<RedeemAuthResponse>(xboxLoginJson);
                if (resp == null)
                    return false;
                xboxLogin = resp;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error {ex.Message} reading Xbox login");
                await localStorage.RemoveItem("XboxLogin");
                return false;
            }
        }

        public async Task<bool> ImportSaveFile(byte[] save, string source, DateTime lastModified)
        {
            // set up the metadata for the save file
            LoadedSaveMetadata meta = new();
            meta.Size = (uint)save.Length;
            meta.LoadedAt = DateTime.UtcNow;
            meta.LastModified = lastModified;
            meta.Source = source;
            using (SHA256 s256 = SHA256.Create())
            {
                byte[] shaHash = s256.ComputeHash(save);
                string hashString = BitConverter.ToString(shaHash).Replace("-", "");
                meta.Sha256 = hashString;
            }

            // parse the save file
            RBProfile? profile;
            try
            {
                using (MemoryStream ms = new MemoryStream(save))
                {
                    ms.Position = 0;
                    SaveDetection.SaveType type = await SaveDetection.DetectSaveTypeAsync(ms);
                    if (type == SaveDetection.SaveType.RB4PS4 || type == SaveDetection.SaveType.RB4Xbox)
                    {
                        EncryptedReadRevisionStream encryptedReadRevisionStream = new(ms, false);
                        profile = RBProfile.ReadFromStream(encryptedReadRevisionStream, type == SaveDetection.SaveType.RB4PS4);
                        encryptedReadRevisionStream.FinishReading();
                        encryptedReadRevisionStream.Close();
                        ms.Close();
                    }
                    else if (type == SaveDetection.SaveType.RB4PS4_Decrypted || type == SaveDetection.SaveType.RB4Xbox_Decrypted)
                    {
                        profile = RBProfile.ReadFromStream(ms, type == SaveDetection.SaveType.RB4PS4_Decrypted);
                        ms.Close();
                    }
                    else
                    {
                        ms.Close();
                        Console.WriteLine($"Save is of an invalid type: {type}");
                        return false;
                    }
                }
            } catch (Exception ex)
            {
                Console.WriteLine($"Error '{ex.Message}' while parsing save");
                return false;
            }
            // if we haven't had a save parsed for some reason, error out
            if (profile == null)
            {
                Console.WriteLine("Failed to parse save.");
                return false;
            }
            // if we do, then we can save it to LocalStorage
            loadedSaveMeta = meta;
            loadedSave = profile;
            loadedSaveBinary = save;
            await localStorage.SetItem("LoadedSave", Convert.ToBase64String(save));
            await localStorage.SetItem("LoadedSaveMetadata", JsonSerializer.Serialize(meta));
            return true;
        }

        public async Task<bool> LoadSavedSave()
        {
            string? localSave = await localStorage.GetItem("LoadedSave");
            string? localSaveMeta = await localStorage.GetItem("LoadedSaveMetadata");
            if (localSave == null || localSaveMeta == null)
            {
                return false;
            }
            try
            {
                // verify the metadata to be correct
                LoadedSaveMetadata? meta = JsonSerializer.Deserialize<LoadedSaveMetadata>(localSaveMeta);
                if (meta == null)
                {
                    Console.WriteLine("Metadata invalid.");
                    await ClearLoadedSave();
                    return false;
                }
                byte[] saveBytes = Convert.FromBase64String(localSave);
                if (saveBytes.Length != meta.Size)
                {
                    Console.WriteLine("Length mismatch.");
                    await ClearLoadedSave();
                    return false;
                }
                using (SHA256 s256 = SHA256.Create())
                {
                    byte[] shaHash = s256.ComputeHash(saveBytes);
                    string hashString = BitConverter.ToString(shaHash).Replace("-", "");
                    if (meta.Sha256 != hashString)
                    {
                        Console.WriteLine("Checksum mismatch.");
                        await ClearLoadedSave();
                        return false;
                    }
                }
                // parse the save file
                RBProfile? profile;
                using (MemoryStream ms = new MemoryStream(saveBytes))
                {
                    ms.Position = 0;
                    SaveDetection.SaveType type = await SaveDetection.DetectSaveTypeAsync(ms);
                    if (type == SaveDetection.SaveType.RB4PS4 || type == SaveDetection.SaveType.RB4Xbox)
                    {
                        EncryptedReadRevisionStream encryptedReadRevisionStream = new(ms, false);
                        profile = RBProfile.ReadFromStream(encryptedReadRevisionStream, type == SaveDetection.SaveType.RB4PS4);
                        encryptedReadRevisionStream.FinishReading();
                        encryptedReadRevisionStream.Close();
                        ms.Close();
                    }
                    else if (type == SaveDetection.SaveType.RB4PS4_Decrypted || type == SaveDetection.SaveType.RB4Xbox_Decrypted)
                    {
                        profile = RBProfile.ReadFromStream(ms, type == SaveDetection.SaveType.RB4PS4_Decrypted);
                        ms.Close();
                    }
                    else
                    {
                        ms.Close();
                        Console.WriteLine($"Save is of an invalid type: {type}");
                        await ClearLoadedSave();
                        return false;
                    }
                }
                loadedSave = profile;
                loadedSaveBinary = saveBytes;
                loadedSaveMeta = meta;
                return true;
            } catch (Exception ex)
            {
                Console.WriteLine($"Error {ex.Message} occurred reading loaded save.");
                await ClearLoadedSave();
                return false;
            }
        }

        public async Task ClearLoadedSave()
        {
            xboxLogin = null;
            loadedSave = null;
            loadedSaveMeta = null;
            loadedSaveBinary = null;
            await localStorage.RemoveItem("LoadedSave");
            await localStorage.RemoveItem("LoadedSaveMetadata");
            await localStorage.RemoveItem("XboxLogin");
        }
    }
}
