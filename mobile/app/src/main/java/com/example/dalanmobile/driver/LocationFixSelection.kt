package com.example.dalanmobile.driver

import android.location.Location

internal suspend fun selectLocationFix(providers: List<String>, maximumAccuracyMeters: Float,
    fetch: suspend (String) -> Location?): Location? {
    var best: Location? = null
    for (provider in providers) {
        val fix = fetch(provider) ?: continue
        if (!fix.hasAccuracy() || !fix.accuracy.isFinite() || fix.accuracy < 0 ||
            !fix.latitude.isFinite() || fix.latitude !in -90.0..90.0 ||
            !fix.longitude.isFinite() || fix.longitude !in -180.0..180.0 ||
            System.currentTimeMillis() - fix.time !in 0..120_000) continue
        if (best == null || fix.accuracy < best.accuracy) best = fix
        if (fix.accuracy <= maximumAccuracyMeters) return fix
    }
    return best
}
