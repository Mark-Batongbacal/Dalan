package com.example.dalanmobile

import com.example.dalanmobile.driver.*
import kotlinx.serialization.encodeToString
import org.junit.Assert.*
import org.junit.Test
import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point

class DriverNavigationTest {
    private fun task(id: String, status: String, pickup: Int, dropoff: Int) = TaskInfo(id, id, "Depot", "Stop", status,
        "2026-11-20T10:00:00Z", "route", pickup, dropoff, 15.0, 120.0, 15.01, 120.01)

    @Test fun all_remaining_stops_follow_confirmed_order_and_exclude_completed_pickups() {
        val stops = NavigationPolicy.remainingStops(listOf(task("a", "PickedUp", 1, 3), task("b", "Assigned", 2, 4), task("c", "Delivered", 5, 6)))
        assertEquals(listOf("b", "a", "b"), stops.map { it.deliveryId })
        assertEquals(listOf("Pickup", "Drop-off", "Drop-off"), stops.map { it.stopKind })
    }

    @Test fun missing_route_order_keeps_each_pickup_before_its_dropoff() {
        val stops = NavigationPolicy.remainingStops(listOf(task("a", "Assigned", 1, 2).copy(pickupPosition = null, dropoffPosition = null)))
        assertEquals(listOf("Pickup", "Drop-off"), stops.map { it.stopKind })
    }

    @Test fun full_route_is_requested_and_old_server_response_remains_readable() {
        assertTrue(driverJson.encodeToString(DriverNavigationRequest(NavigationPoint(15.0, 120.0), "2026-11-20T10:00:00Z", 8.0, includeRemainingStops = true))
            .contains("\"includeRemainingStops\":true"))
        val old = """{"deliveryId":"a","stopKind":"Pickup","address":"Depot","destination":{"latitude":15,"longitude":120},"route":{"distanceMeters":100,"durationSeconds":10,"encodedPolyline6":"test"}}"""
        assertNull(driverJson.decodeFromString<DriverNavigationRoute>(old).stops)
    }

    @Test fun map_draws_every_leg_from_current_position_instead_of_only_the_next_stop() {
        val origin = NavigationPoint(15.0, 120.0)
        val first = NavigationPoint(15.001, 120.0)
        val second = NavigationPoint(15.002, 120.0)
        fun road(from: NavigationPoint, to: NavigationPoint) = DriverRoadRoute(100.0, 10.0,
            LineString.fromLngLats(listOf(Point.fromLngLat(from.longitude, from.latitude), Point.fromLngLat(to.longitude, to.latitude))).toPolyline(6))
        val stops = listOf(DriverNavigationStop("a", "Pickup", "Depot", first, road(origin, first)),
            DriverNavigationStop("a", "Drop-off", "Stop", second, road(first, second)))
        val lines = navigationLines(DriverNavigationRoute("a", "Pickup", "Depot", first, stops[0].route!!, stops), origin)
        assertEquals(2, lines.size)
        assertEquals(15.0, lines.first().coordinates().first().latitude(), .000001)
        assertEquals(lines.first().coordinates().last(), lines.last().coordinates().first())
        assertEquals(15.002, lines.last().coordinates().last().latitude(), .000001)
    }

    @Test fun coincident_pickups_are_retained_and_invalid_empty_geometry_is_not_drawn_as_a_road() {
        val point = NavigationPoint(15.0, 120.0)
        val route = DriverRoadRoute(0.0, 0.0, "")
        val navigation = DriverNavigationRoute("a", "Pickup", "Depot", point, route,
            listOf(DriverNavigationStop("a", "Pickup", "Depot", point, route)))
        assertEquals(2, navigationLines(navigation, point).single().coordinates().size)
        assertThrows(IllegalArgumentException::class.java) { navigationLines(navigation, NavigationPoint(16.0, 120.0)) }
    }
}
