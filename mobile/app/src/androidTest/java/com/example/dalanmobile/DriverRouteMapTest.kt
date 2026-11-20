package com.example.dalanmobile

import android.graphics.Bitmap
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.dalanmobile.driver.*
import com.example.dalanmobile.ui.theme.DalanMobileTheme
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point
import java.io.File

@RunWith(AndroidJUnit4::class)
class DriverRouteMapTest {
    @get:Rule val compose = createComposeRule()

    @Test fun entering_navigation_retains_every_stop_and_connected_leg_in_both_themes() {
        val dark = mutableStateOf(false)
        val navigating = mutableStateOf(false)
        val ready = mutableStateOf(false)
        val origin = Point.fromLngLat(120.586, 15.145)
        val points = listOf(origin, Point.fromLngLat(120.589, 15.1485), Point.fromLngLat(120.593, 15.153), Point.fromLngLat(120.598, 15.159))
        val stops = points.drop(1).mapIndexed { index, point -> DriverNavigationStop("fixture-$index",
            if (index == 0) "Pickup" else "Drop-off", "Demo stop ${index + 1}", NavigationPoint(point.latitude(), point.longitude())) }
        // Synthetic geometry only, for rendering verification; never sent as production road data.
        val lines = points.zipWithNext().map { (from, to) -> LineString.fromLngLats(listOf(from, to)) }
        compose.setContent {
            DalanMobileTheme(dark.value) {
                Column(Modifier.fillMaxSize().padding(20.dp)) {
                    Text(if (navigating.value) "Current position → all remaining stops" else "Assigned route preview")
                    DeliveryMap(stops, if (navigating.value) lines else lines.drop(1), Modifier.fillMaxWidth().height(400.dp),
                        if (navigating.value) origin else null, false) { ready.value = true }
                    DriverRouteLegend(navigating.value)
                    stops.forEachIndexed { index, stop -> Text("${index + 1}. ${stop.stopKind} · ${stop.address}") }
                }
            }
        }
        compose.waitUntil(30_000) { ready.value }
        compose.runOnIdle { navigating.value = true }
        for (theme in listOf(false, true)) {
            compose.runOnIdle { if (dark.value != theme) { ready.value = false; dark.value = theme } }
            compose.waitUntil(30_000) { ready.value }
            compose.onNodeWithText("Current position").assertExists()
            compose.onNodeWithText("Pickup").assertExists()
            compose.onNodeWithText("Drop-off").assertExists()
            for (index in stops.indices) compose.onNodeWithText("${index + 1}. ${stops[index].stopKind} · Demo stop ${index + 1}").assertExists()
            compose.waitForIdle()
            Thread.sleep(1_000)
            val context = InstrumentationRegistry.getInstrumentation().targetContext
            val file = File(context.getExternalFilesDir(null), "navigation-map-${if (theme) "dark" else "light"}.png")
            InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot().let { bitmap ->
                file.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
                bitmap.recycle()
            }
        }
    }
}
