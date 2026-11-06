using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Newtonsoft.Json;

namespace MoVibeSkua.Android.Platform;

/// <summary>An AQW account kept on this device for one-tap login.</summary>
public sealed class SavedAccount
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Server { get; set; } = "Twilly";

    [JsonIgnore]
    public string Title => Username;

    [JsonIgnore]
    public string Subtitle => $"Server: {Server}";
}

/// <summary>
/// The saved accounts, stored only on this device: the list is encrypted (AES-GCM) with a key that lives
/// in the Android Keystore and never leaves it, then kept in the app's private preferences. Uninstalling
/// the app or clearing its data removes them. Every army client reads the same list.
/// </summary>
public static class SavedAccounts
{
    private const string PrefsName = "movibeskua.accounts";
    private const string KeyAlias = "movibeskua.accounts.key";
    private const string ListKey = "list";

    private static ISharedPreferences? Prefs =>
        global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

    public static List<SavedAccount> Load()
    {
        try
        {
            string? stored = Prefs?.GetString(ListKey, null);
            if (string.IsNullOrEmpty(stored))
                return new();
            string json = Decrypt(stored);
            return JsonConvert.DeserializeObject<List<SavedAccount>>(json) ?? new();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Accounts] load failed: {ex.GetType().Name}");
            return new();
        }
    }

    public static void Save(IEnumerable<SavedAccount> accounts)
    {
        try
        {
            string stored = Encrypt(JsonConvert.SerializeObject(accounts));
            Prefs?.Edit()?.PutString(ListKey, stored)?.Commit();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Accounts] save failed: {ex.GetType().Name}");
        }
    }

    private static IKey GetKey()
    {
        KeyStore store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.GetKey(KeyAlias, null) is IKey existing)
            return existing;

        KeyGenerator generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .Build());
        return generator.GenerateKey()!;
    }

    private static string Encrypt(string plain)
    {
        Cipher cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, GetKey());
        byte[] iv = cipher.GetIV()!;
        byte[] data = cipher.DoFinal(Encoding.UTF8.GetBytes(plain))!;
        return Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(data);
    }

    private static string Decrypt(string stored)
    {
        string[] parts = stored.Split(':', 2);
        Cipher cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, GetKey(), new GCMParameterSpec(128, Convert.FromBase64String(parts[0])));
        return Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);
    }
}
