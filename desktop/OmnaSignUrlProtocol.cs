using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OmnaSignDesktop;

/// <summary>
/// Registers a user-scoped URL handler so the public signing website can
/// open the local Apple Account workflow without accessing login credentials.
/// The URL contains no IPA path, account password, session or verification code.
/// </summary>
internal static class OmnaSignUrlProtocol
{
    private const string Protocol = "omnasign";
    public static bool IsAppleAccountLaunch(string[] args)
    {
        foreach (string value in args)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var u)) continue;
            if (!string.Equals(u.Scheme, Protocol, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(u.Host, "apple-account", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) ||
                !string.IsNullOrEmpty(u.Fragment)) continue;
            if (u.AbsolutePath is not ("" or "/")) continue;
            return true;
        }
        return false;
    }

    public static void RegisterCurrentUser()
    {
        // No administrative privileges, no downloaded scripts, no executable args
        // other than the URI that is validated by IsAppleAccountLaunch().
        using var baseKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\omnasign", true);
        if (baseKey is null) return;
        baseKey.SetValue("", "URL:OmnaSign Apple Account Signer", RegistryValueKind.String);
        baseKey.SetValue("URL Protocol", "", RegistryValueKind.String);
        using var command = baseKey.CreateSubKey(@"shell\open\command", true);
        if (command is null) return;
        string exe = Application.ExecutablePath.Replace("\"", "");
        command.SetValue("", "\"" + exe + "\" \"%1\"", RegistryValueKind.String);
    }
}
