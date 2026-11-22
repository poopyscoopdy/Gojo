import SwiftUI

@main
struct OmnaSignerApp: App {
    @StateObject private var model = SigningModel()

    var body: some Scene {
        WindowGroup {
            MainView()
                .environmentObject(model)
                .preferredColorScheme(.dark)
        }
    }
}
