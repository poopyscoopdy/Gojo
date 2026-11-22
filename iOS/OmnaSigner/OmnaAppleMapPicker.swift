import SwiftUI
import MapKit
import CoreLocation

struct OmnaAppleMapPicker: UIViewRepresentable {
    @Binding var position: CLLocationCoordinate2D

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeUIView(context: Context) -> MKMapView {
        let map = MKMapView(frame: .zero)
        map.mapType = .standard
        map.showsUserLocation = false
        map.isRotateEnabled = true
        map.setRegion(MKCoordinateRegion(center: position, span: MKCoordinateSpan(latitudeDelta: 0.45, longitudeDelta: 0.45)), animated: false)
        let tap = UITapGestureRecognizer(target: context.coordinator, action: #selector(Coordinator.tapped(_:)))
        tap.cancelsTouchesInView = false
        map.addGestureRecognizer(tap)
        context.coordinator.mapView = map
        return map
    }

    func updateUIView(_ map: MKMapView, context: Context) {
        context.coordinator.parent = self
        map.removeAnnotations(map.annotations)
        let pin = MKPointAnnotation()
        pin.coordinate = position
        pin.title = "OmnaLocation test point"
        map.addAnnotation(pin)
    }

    final class Coordinator: NSObject {
        var parent: OmnaAppleMapPicker
        weak var mapView: MKMapView?

        init(_ parent: OmnaAppleMapPicker) { self.parent = parent }

        @objc func tapped(_ gesture: UITapGestureRecognizer) {
            guard gesture.state == .ended, let map = mapView else { return }
            let point = gesture.location(in: map)
            parent.position = map.convert(point, toCoordinateFrom: map)
        }
    }
}
