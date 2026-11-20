package com.example.dalanmobile.driver

import kotlin.math.*

internal object NavigationPolicy {
    const val COOLDOWN_MS = 30_000L
    const val OFF_ROUTE_METERS = 60.0
    fun distance(a: NavigationPoint, b: NavigationPoint): Double {
        val lat = Math.toRadians(b.latitude - a.latitude)
        val lon = Math.toRadians(b.longitude - a.longitude)
        val h = sin(lat / 2).pow(2) + cos(Math.toRadians(a.latitude)) * cos(Math.toRadians(b.latitude)) * sin(lon / 2).pow(2)
        return 6371000 * 2 * asin(sqrt(h.coerceIn(0.0, 1.0)))
    }
    fun distanceToRoute(fix: NavigationPoint, points: List<NavigationPoint>): Double {
        if (points.isEmpty()) return Double.POSITIVE_INFINITY
        if (points.size == 1) return distance(fix, points.first())
        val meters = Math.PI / 180 * 6371000
        val longitudeScale = cos(Math.toRadians(fix.latitude)) * meters
        fun longitudeDelta(value: Double) = ((value - fix.longitude + 540) % 360) - 180
        return points.zipWithNext().minOf { (a, b) ->
            val ax = longitudeDelta(a.longitude) * longitudeScale; val ay = (a.latitude - fix.latitude) * meters
            val bx = longitudeDelta(b.longitude) * longitudeScale; val by = (b.latitude - fix.latitude) * meters
            val dx = bx - ax; val dy = by - ay; val squared = dx * dx + dy * dy
            val fraction = if (squared == 0.0) 0.0 else (-(ax * dx + ay * dy) / squared).coerceIn(0.0, 1.0)
            hypot(ax + fraction * dx, ay + fraction * dy)
        }
    }
    fun shouldReroute(elapsedMs: Long, offRouteMeters: Double, hasRoute: Boolean): Boolean =
        elapsedMs >= COOLDOWN_MS && (!hasRoute || offRouteMeters > OFF_ROUTE_METERS)
}
