package com.example.dalanmobile.driver

import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point

internal fun navigationLines(navigation: DriverNavigationRoute, origin: NavigationPoint): List<LineString> {
    val stops = navigation.stops ?: listOf(DriverNavigationStop(navigation.deliveryId, navigation.stopKind,
        navigation.address, navigation.destination, navigation.route))
    var previous = origin
    return stops.map { stop ->
        val route = requireNotNull(stop.route)
        // Consecutive pickups at the same warehouse need no road-provider request.
        val line = if (route.encodedPolyline6.isEmpty() && route.distanceMeters == 0.0 && previous == stop.destination)
            LineString.fromLngLats(listOf(Point.fromLngLat(previous.longitude, previous.latitude),
                Point.fromLngLat(stop.destination.longitude, stop.destination.latitude)))
        else LineString.fromPolyline(route.encodedPolyline6, 6)
        require(line.coordinates().size >= 2)
        previous = stop.destination
        line
    }
}
