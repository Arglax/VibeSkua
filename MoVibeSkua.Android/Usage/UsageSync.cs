using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Android.Content;
using Android.Telephony;

namespace MoVibeSkua.Android.Usage;

/// <summary>
/// Adds the counts to this install's Firestore document over the REST API, signed in as an anonymous
/// Firebase user, so the app needs no Firebase SDK. The project's web API key and id come from the
/// firebase.json asset, which is kept out of the repository; without it nothing is sent.
/// Every army client is the same install: the sign-in lives in a file, and a file lock keeps two
/// clients from signing up or writing the first record at once.
/// </summary>
internal static class UsageSync
{
    public const int Schema = 1;
    public const string AppId = "movibeskua";
    public const string Collection = "movibeskua_installs";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static Config? _config;
    private static bool _configRead;

    private sealed record Config(string ApiKey, string ProjectId);

    private sealed class Auth
    {
        public string Uid { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string IdToken { get; set; } = string.Empty;
        public long ExpiresAt { get; set; }
        // The install's document exists, so firstSeen is already set.
        public bool Created { get; set; }
    }

    private static Context Context => global::Android.App.Application.Context;

    public static string Folder => Path.Combine(Context.FilesDir!.AbsolutePath, "usage");

    private static string AuthPath => Path.Combine(Folder, "auth.json");

    private static string LockPath => Path.Combine(Folder, "auth.lock");

    public static bool Configured => ReadConfig() is not null;

    private static Config? ReadConfig()
    {
        if (_configRead)
            return _config;
        _configRead = true;
        try
        {
            using Stream stream = Context.Assets!.Open("firebase.json");
            using JsonDocument json = JsonDocument.Parse(stream);
            string? key = json.RootElement.GetProperty("apiKey").GetString();
            string? project = json.RootElement.GetProperty("projectId").GetString();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(project))
                _config = new Config(key, project);
        }
        catch
        {
            _config = null;
        }
        return _config;
    }

    /// <summary>Adds <paramref name="counts"/> to the install's document. False when nothing was written.</summary>
    public static async Task<bool> UploadAsync(IReadOnlyDictionary<string, long> counts)
    {
        if (ReadConfig() is not { } config || counts.Count == 0)
            return false;

        Directory.CreateDirectory(Folder);
        using FileStream? held = await LockAsync();
        if (held is null)
            return false;

        Auth? auth = await SignInAsync(config);
        if (auth is null)
            return false;

        string document = $"projects/{config.ProjectId}/databases/(default)/documents/{Collection}/{auth.Uid}";
        using HttpRequestMessage request = new(HttpMethod.Post, $"https://firestore.googleapis.com/v1/projects/{config.ProjectId}/databases/(default)/documents:commit")
        {
            Content = new StringContent(CommitBody(document, counts, !auth.Created), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.IdToken);
        using HttpResponseMessage response = await Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            auth.ExpiresAt = 0;
            WriteAuth(auth);
        }
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            Trace.WriteLine($"[Usage] upload refused: {(int)response.StatusCode} {(error.Length > 300 ? error[..300] : error)}");
            return false;
        }
        Trace.WriteLine($"[Usage] uploaded {counts.Count} counts");

