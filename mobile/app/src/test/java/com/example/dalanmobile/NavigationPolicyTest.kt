package com.example.dalanmobile
import com.example.dalanmobile.driver.NavigationPoint
import com.example.dalanmobile.driver.NavigationPolicy
import org.junit.Assert.*
import org.junit.Test
class NavigationPolicyTest {
    @Test fun distance_uses_segments_not_only_vertices() {
        val route = listOf(NavigationPoint(14.0,121.0), NavigationPoint(14.01,121.0))
        assertTrue(NavigationPolicy.distanceToRoute(NavigationPoint(14.005,121.0), route) < 1)
        assertTrue(NavigationPolicy.distanceToRoute(NavigationPoint(14.005,121.002), route) > 200)
    }
    @Test fun reroutes_are_bounded_and_do_not_fire_for_every_position() {
        assertFalse(NavigationPolicy.shouldReroute(29_999, 200.0, true))
        assertFalse(NavigationPolicy.shouldReroute(30_000, 10.0, true))
        assertTrue(NavigationPolicy.shouldReroute(30_000, 100.0, true))
        assertTrue(NavigationPolicy.shouldReroute(30_000, 0.0, false))
    }
}
