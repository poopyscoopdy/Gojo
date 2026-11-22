import SwiftUI
import UniformTypeIdentifiers
import UIKit
import WebKit
import MapKit
import CoreLocation

private enum ImportKind { case ipa, certificate, profile }

struct MainView: View {
    @EnvironmentObject private var signer: SigningModel
    @State private var importing = false
    @State private var importKind: ImportKind = .ipa
    @State private var selectedTab = 0
    @State private var installManifest = ""
    @State private var draftHTML = "<!doctype html><html><head><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>My Omna App</title><style>body{font-family:system-ui;text-align:center;background:#0a1732;color:white;padding:44px}button{padding:14px;border-radius:12px;background:#719aff;color:white;border:0}</style></head><body><h1>My Omna App</h1><p>Created using OmnaSigner.</p><button onclick=\"document.querySelector('p').textContent='It works!'\">Try me</button></body></html>"
    @State private var previewing = false
    @State private var exportedHTML: URL?
    @State private var chosenLocation = CLLocationCoordinate2D(latitude: 40.7128, longitude: -74.0060)
    @State private var testGPX: URL?

    var body: some View {
        TabView(selection: $selectedTab) {
            NavigationStack { signScreen.navigationTitle("OmnaSigner") }
                .tabItem { Label("Sign", systemImage: "checkmark.shield.fill") }.tag(0)
            NavigationStack { editorScreen.navigationTitle("Create & Preview") }
                .tabItem { Label("Editor", systemImage: "chevron.left.forwardslash.chevron.right") }.tag(1)
            NavigationStack { installScreen.navigationTitle("Install & Privacy") }
                .tabItem { Label("Install", systemImage: "iphone.gen3") }.tag(2)
            NavigationStack { appleMapScreen.navigationTitle("OmnaLocation") }
                .tabItem { Label("Map", systemImage: "map.fill") }.tag(3)
            NavigationStack { webStudioScreen.navigationTitle("OmnaSign Web App") }
                .tabItem { Label("Web App", systemImage: "globe") }.tag(4)
        }
        .tint(.cyan)
        .preferredColorScheme(.dark)
        .fileImporter(isPresented: $importing, allowedContentTypes: [.item], allowsMultipleSelection: false) { result in
            switch result {
            case .success(let urls):
                if let url = urls.first {
                    switch importKind {
                    case .ipa: signer.importAsset(url, kind: .ipa)
                    case .certificate: signer.importAsset(url, kind: .certificate)
                    case .profile: signer.importAsset(url, kind: .profile)
                    }
                }
            case .failure(let error): signer.alert = error.localizedDescription
            }
        }
        .alert("OmnaSigner", isPresented: Binding(get: { signer.alert != nil }, set: { if !$0 { signer.alert = nil } })) {
            Button("OK") { signer.alert = nil }
        } message: {
            Text(signer.alert ?? "")
        }
    }