        if (!auth.Created)
        {
            auth.Created = true;
            WriteAuth(auth);
        }
        return true;
    }

    /// <summary>The project id and a signed-in token for other writes (problem reports); null when offline or not configured.</summary>
    public static async Task<(string ProjectId, string Uid, string IdToken)?> SignedInAsync()
    {
        if (ReadConfig() is not { } config)
            return null;
        Directory.CreateDirectory(Folder);
        using FileStream? held = await LockAsync();
        if (held is null)
            return null;
        Auth? auth = await SignInAsync(config);
        return auth is null ? null : (config.ProjectId, auth.Uid, auth.IdToken);
    }

    /// <summary>Forgets the token after the server refused it, so the next sign-in refreshes it.</summary>
    public static void TokenRefused()
    {
        if (ReadAuth() is { } auth)
        {
            auth.ExpiresAt = 0;
            WriteAuth(auth);
        }
    }

    // One write: the plain fields by mask, and every count as an increment, so writes from several
    // clients add up instead of overwriting each other.
    private static string CommitBody(string document, IReadOnlyDictionary<string, long> counts, bool firstWrite)
    {
        (string version, long code) = AppVersion();
        JsonObject fields = new()
        {
            ["schema"] = new JsonObject { ["integerValue"] = Schema.ToString() },
            ["app"] = new JsonObject { ["stringValue"] = AppId },
            ["appVersion"] = new JsonObject { ["stringValue"] = version.Length > 24 ? version[..24] : version },
            ["appVersionCode"] = new JsonObject { ["integerValue"] = code.ToString() },
            ["country"] = new JsonObject { ["stringValue"] = Country() },
        };

        JsonArray transforms = new() { new JsonObject { ["fieldPath"] = "lastSeen", ["setToServerValue"] = "REQUEST_TIME" } };
        if (firstWrite)
            transforms.Add(new JsonObject { ["fieldPath"] = "firstSeen", ["setToServerValue"] = "REQUEST_TIME" });
        foreach ((string id, long count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (count <= 0)
                continue;
            // "scr.SCREEN.field" is a screen's timing, "day.dYYYYMMDD.kind" time on a day; anything else is a signal.
            string path = id.StartsWith("scr.", StringComparison.Ordinal) ? "screens." + id[4..]
                : id.StartsWith("day.", StringComparison.Ordinal) ? "days." + id[4..]
                : "features." + id;
            transforms.Add(new JsonObject
            {
                ["fieldPath"] = path,
                ["increment"] = new JsonObject { ["integerValue"] = count.ToString() }
            });
        }

        JsonObject body = new()
        {
            ["writes"] = new JsonArray
            {
                new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = document, ["fields"] = fields },
                    ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray("schema", "app", "appVersion", "appVersionCode", "country") },
                    ["updateTransforms"] = transforms
                }
            }
        };
        return body.ToJsonString();
    }

    private static async Task<Auth?> SignInAsync(Config config)
    {
        Auth? auth = ReadAuth();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (auth is not null && auth.ExpiresAt - 60_000 > now)
            return auth;

        if (auth is not null)
        {
            using HttpResponseMessage refreshed = await Http.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={config.ApiKey}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = auth.RefreshToken
                }));
            if (refreshed.IsSuccessStatusCode)
            {
                using JsonDocument json = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
                JsonElement root = json.RootElement;
                auth.IdToken = root.GetProperty("id_token").GetString()!;
                auth.RefreshToken = root.GetProperty("refresh_token").GetString()!;
                auth.ExpiresAt = now + long.Parse(root.GetProperty("expires_in").GetString()!) * 1000;
                WriteAuth(auth);
                return auth;
            }
            // A refused refresh token (the anonymous user was deleted) starts a new install below;
            // a network failure leaves the sign-in for the next try.
            if (refreshed.StatusCode != HttpStatusCode.BadRequest)
                return null;
        }

        using HttpResponseMessage signedUp = await Http.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={config.ApiKey}",
            new StringContent("{\"returnSecureToken\":true}", Encoding.UTF8, "application/json"));
        if (!signedUp.IsSuccessStatusCode)
        {
            // CONFIGURATION_NOT_FOUND or OPERATION_NOT_ALLOWED: Anonymous sign-in is off in the project.
            string error = await signedUp.Content.ReadAsStringAsync();
            Trace.WriteLine($"[Usage] sign-up refused: {(int)signedUp.StatusCode} {(error.Length > 300 ? error[..300] : error)}");
            return null;
        }
        using (JsonDocument json = JsonDocument.Parse(await signedUp.Content.ReadAsStringAsync()))
        {
            JsonElement root = json.RootElement;
            auth = new Auth
            {
                Uid = root.GetProperty("localId").GetString()!,
                IdToken = root.GetProperty("idToken").GetString()!,
                RefreshToken = root.GetProperty("refreshToken").GetString()!,
                ExpiresAt = now + long.Parse(root.GetProperty("expiresIn").GetString()!) * 1000
            };
        }
        WriteAuth(auth);
        return auth;
    }

    private static Auth? ReadAuth()
    {
        try
        {
            return File.Exists(AuthPath) ? JsonSerializer.Deserialize<Auth>(File.ReadAllText(AuthPath)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteAuth(Auth auth)
    {
        string temp = AuthPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(auth));
        File.Move(temp, AuthPath, true);
    }

    // FileShare.None takes an exclusive lock on the file that other processes see too.
    private static async Task<FileStream?> LockAsync()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(200);
            }
        }
        return null;
    }

    private static (string Version, long Code) AppVersion()
    {
        try
        {
            var info = Context.PackageManager!.GetPackageInfo(Context.PackageName!, 0)!;
            long code = OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
            return (info.VersionName ?? "unknown", code);
        }
        catch
        {
            return ("unknown", 0);
        }
    }

    /// <summary>The two-letter country of the mobile network, else the SIM, else the phone's language settings.</summary>
    private static string Country()
    {
        try
        {
            TelephonyManager? telephony = Context.GetSystemService(Context.TelephonyService) as TelephonyManager;
            string?[] candidates =
            {
                telephony?.NetworkCountryIso,
                telephony?.SimCountryIso,
                Context.Resources?.Configuration?.Locales.Get(0)?.Country,
                Java.Util.Locale.Default.Country
            };
            return candidates.Select(c => c?.Trim().ToUpperInvariant()).FirstOrDefault(c => c is { Length: 2 }) ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
