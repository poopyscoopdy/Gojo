import Foundation
import SwiftUI
import RorkSign

@MainActor
final class SigningModel: ObservableObject {
    enum Asset { case ipa, certificate, profile }
    @Published private(set) var ipa: URL?
    @Published private(set) var certificate: URL?
    @Published private(set) var profile: URL?
    @Published private(set) var signedIPA: URL?
    @Published var bundleID = ""
    @Published var password = ""
    @Published var working = false
    @Published var message = "Import your IPA and signing files."
    @Published var alert: String?
    var canSign: Bool { ipa != nil && certificate != nil && profile != nil && bundleID.split(separator: ".").count >= 2 && !working }

    func importAsset(_ picked: URL, kind: Asset) {
        let authorized = picked.startAccessingSecurityScopedResource()
        defer { if authorized { picked.stopAccessingSecurityScopedResource() } }
        let ext = picked.pathExtension.lowercased()
        switch kind {
        case .ipa: guard ext == "ipa" else { alert = "Choose an IPA file."; return }
        case .certificate: guard ["p12", "pfx"].contains(ext) else { alert = "Choose a .p12 or .pfx certificate."; return }
        case .profile: guard ext == "mobileprovision" else { alert = "Choose a .mobileprovision file."; return }
        }
        do {
            var folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("OmnaSignerImports", isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            var vals = URLResourceValues()
            vals.isExcludedFromBackup = true
            try? folder.setResourceValues(vals)
            let copy = folder.appendingPathComponent(UUID().uuidString + "." + ext)
            try FileManager.default.copyItem(at: picked, to: copy)
            try? (copy as NSURL).setResourceValue(FileProtectionType.complete, forKey: .fileProtectionKey)
            switch kind {
            case .ipa:
                if let old = ipa { try? FileManager.default.removeItem(at: old) }
                ipa = copy
            case .certificate:
                if let old = certificate { try? FileManager.default.removeItem(at: old) }
                certificate = copy
            case .profile:
                if let old = profile { try? FileManager.default.removeItem(at: old) }
                profile = copy
                if let decoded = try? RorkSigner.decodeProvisioningProfile(Data(contentsOf: copy)),
                   let allowed = decoded.authorizedBundleIdentifier, !allowed.contains("*") {
                    bundleID = allowed
                }
            }
            signedIPA = nil
            message = "Imported \(picked.lastPathComponent)."
        } catch {
            alert = "Import failed: \(error.localizedDescription)"
        }
    }

    func clearCredentials() {
        for file in [certificate, profile].compactMap({ $0 }) {
            try? FileManager.default.removeItem(at: file)
        }
        certificate = nil
        profile = nil
        password = ""
        signedIPA = nil
        message = "Signing credentials cleared."
    }

    func sign() {
        guard canSign, let input = ipa, let p12 = certificate, let provisioning = profile else { return }
        let pass = password
        let targetBundleID = bundleID
        password = ""
        signedIPA = nil
        working = true
        message = "Signing locally on this iPhone…"
        Task {
            do {
                let output = try await Task.detached(priority: .userInitiated) { () -> URL in
                    let privateIdentity = try SigningIdentity(pkcs12Data: Data(contentsOf: p12), password: pass)
                    let profileBytes = try Data(contentsOf: provisioning)
                    let destination = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
                        .appendingPathComponent("OmnaSigner-\(UUID().uuidString.prefix(8))-signed.ipa")
                    _ = try RorkSigner.signIPA(at: input, outputURL: destination,
                        identity: privateIdentity,
                        options: AppSigningOptions(bundleIdentifier: targetBundleID, rootProvisioningProfile: profileBytes))
                    return destination
                }.value
                signedIPA = output
                message = "Signed IPA created. Export it with the Share button."
            } catch {
                alert = "Signing failed: \(error.localizedDescription)"
                message = "Signing was not completed."
            }
            working = false
        }
    }
}