    private var signScreen: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 17) {
                VStack(alignment: .leading, spacing: 9) {
                    Text("OMNA ECOSYSTEM").font(.caption.bold()).foregroundStyle(.cyan)
                    Text("Sign apps.\nOn your iPhone.").font(.system(size: 31, weight: .bold, design: .rounded))
                    Text("Your certificates stay local — no subscription required.")
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(22)
                .background(LinearGradient(colors: [.blue.opacity(0.35), .indigo.opacity(0.2)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 20))

                section("1  Import the IPA", icon: "square.and.arrow.down") {
                    fileItem("Original IPA", signer.ipa, button: "Choose .ipa") {
                        importKind = .ipa; importing = true
                    }
                    Text("Sign only apps that you are authorized to modify or install.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                section("2  Signing credentials", icon: "lock.shield") {
                    fileItem("Apple certificate", signer.certificate, button: "Choose .p12") {
                        importKind = .certificate; importing = true
                    }
                    fileItem("Provisioning profile", signer.profile, button: "Choose .mobileprovision") {
                        importKind = .profile; importing = true
                    }
                    TextField("Target bundle ID (com.example.app)", text: $signer.bundleID)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .padding(13)
                        .background(.white.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
                    Text("Must match an app ID authorized by your profile. Imported specific-app profiles prefill this value.")
                        .font(.caption).foregroundStyle(.secondary)
                    SecureField("Certificate password", text: $signer.password)
                        .textContentType(.password)
                        .padding(13)
                        .background(.white.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
                    Button("Delete imported signing credentials", role: .destructive) {
                        signer.clearCredentials()
                    }.font(.footnote)
                }
                section("3  Sign and export", icon: "checkmark.seal") {
                    Button(action: { signer.sign() }) {
                        HStack {
                            Spacer()
                            if signer.working { ProgressView().tint(.white) }
                            Text(signer.working ? "Signing…" : "Sign IPA locally").fontWeight(.bold)
                            Spacer()
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(.blue)
                    .disabled(!signer.canSign)
                    Text(signer.message).font(.footnote).foregroundStyle(.secondary)
                    if let signed = signer.signedIPA {
                        ShareLink(item: signed) {
                            Label("Save / Share signed IPA", systemImage: "square.and.arrow.up")
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(.cyan)
                    }
                }
            }.padding(16)
        }
    }

    private var editorScreen: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                Text("Make a web app").font(.title2.bold())
                Text("Edit HTML and preview it before building an iOS wrapper. This editor exports HTML; it doesn't decompile native Swift apps or produce a signed IPA.")
                    .font(.footnote).foregroundStyle(.secondary)
                HStack {
                    Button("Preview") { previewing = true }.buttonStyle(.borderedProminent)
                    Button("Export HTML") { exportHTML() }.buttonStyle(.bordered)
                    if let exportedHTML {
                        ShareLink(item: exportedHTML) { Label("Share", systemImage: "square.and.arrow.up") }
                    }
                }
                TextEditor(text: $draftHTML)
                    .font(.system(size: 12, design: .monospaced))
                    .frame(minHeight: 380)
                    .scrollContentBackground(.hidden)
                    .padding(8)
                    .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 14))
                Text("For a full IPA resource editor, use OmnaSign Web or Studio for Windows.")
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(16)
        }
        .sheet(isPresented: $previewing) {
            NavigationStack {
                HTMLPreview(source: draftHTML)
                    .ignoresSafeArea(edges: .bottom)
                    .toolbar {
                        ToolbarItem(placement: .topBarTrailing) {
                            Button("Done") { previewing = false }
                        }
                    }
                    .navigationTitle("Web preview")
                    .navigationBarTitleDisplayMode(.inline)
            }
        }
    }

    private var appleMapScreen: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Apple Maps · testing only").font(.title2.bold())
            Text("Tap the map to choose a coordinate for an app you develop. This does not change Find My, other apps, or the iPhone's real GPS location.")
                .font(.caption).foregroundStyle(.secondary)
            OmnaAppleMapPicker(position: $chosenLocation)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .clipShape(RoundedRectangle(cornerRadius: 14))
            Text(String(format: "Test coordinate: %.6f, %.6f", chosenLocation.latitude, chosenLocation.longitude))
                .font(.footnote.monospaced())
            HStack {
                Button("Export GPX for Xcode") { exportTestLocation() }
                    .buttonStyle(.borderedProminent)
                if let testGPX {
                    ShareLink(item: testGPX) { Label("Share GPX", systemImage: "square.and.arrow.up") }
                }
            }
            Text("GPX is for location simulation while debugging with Xcode; it cannot force a third-party app to report this position.")
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(14)
    }

    private func exportTestLocation() {
        let lat = String(format: "%.6f", chosenLocation.latitude)
        let lon = String(format: "%.6f", chosenLocation.longitude)
        let gpx = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><gpx version=\"1.1\" creator=\"OmnaSign\" xmlns=\"http://www.topografix.com/GPX/1/1\"><wpt lat=\"" + lat + "\" lon=\"" + lon + "\"><name>OmnaLocation test</name></wpt></gpx>"
        do {
            let url = FileManager.default.temporaryDirectory.appendingPathComponent("OmnaLocation-" + UUID().uuidString + ".gpx")
            try gpx.write(to: url, atomically: true, encoding: .utf8)
            testGPX = url
        } catch {
            signer.alert = "Could not export GPX: " + error.localizedDescription
        }
    }

    private var webStudioScreen: some View {
        VStack(spacing: 0) {
            Text("Online app studio · manual editing and OmnaAi")
                .font(.caption).foregroundStyle(.secondary)
                .padding(.vertical, 8)
            OmnaWebStudioView()
        }
    }

    private var installScreen: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 17) {
                section("Install a signed app", icon: "iphone.gen3") {
                    Text("An IPA signed here can be shared with a compatible sideloader. For eligible Apple over-the-air installation, first host the signed IPA and its manifest on a trusted HTTPS server.")
                        .font(.footnote).foregroundStyle(.secondary)
                    TextField("https://example.com/app/manifest.plist", text: $installManifest)
                        .textInputAutocapitalization(.never)
                        .keyboardType(.URL)
                        .autocorrectionDisabled()
                        .padding(13)
                        .background(.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 11))
                    Button("Open OTA installation") { openOTA() }
                        .buttonStyle(.borderedProminent)
                        .disabled(URL(string: installManifest)?.scheme?.lowercased() != "https")
                    Text("Apple may reject the install if the certificate, bundle ID, entitlements or device provisioning don't match.")
                        .font(.caption).foregroundStyle(.orange)
                }
                section("Privacy and limitations", icon: "hand.raised") {
                    Text("IPA signing runs on your phone using the RorkSign library. The OmnaSigner app does not send your .p12 private key or its password to our website.")
                    Text("Imported certificates are stored in the app sandbox with iOS file protection and can be deleted from the Sign tab.")
                    Text("Signatures are not a bypass for Apple's application installation rules. Unsigned apps cannot be directly installed.")
                }
            }.font(.footnote).padding(16)
        }
    }

    private func section<Content: View>(_ title: String, icon: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 13) {
            Label(title, systemImage: icon).font(.headline)
            content()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(17)
        .background(Color(red: 0.08, green: 0.12, blue: 0.20), in: RoundedRectangle(cornerRadius: 17))
    }

    private func fileItem(_ title: String, _ path: URL?, button: String, action: @escaping () -> Void) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.caption.bold()).foregroundStyle(.secondary)
            Text(path?.lastPathComponent ?? "Nothing selected")
                .font(.caption.monospaced())
                .lineLimit(2)
                .foregroundStyle(path == nil ? .secondary : .primary)
            Button(button, action: action).buttonStyle(.bordered)
        }
    }

    private func exportHTML() {
        do {
            let url = FileManager.default.temporaryDirectory.appendingPathComponent("OmnaApp-\(UUID().uuidString.prefix(8)).html")
            try draftHTML.write(to: url, atomically: true, encoding: .utf8)
            exportedHTML = url
        } catch {
            signer.alert = "Could not export HTML: \(error.localizedDescription)"
        }
    }

    private func openOTA() {
        guard let u = URL(string: installManifest), u.scheme?.lowercased() == "https" else { return }
        var allowed = CharacterSet.urlQueryAllowed
        allowed.remove(charactersIn: "&=+?")
        let escaped = u.absoluteString.addingPercentEncoding(withAllowedCharacters: allowed) ?? ""
        if let url = URL(string: "itms-services://?action=download-manifest&url=" + escaped) {
            UIApplication.shared.open(url)
        }
    }
}

struct HTMLPreview: UIViewRepresentable {
    let source: String
    func makeUIView(context: Context) -> WKWebView {
        let view = WKWebView(frame: .zero)
        view.isOpaque = false
        return view
    }
    func updateUIView(_ uiView: WKWebView, context: Context) {
        uiView.loadHTMLString(source, baseURL: nil)
    }
}

struct OmnaWebStudioView: UIViewRepresentable {
    func makeUIView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = true
        let web = WKWebView(frame: .zero, configuration: configuration)
        web.allowsBackForwardNavigationGestures = true
        if let url = URL(string: "https://omnasign-studio.vercel.app/#webapp/studio") {
            web.load(URLRequest(url: url))
        }
        return web
    }
    func updateUIView(_ uiView: WKWebView, context: Context) {}
}
